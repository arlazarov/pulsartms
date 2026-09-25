using Application.Features.Integrations.Models;

namespace Application.Features.Integrations.Interfaces;

public interface IIntegrationCredentials
{
  Task<IntegrationCredentialValues> GetAsync(
    string provider,
    CancellationToken ct
  );
}

public interface IIntegrationDeploymentCredentials
{
  IntegrationCredentialValues Get(string provider);
}

public interface IIntegrationCredentialStore
{
  Task<StoredIntegrationCredentials> ReadAsync(
    string provider,
    CancellationToken ct
  );
  Task<bool> TryWriteAsync(
    string provider,
    long expectedRevision,
    IntegrationCredentialValues? values,
    CancellationToken ct
  );

  // Whether another company's saved credentials for the provider hold this
  // value in the field. Answers only yes or no; nothing of the other
  // company's credentials leaves the store.
  Task<bool> HeldElsewhereAsync(
    string provider,
    string field,
    string value,
    CancellationToken ct
  );
}
