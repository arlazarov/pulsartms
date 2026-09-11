using Application.Features.Eta.Interfaces;
using Application.Features.Routing.Models;
using GeoTimeZone;
namespace Infrastructure.Eta;
public sealed class RouteRegionLookup : IRouteRegionLookup
{
  private static readonly HashSet<string> Canada = new(StringComparer.Ordinal) {
    "America/St_Johns", "America/Halifax", "America/Glace_Bay", "America/Moncton", "America/Goose_Bay", "America/Blanc-Sablon",
    "America/Toronto", "America/Montreal", "America/Nipigon", "America/Thunder_Bay", "America/Iqaluit", "America/Pangnirtung", "America/Atikokan", "America/Coral_Harbour",
    "America/Winnipeg", "America/Rainy_River", "America/Resolute", "America/Rankin_Inlet", "America/Regina", "America/Swift_Current",
    "America/Edmonton", "America/Cambridge_Bay", "America/Yellowknife", "America/Inuvik", "America/Creston", "America/Dawson_Creek", "America/Fort_Nelson",
    "America/Vancouver", "America/Whitehorse", "America/Dawson" };
  private static readonly HashSet<string> Usa = new(StringComparer.Ordinal) {
    "America/New_York", "America/Detroit", "America/Kentucky/Louisville", "America/Kentucky/Monticello", "America/Indiana/Indianapolis",
    "America/Indiana/Vincennes", "America/Indiana/Winamac", "America/Indiana/Marengo", "America/Indiana/Petersburg", "America/Indiana/Vevay",
    "America/Chicago", "America/Indiana/Tell_City", "America/Indiana/Knox", "America/Menominee", "America/North_Dakota/Center",
    "America/North_Dakota/New_Salem", "America/North_Dakota/Beulah", "America/Denver", "America/Boise", "America/Phoenix", "America/Los_Angeles",
    "America/Anchorage", "America/Juneau", "America/Sitka", "America/Metlakatla", "America/Yakutat", "America/Nome", "America/Adak", "Pacific/Honolulu" };
  public RouteRegion Find(RoutePoint point)
  {
    var zone = TimeZoneLookup.GetTimeZone(point.Latitude, point.Longitude).Result;
    return new(Canada.Contains(zone) ? "CA" : Usa.Contains(zone) ? "US" : "", zone, point.Latitude >= 60);
  }
}
