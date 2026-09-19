namespace Client.Models.DTO;

public record UserDTO(
  Guid Id,
  string Name,
  string Email,
  bool IsActive,
  string Role = "Admin"
);
