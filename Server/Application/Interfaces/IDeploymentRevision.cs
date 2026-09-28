namespace Application.Interfaces;

// The deployed revision this process belongs to, as the platform names it
// ("local" where nothing names one).
public interface IDeploymentRevision
{
  string Name { get; }
}
