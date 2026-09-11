namespace Application.Features.Users.Models;

public record UserDto(Guid Id, string Name, string Email, bool IsActive, string Role = "Admin");
