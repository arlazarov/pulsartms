using System.Net;
using Application.Features.Routing.Exceptions;

namespace Infrastructure.Integrations.TomTom;

// What a refused request means and when it is worth asking again. A key
// TomTom rejects will be rejected for the next hour too; a busy or failing
// provider is worth five minutes; anything else was the request's own
// fault, and asking again will not change the answer.
public static class TomTomFailure
{
  public static RoutePlanningException From(HttpStatusCode status, DateTime now)
  {
    var rejected =
      status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    return new(
      rejected
        ? "TomTom rejected this key or Routing is not enabled for this key."
        : $"TomTom request failed (HTTP {(int)status}). The saved plan has been kept.",
      rejected ? now.AddHours(1)
        : (int)status >= 500
        || status
          is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
          ? now.AddMinutes(5)
        : null
    );
  }
}
