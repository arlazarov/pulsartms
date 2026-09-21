using System.Security.Claims;
using Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Infrastructure.Identity;

// One for the whole server, not one per scope. A background pass says whose
// it is once, and then the work it starts opens scopes of its own - a
// handler sent through the mediator, a store resolved inside it. Held in a
// scoped field, the choice stayed behind in the scope that made it and
// everything beneath read an empty database. Held in the flow of the work
// itself, it goes wherever the work goes, and the shared read cache - which
// lives outside every scope - can ask the same question.
public sealed class CurrentCompany(IHttpContextAccessor accessor)
  : ICurrentCompany
{
  // The claim the carrier travels in. It is put into the token when a
  // person signs in, so it cannot be chosen by whoever is asking.
  public const string Claim = "company";

  private static readonly AsyncLocal<Guid?> Chosen = new();

  public Guid? Id => Chosen.Value ?? FromTheToken();

  // Background work says whose pass it is running. Until it does, it sees
  // nothing - a pass that forgot would otherwise read every carrier's
  // loads and write one carrier's answers onto another's.
  public IDisposable As(Guid company)
  {
    var previous = Chosen.Value;
    Chosen.Value = company;
    return new Restore(previous);
  }

  private Guid? FromTheToken() =>
    accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
    && Guid.TryParse(user.FindFirstValue(Claim), out var company)
      ? company
      : null;

  private sealed class Restore(Guid? previous) : IDisposable
  {
    public void Dispose() => Chosen.Value = previous;
  }
}
