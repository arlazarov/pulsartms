namespace Client.Models.Auth;

public record AuthResponse(
  string TokenType,
  string AccessToken,
  int ExpiresIn,
  string RefreshToken
);
