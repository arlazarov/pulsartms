using System.Text.Json;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Models;
using Infrastructure.Persistence;

namespace Infrastructure.Synchronization;

public sealed class SynchronizationStore(AppDbContext db)
  : ISynchronizationStore
{
  public static readonly Guid Id = new("6acb468e-c923-481f-b9c9-6363febf4c0a");
  private readonly CheckpointLeaseStore checkpoint = new(db, Id);
  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );

  public Task<bool> AcquireAsync(
    string owner,
    DateTime now,
    CancellationToken ct
  ) => checkpoint.AcquireAsync(owner, now, ct);

  public Task<bool> RenewAsync(
    string owner,
    DateTime now,
    CancellationToken ct
  ) => checkpoint.RenewAsync(owner, now, ct);

  public Task SaveAsync(string owner, string json, CancellationToken ct) =>
    checkpoint.SaveAsync(owner, json, ct);

  public Task ReleaseAsync(string owner, CancellationToken ct) =>
    checkpoint.ReleaseAsync(owner, ct);

  public async Task<SynchronizationState> ReadAsync(CancellationToken ct)
  {
    var json = await checkpoint.ReadAsync(ct);
    return json is null
      ? new()
      : JsonSerializer.Deserialize<SynchronizationState>(json, Json) ?? new();
  }
}
