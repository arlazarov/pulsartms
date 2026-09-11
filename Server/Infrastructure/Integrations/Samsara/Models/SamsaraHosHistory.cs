namespace Infrastructure.Integrations.Samsara.Models;
public sealed class SamsaraHosHistory
{
  public SamsaraDriverReference Driver { get; set; } = new();
  public List<SamsaraHosPeriod> HosLogs { get; set; } = [];
}
public sealed class SamsaraHosPeriod
{
  public DateTimeOffset LogStartTime { get; set; }
  public DateTimeOffset? LogEndTime { get; set; }
  public string HosStatusType { get; set; } = "";
}
public sealed class SamsaraEldSettings { public List<SamsaraHosRuleset> Rulesets { get; set; } = []; }
public sealed class SamsaraHosRuleset
{
  public string Cycle { get; set; } = "";
  public string Shift { get; set; } = "";
  public string Jurisdiction { get; set; } = "";
}
