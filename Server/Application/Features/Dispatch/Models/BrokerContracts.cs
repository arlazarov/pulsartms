namespace Application.Features.Dispatch.Models;

public sealed class BrokerPaymentTerms
{
  public string BillingEmail { get; set; } = "";
  public string QuickPayEmail { get; set; } = "";
  public int? PaymentDays { get; set; }
  public string PaymentCadence { get; set; } = "unspecified";
  public string PaymentClock { get; set; } = "unspecified";
  public int? QuickPayDays { get; set; }
  public decimal? QuickPayPercent { get; set; }
  public decimal? QuickPayFlatFee { get; set; }
  public string QuickPayFeeCurrency { get; set; } = "";
  public string QuickPayBasis { get; set; } = "unspecified";
  public string Notes { get; set; } = "";
}

public sealed class BrokerProfile
{
  public Guid Id { get; set; }
  public long Revision { get; set; }
  public string Name { get; set; } = "";
  public string Contact { get; set; } = "";
  public string Phone { get; set; } = "";
  public string Email { get; set; } = "";
  public string BillTo { get; set; } = "";
  public BrokerPaymentTerms Terms { get; set; } = new();
}

public sealed class LoadAdjustment
{
  public string Currency { get; set; } = "";
  public Guid? DriverId { get; set; }
  public Guid Id { get; set; }
  public string Target { get; set; } = "broker";
  public string Direction { get; set; } = "addition";
  public decimal Amount { get; set; }
  public string Reason { get; set; } = "";
  public string DriverName { get; set; } = "";
}

public sealed record LoadBillingTotals(
  decimal? BrokerAdditions,
  decimal? BrokerDeductions,
  decimal? InvoiceAmount
)
{
  public bool CurrencyMismatch { get; init; }
}
