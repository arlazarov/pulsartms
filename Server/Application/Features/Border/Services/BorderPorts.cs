using System.Text.Json;
using Application.Features.Border.Models;

namespace Application.Features.Border.Services;

public static class BorderPorts
{
  public static IReadOnlyList<BorderPort> All { get; } = Load();

  public static bool Contains(string country, string code) =>
    All.Any(x => x.Country == country && x.Code == code);

  private static IReadOnlyList<BorderPort> Load()
  {
    using var stream =
      typeof(BorderPorts).Assembly.GetManifestResourceStream(
        "Application.Features.Border.Data.Ports.json"
      )
      ?? throw new InvalidOperationException("Border port catalog is missing.");
    return Array.AsReadOnly(JsonSerializer.Deserialize<BorderPort[]>(stream)!);
  }
}
