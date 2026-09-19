using System.Text.Json;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Infrastructure.Persistence;
using Infrastructure.Synchronization;

namespace Infrastructure.Integrations.Google.Gmail;

public sealed class GmailWatchStore(AppDbContext db) : IGmailWatchStore
{
  public static readonly Guid Id = new("71067801-256e-47e1-b30b-4a559f0aac63");
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

  public Task SaveAsync(
    string owner,
    GmailWatchState state,
    CancellationToken ct
  ) => checkpoint.SaveAsync(owner, JsonSerializer.Serialize(state, Json), ct);

  public async Task<GmailWatchState?> ReadAsync(CancellationToken ct)
  {
    var json = await checkpoint.ReadAsync(ct);
    var state = json is null
      ? null
      : JsonSerializer.Deserialize<GmailWatchState>(json, Json);
    return state is null || state.RegisteredAt == default ? null : state;
  }
}
