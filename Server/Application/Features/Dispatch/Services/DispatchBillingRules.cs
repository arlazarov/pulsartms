using System.Net.Mail;
using Application.Features.Dispatch.Models;

namespace Application.Features.Dispatch.Services;

public static class DispatchBillingRules
{
  public static string? TermsError(BrokerPaymentTerms? terms)
  {
    if (
      terms is null
      || !Email(terms.BillingEmail)
      || !Email(terms.QuickPayEmail)
      || terms.PaymentDays is < 0 or > 365
      || terms.QuickPayDays is < 0 or > 365
      || terms.QuickPayPercent is < 0 or > 100
      || terms.QuickPayPercent is { } percent
        && decimal.Round(percent, 2) != percent
      || terms.QuickPayFlatFee is { } fee && !Amount(fee, true)
      || terms.QuickPayFeeCurrency is not ("" or "USD" or "CAD")
      || terms.QuickPayFlatFee.HasValue && terms.QuickPayFeeCurrency.Length == 0
      || terms.PaymentCadence
        is not (
          "unspecified"
          or "per_invoice"
          or "weekly"
          or "biweekly"
          or "monthly"
          or "other"
        )
      || terms.PaymentClock
        is not (
          "unspecified"
          or "invoice"
          or "documents_received"
          or "delivery"
        )
      || terms.QuickPayBasis is not ("unspecified" or "linehaul" or "invoice")
      || terms.Notes is null
      || terms.Notes.Length > 2000
    )
      return "Check billing emails and payment terms.";
    return null;
  }

  public static string? AdjustmentsError(DispatchWorkspaceMetadata metadata)
  {
    var rows = metadata.Adjustments;
    if (rows is null || rows.Count > 100)
      return "A load supports up to 100 adjustments.";
    if (rows.Count > 0 && metadata.Currency is not ("USD" or "CAD"))
      return "Choose the load currency before adding adjustments.";
    if (
      rows.Any(row =>
        row is null
        || row.Id == Guid.Empty
        || row.Target is not ("broker" or "driver")
        || row.Direction is not ("addition" or "deduction")
        || row.Currency is not ("USD" or "CAD")
        || !Amount(row.Amount, false)
        || string.IsNullOrWhiteSpace(row.Reason)
        || row.Reason.Length > 500
        || row.DriverName is null
        || row.DriverName.Length > 200
        || row.Target == "driver" && row.DriverId is null
        || row.Target == "broker" && row.DriverId is not null
      )
      || rows.Select(row => row.Id).Distinct().Count() != rows.Count
    )
      return "Each adjustment needs an amount, reason and recipient.";
    if (Totals(metadata).InvoiceAmount is < 0)
      return "Broker deductions cannot exceed the rate and additions.";
    return null;
  }

  public static LoadBillingTotals Totals(DispatchWorkspaceMetadata metadata)
  {
    var broker = metadata.Adjustments.Where(row => row.Target == "broker");
    if (broker.Any(row => row.Currency != metadata.Currency))
      return new(null, null, null) { CurrencyMismatch = true };
    var additions = broker
      .Where(row => row.Direction == "addition")
      .Sum(row => row.Amount);
    var deductions = broker
      .Where(row => row.Direction == "deduction")
      .Sum(row => row.Amount);
    return new(additions, deductions, metadata.Price + additions - deductions);
  }

  public static bool Email(string? value) =>
    value is not null
    && value.Length <= 254
    && (
      value.Length == 0
      || MailAddress.TryCreate(value, out var address)
        && address.Address == value
    );

  private static bool Amount(decimal amount, bool zero) =>
    amount >= (zero ? 0 : 0.01m)
    && amount <= 999999999m
    && decimal.Round(amount, 2) == amount;
}
