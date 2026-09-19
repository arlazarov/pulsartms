using System.Globalization;

namespace Client.Models;

public sealed record DisplayUnits(
  string Temperature = "celsius",
  string Distance = "both"
)
{
  public static DisplayUnits Default { get; } = new();

  public DisplayUnits Normalize() =>
    new(
      Temperature is "fahrenheit" ? "fahrenheit" : "celsius",
      Distance is "miles" or "kilometers" ? Distance : "both"
    );

  public bool BothDistances => Distance == "both";
  public string DistanceUnit => Distance == "kilometers" ? "km" : "mi";
  public string TemperatureUnit => Temperature == "fahrenheit" ? "°F" : "°C";

  public string DistanceValue(double? miles) =>
    Format(
      Valid(miles) is { } value
        ? value * (Distance == "kilometers" ? 1.609344 : 1)
        : null
    );

  public string Kilometers(double? miles) => Format(Valid(miles) * 1.609344);

  public string FormatDistance(double? miles) =>
    Valid(miles) is null ? "—"
    : BothDistances ? $"{DistanceValue(miles)} mi · {Kilometers(miles)} km"
    : $"{DistanceValue(miles)} {DistanceUnit}";

  public string FormatDistanceDelta(double miles) =>
    double.IsFinite(miles)
      ? (miles < 0 ? "−" : "+") + FormatDistance(Math.Abs(miles))
      : "—";

  public string TemperatureValue(decimal? celsius) =>
    celsius is { } value
      ? (Temperature == "fahrenheit" ? value * 9m / 5m + 32m : value).ToString(
        "0.#",
        CultureInfo.InvariantCulture
      )
      : "—";

  private static double? Valid(double? value) =>
    value is >= 0 && double.IsFinite(value.Value) ? value : null;

  private static string Format(double? value) =>
    value?.ToString("N0", CultureInfo.InvariantCulture) ?? "—";
}
