using Application.Models;

namespace Application.Interfaces;

public record IdentityServiceResult(bool Success, string? UserId, ValidationErrors? Errors = null);

public interface IIdentityService
{
  Task<IdentityServiceResult> CreateUserAsync(
    string email,
    string password,
    CancellationToken cancellationToken = default
  );

  Task<IdentityServiceResult> UpdateEmailAsync(
    string identityUserId,
    string email,
    CancellationToken cancellationToken = default
  );

  Task<IdentityServiceResult> UpdatePasswordAsync(
    string identityUserId,
    string password,
    CancellationToken cancellationToken = default
  );

  Task<IdentityServiceResult> SetActiveAsync(
    string identityUserId,
    bool isActive,
    CancellationToken cancellationToken = default
  );

  Task<IdentityServiceResult> DeleteUserAsync(
    string identityUserId,
    CancellationToken cancellationToken = default
  );
}
