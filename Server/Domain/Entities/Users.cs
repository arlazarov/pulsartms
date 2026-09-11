namespace Domain.Entities;

public class User : BaseEntity
{
  public string IdentityUserId { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Email { get; set; } = string.Empty;
  public bool IsActive { get; set; } = true;
}
