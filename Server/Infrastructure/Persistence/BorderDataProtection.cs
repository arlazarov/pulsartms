using Application.Features.Border.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace Infrastructure.Persistence;

public sealed class BorderDataProtection(IDataProtectionProvider provider)
  : IBorderDataProtection
{
  private IDataProtector Protector(Guid id) =>
    provider.CreateProtector("PulsR.Border.Private.v1", id.ToString("N"));

  public string Protect(Guid crossingId, string value) =>
    Protector(crossingId).Protect(value);

  public string Unprotect(Guid crossingId, string value) =>
    Protector(crossingId).Unprotect(value);
}
