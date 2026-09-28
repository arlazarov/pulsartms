namespace Application.Interfaces;

// Who operates this deployment of the service: the identities its
// configuration names, apart from any carrier's roles. What they may do
// reaches every carrier at once, so a carrier's Admin alone may not.
public interface IDeploymentOperators
{
  bool Includes(string? identityUserId);
}
