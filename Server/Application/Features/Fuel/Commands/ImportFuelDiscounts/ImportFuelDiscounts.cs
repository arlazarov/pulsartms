using Application.Caching;
using Application.Features.Fuel.Exceptions;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Application.Models;
using Domain.Entities.Fuel;
using Microsoft.Extensions.Logging;

namespace Application.Features.Fuel.Commands.ImportFuelDiscounts;

public record ImportFuelDiscountsCommand : IRequest<RequestResponse<int>>;

public class ImportFuelDiscountsHandler(
  IAppDbContext dbContext,
  IFuelDiscountProvider fuelDiscountProvider,
  FuelStationLookupService lookups,
  ReadCache reads,
  TimeProvider time,
  FuelImportSkips skips,
  ILogger<ImportFuelDiscountsHandler> logger
) : IRequestHandler<ImportFuelDiscountsCommand, RequestResponse<int>>
{
  // The mailbox is read from two days before the last import, so an outage
  // up to Furthest loses no message; one longer does lose the messages
  // received more than Furthest ago.
  public static readonly TimeSpan Overlap = TimeSpan.FromDays(2);
  public static readonly TimeSpan Furthest = TimeSpan.FromDays(30);

  public async Task<RequestResponse<int>> Handle(
    ImportFuelDiscountsCommand request,
    CancellationToken cancellationToken
  )
  {
    var importedMessageIds = await dbContext
      .FuelImportSources.AsNoTracking()
      .Select(x => x.GmailMessageId)
      .ToListAsync(cancellationToken);
    var now = time.GetUtcNow().UtcDateTime;
    var last = await dbContext.FuelImportSources.MaxAsync(
      x => (DateTime?)x.ImportedAt,
      cancellationToken
    );
    var since = Since(now, last);
    var imports = await fuelDiscountProvider.GetDiscountsAsync(
      importedMessageIds,
      since,
      cancellationToken
    );
    var count = 0;

    foreach (var message in imports.GroupBy(x => x.MessageId))
    {
      if (
        importedMessageIds.Contains(
          message.Key,
          StringComparer.OrdinalIgnoreCase
        )
      )
        continue;
      // One message's bad attachment skips that message only (audit F20).
      if (
        string.IsNullOrWhiteSpace(message.Key)
        || message.Any(x =>
          x.Unreadable || x.Rows.Count == 0 || x.EffectiveDate == default
        )
      )
      {
        if (skips.First(message.Key))
          logger.LogWarning(
            "Fuel import skipped message {MessageId}: {Reason}",
            message.Key,
            message.Any(x => x.Unreadable) ? "unreadable" : "empty"
          );
        continue;
      }

      IReadOnlyDictionary<
        (string StationId, string Query),
        FuelStationLookupResult
      > prepared;
      try
      {
        prepared = await FuelStationSync.PrepareAsync(
          dbContext,
          lookups,
          message.SelectMany(x => x.Rows).ToArray(),
          cancellationToken
        );
      }
      catch (FuelStationLookupDeferredException ex)
      {
        return RequestResponse<int>.Fail(ex.Message, 503);
      }

      await using var transaction =
        await dbContext.Database.BeginTransactionAsync(cancellationToken);
      await dbContext.LockFuelImportAsync(cancellationToken);

      if (
        await dbContext.FuelImportSources.AnyAsync(
          x => x.GmailMessageId == message.Key,
          cancellationToken
        )
      )
      {
        await transaction.CommitAsync(cancellationToken);
        continue;
      }

      foreach (var import in message.OrderBy(x => x.EffectiveDate))
      {
        try
        {
          await FuelStationSync.SyncAsync(
            dbContext,
            lookups,
            prepared,
            import.Rows,
            cancellationToken
          );
        }
        catch (FuelStationLookupDeferredException ex)
        {
          return RequestResponse<int>.Fail(ex.Message, 503);
        }
        count += await dbContext.SaveChangesAsync(cancellationToken);
        await FuelDiscountSync.SyncAsync(dbContext, import, cancellationToken);
        count += await dbContext.SaveChangesAsync(cancellationToken);
      }

      var attachmentNames = string.Join(
        ", ",
        message.Select(x => x.AttachmentName).Distinct()
      );
      dbContext.FuelImportSources.Add(
        new FuelImportSource
        {
          Id = Guid.NewGuid(),
          GmailMessageId = message.Key,
          AttachmentName =
            attachmentNames.Length > 500
              ? attachmentNames[..500]
              : attachmentNames,
          ImportedAt = DateTime.UtcNow,
        }
      );
      count += await dbContext.SaveChangesAsync(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
      reads.Invalidate(ReadGroups.Fuel);
    }
    return RequestResponse<int>.Ok(count);
  }

  internal static DateTime Since(DateTime now, DateTime? lastImport)
  {
    var since = (lastImport ?? now) - Overlap;
    return since > now - Overlap ? now - Overlap
      : since < now - Furthest ? now - Furthest
      : since;
  }
}
