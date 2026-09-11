namespace Infrastructure.Integrations.Samsara.Models;

public class SamsaraDriver
{
  public string Timezone { get; set; } = "";
  public int EldDayStartHour { get; set; }
  public SamsaraEldSettings? EldSettings { get; set; }
  public string Id { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string DriverActivationStatus { get; set; } = string.Empty;
  public List<SamsaraAttribute> Attributes { get; set; } = [];
}

public class SamsaraAttribute
{
  public string Name { get; set; } = string.Empty;
  public List<string> StringValues { get; set; } = [];
}
