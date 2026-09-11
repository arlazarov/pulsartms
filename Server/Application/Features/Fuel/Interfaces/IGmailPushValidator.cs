namespace Application.Features.Fuel.Interfaces;

public interface IGmailPushValidator
{
  bool IsConfigured { get; }
  Task<bool> ValidateAsync(string authorization);
  bool IsExpectedMailbox(string? email);
}
