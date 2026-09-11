using Application.Features.Integrations.Models;

namespace Application.Features.Integrations.Interfaces;

public interface IIntegrationCredentials
{
  Task<IntegrationCredentialValues> GetAsync(string provider, CancellationToken ct);
}

public interface IIntegrationDeploymentCredentials
{
  IntegrationCredentialValues Get(string provider);
}

public interface IIntegrationCredentialStore
{
  Task<StoredIntegrationCredentials> ReadAsync(string provider, CancellationToken ct);
  Task<bool> TryWriteAsync(string provider, long expectedRevision, IntegrationCredentialValues? values, CancellationToken ct);
}
