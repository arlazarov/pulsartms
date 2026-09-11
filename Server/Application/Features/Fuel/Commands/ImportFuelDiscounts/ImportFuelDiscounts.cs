using Application.Caching;
using Application.Features.Fuel.Interfaces;
using Application.Models;
using Domain.Entities.Fuel;
using Application.Features.Fuel.Services;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Exceptions;
using Application.Features.Synchronization.Services;

namespace Application.Features.Fuel.Commands.ImportFuelDiscounts;

public record ImportFuelDiscountsCommand : IRequest<RequestResponse<int>>;

public class ImportFuelDiscountsHandler(
  IAppDbContext dbContext,
  IFuelDiscountProvider fuelDiscountProvider,
  FuelStationLookupService lookups,
  ReadCache reads
) : IRequestHandler<ImportFuelDiscountsCommand, RequestResponse<int>>
{
  public async Task<RequestResponse<int>> Handle(
    ImportFuelDiscountsCommand request, CancellationToken cancellationToken)
  {
    var importedMessageIds = await dbContext.FuelImportSources.AsNoTracking()
      .Select(x => x.GmailMessageId).ToListAsync(cancellationToken);
    var imports = await fuelDiscountProvider.GetDiscountsAsync(importedMessageIds, cancellationToken);
    var count = 0;

    foreach (var message in imports.GroupBy(x => x.MessageId))
    {
      if (importedMessageIds.Contains(message.Key, StringComparer.OrdinalIgnoreCase)) continue;
      if (string.IsNullOrWhiteSpace(message.Key)
        || message.Any(x => x.Rows.Count == 0 || x.EffectiveDate == default))
        throw new InvalidOperationException("Fuel import contains an empty or invalid attachment.");

      IReadOnlyDictionary<(string StationId, string Query), FuelStationLookupResult> prepared;
      try
      {
        prepared = await FuelStationSync.PrepareAsync(dbContext, lookups, message.SelectMany(x => x.Rows).ToArray(), cancellationToken);
      }
      catch (FuelStationLookupDeferredException ex) { return RequestResponse<int>.Fail(ex.Message, 503); }

      await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
      await dbContext.LockFuelImportAsync(cancellationToken);

      if (await dbContext.FuelImportSources.AnyAsync(
        x => x.GmailMessageId == message.Key, cancellationToken))
      {
        await transaction.CommitAsync(cancellationToken);
        continue;
      }

      foreach (var import in message.OrderBy(x => x.EffectiveDate))
      {
        try { await FuelStationSync.SyncAsync(dbContext, lookups, prepared, import.Rows, cancellationToken); }
        catch (FuelStationLookupDeferredException ex) { return RequestResponse<int>.Fail(ex.Message, 503); }
        count += await dbContext.SaveChangesAsync(cancellationToken);
        await FuelDiscountSync.SyncAsync(dbContext, import, cancellationToken);
        count += await dbContext.SaveChangesAsync(cancellationToken);
      }

      var attachmentNames = string.Join(", ", message.Select(x => x.AttachmentName).Distinct());
      dbContext.FuelImportSources.Add(new FuelImportSource
      {
        Id = Guid.NewGuid(), GmailMessageId = message.Key,
        AttachmentName = attachmentNames.Length > 500 ? attachmentNames[..500] : attachmentNames,
        ImportedAt = DateTime.UtcNow,
      });
      count += await dbContext.SaveChangesAsync(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
      reads.Invalidate("fuel");
    }
    return RequestResponse<int>.Ok(count);
  }
}
