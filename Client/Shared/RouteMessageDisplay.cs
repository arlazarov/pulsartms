namespace Client.Shared;

public static class RouteMessageDisplay
{
  public static string? Concise(string? message) =>
    message
      is "The address correction needs confirmation of the street and building number; no city-center fallback was used."
        or "The stop needs an unambiguous street-level address; no city-center fallback was used."
      ? "Check the stop address."
      : message;

  public static string? For(string? message, bool hasValidRoute) =>
    hasValidRoute
    && message
      is (
        "Route update queued."
        or "Route update pending."
        or "Route service is temporarily unavailable. Retrying automatically."
        or "Route is temporarily unavailable. Retrying automatically."
        or "Route could not be displayed. Retrying automatically."
        or "Automatic route update paused by the truck request budget. The saved route is retained."
      )
      ? null
      : Concise(message);
}
