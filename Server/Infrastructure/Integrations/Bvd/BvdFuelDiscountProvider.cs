using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Infrastructure.Integrations.Google.Gmail;

namespace Infrastructure.Integrations.Bvd;

public class BvdFuelDiscountProvider(
  GmailAttachmentService gmailAttachmentService
) : IFuelDiscountProvider
{
  public async Task<IReadOnlyList<FuelDiscountImportData>> GetDiscountsAsync(
    IReadOnlyCollection<string> importedMessageIds,
    DateTime since,
    CancellationToken cancellationToken = default
  )
  {
    var attachments =
      await gmailAttachmentService.GetFuelDiscountAttachmentsAsync(
        importedMessageIds,
        since,
        cancellationToken
      );
    return [.. attachments.Select(Read).OfType<FuelDiscountImportData>()];
  }

  // One attachment. Parsing only reads the bytes it was given, so whatever
  // it throws is this file's content: the attachment is marked unreadable
  // rather than failing every message of the run (audit F20).
  public static FuelDiscountImportData? Read(GmailAttachment attachment)
  {
    if (GetCurrency(attachment.FileName) is not { } currency)
      return null;
    var import = new FuelDiscountImportData
    {
      MessageId = attachment.MessageId,
      AttachmentName = attachment.FileName,
      Currency = currency,
    };
    try
    {
      (import.EffectiveDate, import.EffectiveTo, import.Rows) =
        BvdFuelCsvParser.Parse(attachment.Content);
    }
    catch (Exception)
    {
      import.Unreadable = true;
    }
    return import;
  }

  private static string? GetCurrency(string fileName)
  {
    if (fileName.Contains("CAD", StringComparison.OrdinalIgnoreCase))
    {
      return "CAD";
    }

    if (fileName.Contains("USD", StringComparison.OrdinalIgnoreCase))
    {
      return "USD";
    }

    return null;
  }
}
