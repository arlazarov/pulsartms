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
    CancellationToken cancellationToken = default
  )
  {
    var attachments =
      await gmailAttachmentService.GetFuelDiscountAttachmentsAsync(
        importedMessageIds,
        cancellationToken
      );

    var imports = new List<FuelDiscountImportData>();

    foreach (var attachment in attachments)
    {
      var currency = GetCurrency(attachment.FileName);

      if (currency is null)
      {
        continue;
      }

      var (EffectiveDate, EffectiveTo, Rows) = BvdFuelCsvParser.Parse(
        attachment.Content
      );

      imports.Add(
        new FuelDiscountImportData
        {
          MessageId = attachment.MessageId,
          AttachmentName = attachment.FileName,
          Currency = currency,
          EffectiveDate = EffectiveDate,
          EffectiveTo = EffectiveTo,
          Rows = Rows,
        }
      );
    }

    return imports;
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
