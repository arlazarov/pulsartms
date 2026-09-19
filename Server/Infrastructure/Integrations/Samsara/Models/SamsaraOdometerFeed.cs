namespace Infrastructure.Integrations.Samsara.Models;

public sealed class SamsaraOdometerFeed
{
  public string Id { get; set; } = "";
  public List<SamsaraOdometerReading> ObdOdometerMeters { get; set; } = [];
}

public sealed class SamsaraOdometerReading
{
  public DateTimeOffset Time { get; set; }
  public decimal? Value { get; set; }
}
