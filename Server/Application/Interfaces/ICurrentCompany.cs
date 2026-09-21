namespace Application.Interfaces;

// Which carrier is being served right now.
//
// For a request, that is the carrier the signed-in person belongs to. For
// background work there is nobody signed in, so the work says which
// carrier it is doing a pass for - and it has to say, because a pass that
// ran for "everyone" would write one carrier's mileage onto another's
// loads.
public interface ICurrentCompany
{
  // Null only before anyone has been identified: a request that has not
  // authenticated yet, or background work that has not yet chosen whose
  // pass it is running. Reading company-owned rows in that state returns
  // nothing rather than everything.
  Guid? Id { get; }

  // Run something as one carrier. Background work wraps a pass in this.
  IDisposable As(Guid company);
}
