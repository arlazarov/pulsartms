namespace Application.Interfaces;

// Identity of the authenticated caller, not a cached application profile.
public interface ICurrentUser
{
  bool IsAuthenticated { get; }
  string? IdentityUserId { get; }
}
