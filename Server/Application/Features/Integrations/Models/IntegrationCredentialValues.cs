namespace Application.Features.Integrations.Models;

public sealed class IntegrationCredentialValues
{
  private readonly Dictionary<string, string> values;

  public IntegrationCredentialValues(
    IEnumerable<KeyValuePair<string, string>> values
  ) => this.values = new(values, StringComparer.Ordinal);

  public string? Get(string field) => values.GetValueOrDefault(field);

  public IReadOnlyCollection<string> FieldNames => values.Keys;

  public override string ToString() => "[Integration credentials]";
}
