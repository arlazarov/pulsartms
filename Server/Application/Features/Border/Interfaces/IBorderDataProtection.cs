namespace Application.Features.Border.Interfaces;

public interface IBorderDataProtection
{
  string Protect(Guid crossingId, string value);
  string Unprotect(Guid crossingId, string value);
}
