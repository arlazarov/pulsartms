namespace Application.Interfaces;

// The deployed revision this process belongs to, as the platform names
// it; null when nothing names it or the name is not one the platform
// gives. A release required of a nameless revision is never given.
public interface IDeploymentRevision
{
  string? Name { get; }
}
