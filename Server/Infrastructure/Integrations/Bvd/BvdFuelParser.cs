using System.Globalization;
using System.Text;
using Application.Features.Fuel.Models;
using CsvHelper;
using CsvHelper.Configuration;

namespace Infrastructure.Integrations.Bvd;

public static class BvdFuelCsvParser
{
  public static (
    DateOnly EffectiveDate,
    DateOnly EffectiveTo,
    List<FuelDiscountImportRow> Rows
  ) Parse(byte[] content)
  {
    using var stream = new MemoryStream(content);
    using var reader = new StreamReader(stream, Encoding.UTF8);
    using var csv = new CsvReader(
      reader,
      new CsvConfiguration(CultureInfo.InvariantCulture)
      {
        HeaderValidated = null,
        MissingFieldFound = null,
        TrimOptions = TrimOptions.Trim,
        PrepareHeaderForMatch = args => args.Header.Trim().ToUpperInvariant(),
      }
    );

    if (!csv.Read())
    {
      return (default, default, []);
    }

    var dates = (
      csv.GetField(7)
      ?? throw new InvalidOperationException("Effective Date is missing.")
    ).Split(" to ", StringSplitOptions.TrimEntries);
    if (dates.Length is < 1 or > 2)
      throw new FormatException("Invalid fuel price date range.");
    var effectiveDate = DateOnly.Parse(dates[0], CultureInfo.InvariantCulture);
    var effectiveTo =
      dates.Length == 2
        ? DateOnly.Parse(dates[1], CultureInfo.InvariantCulture)
        : effectiveDate;
    if (effectiveTo < effectiveDate)
      throw new FormatException("Fuel price end date precedes start date.");

    if (!csv.Read())
    {
      return (effectiveDate, effectiveTo, []);
    }

    csv.ReadHeader();

    var rows = new List<FuelDiscountImportRow>();

    while (csv.Read())
    {
      var regions = new[] { "STATE", "PROV", "PROVINCE" }
        .Select(header => csv.GetField(header)?.Trim().ToUpperInvariant())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct()
        .ToArray();
      if (regions.Length != 1)
        throw new FormatException(
          "Fuel station state/province is missing or conflicting."
        );
      rows.Add(
        new FuelDiscountImportRow
        {
          StationId = csv.GetField("SITE") ?? string.Empty,
          Name = csv.GetField("NAME") ?? string.Empty,
          City = csv.GetField("CITY") ?? string.Empty,
          State = regions[0]!,
          RetailPrice = csv.GetField<decimal>("RETAIL PRICE"),
          DiscountPrice = csv.GetField<decimal>("YOUR PRICE"),
        }
      );
    }

    return (effectiveDate, effectiveTo, rows);
  }
}
