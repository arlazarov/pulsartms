namespace Infrastructure.Integrations.Torque.Models;

public class TorqueDispatchResponse
{
  public List<TorqueDispatchDto> Data { get; set; } = [];
  public int TotalCount { get; set; }
  public int Page { get; set; }
  public int ItemsPerPage { get; set; }
  public TorqueDateRangeDto? DateRange { get; set; }
  public int? LoadNo { get; set; }
  public string? OrderNo { get; set; }
}

public class TorqueDateRangeDto
{
  public string From { get; set; } = string.Empty;
  public string To { get; set; } = string.Empty;
}
