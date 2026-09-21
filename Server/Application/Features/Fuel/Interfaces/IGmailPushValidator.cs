namespace Application.Features.Fuel.Interfaces;

public interface IGmailPushValidator
{
  Guid CompanyId { get; }
  bool IsConfigured { get; }
  Task<bool> ValidateAsync(string authorization);
  bool IsExpectedMailbox(string? email);
}
