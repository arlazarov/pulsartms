namespace Application.Features.Routing.Models;

public sealed record RoutePoint(double Latitude, double Longitude)
{
  [System.Text.Json.Serialization.JsonIgnore]
  public bool IsValid => double.IsFinite(Latitude) && double.IsFinite(Longitude)
    && Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180
    && (Latitude != 0 || Longitude != 0);
}
