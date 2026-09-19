using System.Globalization;

namespace Client.Shared.Dispatch;

public static class LoadNumberDisplay
{
  public static string Format(int? number, string? prefix) =>
    number is { } value
      ? string.Concat(prefix, value.ToString(CultureInfo.InvariantCulture))
      : "—";
}
