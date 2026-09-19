namespace Application.Options;

// All: one instance serves requests and runs every worker (the default single-instance deployment).
// Workers: runs every worker; requests are still served. Api: serves requests only and follows the
// owner's synchronization checkpoint instead of taking the lease, so a request tier can scale out.
public enum HostingRole { All, Api, Workers }

public sealed class HostingOptions
{
  public HostingRole Role { get; set; } = HostingRole.All;
}
