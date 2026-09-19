using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchBillingRulesTests
{
  [Fact]
  public void DriverRecordsAndQuickPayDoNotChangeInvoice()
  {
    var metadata = new DispatchWorkspaceMetadata
    {
      Price = 1000m,
      Currency = "USD",
      PaymentTerms = new() { QuickPayPercent = 3m },
      Adjustments =
      [
        Row("broker", "addition", 150m),
        Row("broker", "deduction", 25m),
        Row("driver", "addition", 80m),
        Row("driver", "deduction", 10m),
      ],
    };
    Assert.Null(DispatchBillingRules.AdjustmentsError(metadata));
    Assert.Equal(new(150m, 25m, 1125m), DispatchBillingRules.Totals(metadata));
    Assert.Equal(1000m, metadata.Price);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(0)]
  [InlineData(1.234)]
  public void InvalidAmountsAreRejected(decimal amount)
  {
    var metadata = new DispatchWorkspaceMetadata
    {
      Price = 1000,
      Currency = "USD",
      Adjustments = [Row("broker", "addition", amount)],
    };
    Assert.NotNull(DispatchBillingRules.AdjustmentsError(metadata));
  }

  [Fact]
  public void CurrencyRecipientDuplicatesAndNegativeInvoiceAreRejected()
  {
    var row = Row("driver", "addition", 10m);
    var metadata = new DispatchWorkspaceMetadata
    {
      Price = 5m,
      Currency = "USD",
      Adjustments = [row],
    };
    row.DriverId = null;
    Assert.NotNull(DispatchBillingRules.AdjustmentsError(metadata));
    row.Target = "broker";
    row.Direction = "deduction";
    Assert.NotNull(DispatchBillingRules.AdjustmentsError(metadata));
    metadata.Price = 100;
    metadata.Adjustments.Add(row);
    Assert.NotNull(DispatchBillingRules.AdjustmentsError(metadata));
    metadata.Adjustments.RemoveAt(1);
    metadata.Currency = "";
    Assert.NotNull(DispatchBillingRules.AdjustmentsError(metadata));
  }

  [Fact]
  public void SourceCurrencyChangeCannotReinterpretRecordedAmounts()
  {
    var metadata = new DispatchWorkspaceMetadata
    {
      Price = 1000,
      Currency = "CAD",
      Adjustments = [Row("broker", "addition", 25)],
    };
    var totals = DispatchBillingRules.Totals(metadata);
    Assert.True(totals.CurrencyMismatch);
    Assert.Null(totals.InvoiceAmount);
    Assert.Null(totals.BrokerAdditions);
    Assert.Equal("USD", metadata.Adjustments[0].Currency);
    metadata.Adjustments[0].Target = "driver";
    Assert.False(DispatchBillingRules.Totals(metadata).CurrencyMismatch);
    Assert.Equal(1000, DispatchBillingRules.Totals(metadata).InvoiceAmount);
  }

  [Fact]
  public void UnknownRateStaysUnknownAndTermsRequireRealEmail()
  {
    var metadata = new DispatchWorkspaceMetadata();
    Assert.Null(DispatchBillingRules.Totals(metadata).InvoiceAmount);
    Assert.NotNull(
      DispatchBillingRules.TermsError(new() { BillingEmail = "not an email" })
    );
    Assert.NotNull(
      DispatchBillingRules.TermsError(new() { QuickPayPercent = 101 })
    );
    Assert.NotNull(
      DispatchBillingRules.TermsError(new() { QuickPayFlatFee = 10 })
    );
    Assert.Null(
      DispatchBillingRules.TermsError(
        new() { QuickPayFlatFee = 10, QuickPayFeeCurrency = "USD" }
      )
    );
  }

  private static LoadAdjustment Row(
    string target,
    string direction,
    decimal amount
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      Target = target,
      Direction = direction,
      Amount = amount,
      Currency = "USD",
      Reason = "Recorded expense",
      DriverId = target == "driver" ? Guid.NewGuid() : null,
    };
}
