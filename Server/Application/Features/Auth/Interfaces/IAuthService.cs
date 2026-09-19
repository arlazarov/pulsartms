namespace Application.Features.Auth.Interfaces;

public interface IAuthService
{
  Task<bool> LoginAsync(
    string email,
    string password,
    CancellationToken cancellationToken = default
  );

  Task<bool> RefreshAsync(
    string refreshToken,
    CancellationToken cancellationToken = default
  );

  Task LogoutAsync();
}
