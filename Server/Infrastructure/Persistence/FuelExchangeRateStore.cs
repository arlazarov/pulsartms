using System.Text.Json;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Routing.Interfaces;
using Infrastructure.Synchronization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

public sealed class FuelExchangeRateStore(
  AppDbContext db,
  IPlanningPublicationScope publication,
  ILogger<FuelExchangeRateStore> logger
) : IFuelExchangeRateStore
{
  private const int MaximumStateLength = 4096;
  public static readonly Guid Id = new("5ea9c4aa-c4d8-4d6a-bb82-e0be7a6ab86c");
  private readonly CheckpointLeaseStore checkpoint = new(db, Id);
  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );

  public Task<bool> AcquireAsync(
    string owner,
    DateTime now,
    CancellationToken ct
  ) => checkpoint.AcquireAsync(owner, now, ct);

  public Task ReleaseAsync(string owner, CancellationToken ct) =>
    checkpoint.ReleaseAsync(owner, ct);

  public async Task SaveAsync(
    string owner,
    FuelExchangeRate rate,
    CancellationToken ct
  )
  {
    // Join result publication without locking unrelated checkpoint rows.
    await using var transaction = await publication.BeginAsync(null, ct);
    await checkpoint.SaveAsync(owner, JsonSerializer.Serialize(rate, Json), ct);
    await transaction.CommitAsync(ct);
  }

  public async Task<FuelExchangeRate?> ReadAsync(CancellationToken ct)
  {
    var json = await db
      .SynchronizationCheckpoints.Where(x =>
        x.Id == Id && x.StateJson.Length <= MaximumStateLength
      )
      .Select(x => x.StateJson)
      .SingleOrDefaultAsync(ct);
    if (string.IsNullOrWhiteSpace(json))
      return null;
    try
    {
      var rate = JsonSerializer.Deserialize<FuelExchangeRate>(json, Json);
      return rate is null || rate.RetrievedAt == default ? null : rate;
    }
    catch (JsonException ex)
    {
      logger.LogWarning(ex, "Discarding an unreadable stored exchange rate");
      return null;
    }
  }
}
