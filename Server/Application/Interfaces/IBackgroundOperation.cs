namespace Application.Interfaces;

// Infrastructure's generic worker hosts these; each feature owns its own
// marker so the worker contract does not tie features to one another.
public interface IBackgroundOperation
{
  Task RunAsync(CancellationToken cancellationToken);
}
