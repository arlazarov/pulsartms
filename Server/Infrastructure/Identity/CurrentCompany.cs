using System.Security.Claims;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace Infrastructure.Identity;

public sealed class CurrentCompany(IHttpContextAccessor accessor)
  : ICurrentCompany
{
  // The claim the carrier travels in. It is put into the token when a
  // person signs in, so it cannot be chosen by whoever is asking.
  public const string Claim = "company";

  private Guid? chosen;

  public Guid? Id => chosen ?? FromTheToken();

  // Background work says whose pass it is running. Until it does, it sees
  // nothing - a pass that forgot would otherwise read every carrier's
  // loads and write one carrier's answers onto another's.
  public IDisposable As(Guid company)
  {
    var previous = chosen;
    chosen = company;
    return new Restore(this, previous);
  }

  private Guid? FromTheToken() =>
    accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
    && Guid.TryParse(user.FindFirstValue(Claim), out var company)
      ? company
      : null;

  private sealed class Restore(CurrentCompany current, Guid? previous)
    : IDisposable
  {
    public void Dispose() => current.chosen = previous;
  }
}
