using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using System.Collections.Concurrent;

namespace Server.Tests.Support;

public sealed class StubProviderCredentials(params (string Field, string Value)[] fields) : IIntegrationCredentials
{
  public IntegrationCredentialValues Values { get; set; } = new(fields.Select(field => KeyValuePair.Create(field.Field, field.Value)));
  public ConcurrentQueue<string> Requests { get; } = new();

  public Task<IntegrationCredentialValues> GetAsync(string provider, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    Requests.Enqueue(provider);
    return Task.FromResult(Values);
  }
}
