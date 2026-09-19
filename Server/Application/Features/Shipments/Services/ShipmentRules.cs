using Application.Features.Shipments.Models;

namespace Application.Features.Shipments.Services;

public static class ShipmentRules
{
  public static readonly string[] PackageTypes =
  [
    "box",
    "carton",
    "crate",
    "bag",
    "drum",
    "piece",
    "pallet",
    "bulk",
  ];

  public static string? Validate(Shipment? draft)
  {
    if (draft is null || draft.Id == Guid.Empty || draft.LoadId == Guid.Empty)
      return "Provide the shipment and load identities.";
    if (
      !TextFits(draft, 200)
      || draft.Commodities is null
      || draft.Commodities.Count > 100
    )
      return "Shipment fields exceed the supported limits.";
    foreach (var party in new[] { draft.Shipper, draft.Consignee })
      if (party is null || !TextFits(party, 300))
        return "Provide party fields within the supported limits.";
    if (
      draft.Commodities.Any(x => x is null || x.Id == Guid.Empty)
      || draft.Commodities.Select(x => x.Id).Distinct().Count()
        != draft.Commodities.Count
    )
      return "Each commodity needs its own identity.";
    foreach (var row in draft.Commodities)
    {
      if (
        !TextFits(row, 500)
        || row.Quantity is <= 0
        || row.Weight is <= 0 or >= 1000000000000000m
        || row.Weight.HasValue
          && decimal.Round(row.Weight.Value, 3) != row.Weight
      )
        return "Use positive quantities and weights with up to three decimals.";
      if (
        row.PackageType.Length > 0 && !PackageTypes.Contains(row.PackageType)
        || row.WeightUnit is not ("" or "kg" or "lb")
      )
        return "Choose a supported package type and weight unit.";
    }
    return null;
  }

  public static List<ShipmentIssue> Missing(Shipment draft)
  {
    List<ShipmentIssue> issues = [];
    void Need(bool missing, string field, string message)
    {
      if (missing)
        issues.Add(new(field, message));
    }
    foreach (
      var (name, party) in new[]
      {
        ("Shipper", draft.Shipper),
        ("Consignee", draft.Consignee),
      }
    )
    {
      foreach (
        var field in new[]
        {
          "Name",
          "AddressLine1",
          "City",
          "Region",
          "Country",
          "PostalCode",
        }
      )
        Need(
          string.IsNullOrWhiteSpace(
            (string?)typeof(ShipmentParty).GetProperty(field)!.GetValue(party)
          ),
          $"{name}.{field}",
          $"Complete {name.ToLowerInvariant()} {field}."
        );
    }
    Need(draft.Commodities.Count == 0, "Commodities", "Add a commodity.");
    foreach (var row in draft.Commodities)
    {
      var prefix = $"Commodities.{row.Id}";
      Need(
        string.IsNullOrWhiteSpace(row.Description),
        prefix + ".Description",
        "Describe the goods."
      );
      Need(
        !row.Quantity.HasValue,
        prefix + ".Quantity",
        "Enter package quantity."
      );
      Need(
        row.PackageType.Length == 0,
        prefix + ".PackageType",
        "Confirm the external packaging type."
      );
      Need(
        !row.Weight.HasValue || row.WeightUnit.Length == 0,
        prefix + ".Weight",
        "Enter weight and its unit."
      );
    }
    return issues;
  }

  private static bool TextFits(object value, int limit) =>
    value
      .GetType()
      .GetProperties()
      .Where(p => p.PropertyType == typeof(string))
      .All(p => p.GetValue(value) is string text && text.Length <= limit);
}
