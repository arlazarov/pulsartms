using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace Application.Features.Fuel.Commands.SyncIftaTaxRates;

public static class IftaTaxMatrixParser
{
  public static List<IftaTaxRateData> Parse(string content)
  {
    using var reader = new StringReader(content);
    using var csv = new CsvReader(
      reader,
      new CsvConfiguration(CultureInfo.InvariantCulture)
      {
        HeaderValidated = null,
        MissingFieldFound = null,
        TrimOptions = TrimOptions.Trim,
      }
    );

    var rows = new List<IftaTaxRateData>();

    csv.Read();
    csv.ReadHeader();

    while (csv.Read())
    {
      var jurisdiction = csv.GetField("Jurisdiction") ?? string.Empty;
      var fuelType = csv.GetField("Fuel Type") ?? string.Empty;
      var rateValue = csv.GetField("Tax Rate") ?? string.Empty;
      var currency = csv.GetField("Currency") ?? string.Empty;
      var unit = csv.GetField("Unit") ?? string.Empty;

      if (
        string.IsNullOrWhiteSpace(jurisdiction)
        || string.IsNullOrWhiteSpace(fuelType)
        || !decimal.TryParse(
          rateValue,
          NumberStyles.Any,
          CultureInfo.InvariantCulture,
          out var rate
        )
      )
      {
        continue;
      }

      rows.Add(
        new IftaTaxRateData
        {
          Jurisdiction = jurisdiction,
          FuelType = fuelType,
          Rate = rate,
          Currency = currency,
          Unit = unit,
        }
      );
    }

    return rows;
  }
}

public class IftaTaxRateData
{
  public string Jurisdiction { get; set; } = string.Empty;
  public string FuelType { get; set; } = string.Empty;
  public decimal Rate { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string Unit { get; set; } = string.Empty;
}
