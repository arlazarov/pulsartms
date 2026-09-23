using System.Net.Mail;

namespace Domain.Rules.Fleet;

// Phone numbers are kept in E.164 so a messaging provider receives exactly
// what was checked here. Only a number whose country is certain is
// completed: ten digits that fit the North American plan, with or without
// its leading 1. Anything else has to be written with its "+" and country
// code, because guessing a country would send to somebody else.
public static class ContactAddresses
{
  public const int MaximumPhoneInput = 40;
  public const int MaximumEmail = 254;

  public static string? Phone(string? input)
  {
    if (string.IsNullOrWhiteSpace(input) || input.Length > MaximumPhoneInput)
      return null;
    var text = input.Trim();
    var international = text.StartsWith('+');
    var digits = new List<char>(16);
    foreach (var c in international ? text[1..] : text)
    {
      if (char.IsAsciiDigit(c))
        digits.Add(c);
      else if (c is not (' ' or '-' or '(' or ')' or '.'))
        return null;
    }
    var number = new string([.. digits]);
    if (international)
      return number.Length is >= 8 and <= 15 && number[0] != '0'
        ? "+" + number
        : null;
    if (number.Length == 11 && number[0] == '1')
      number = number[1..];
    return number.Length == 10 && NorthAmerican(number) ? "+1" + number : null;
  }

  // Area code and exchange both start with 2-9 in the North American plan.
  private static bool NorthAmerican(string number) =>
    number[0] >= '2' && number[3] >= '2';

  public static string? Email(string? input)
  {
    if (string.IsNullOrWhiteSpace(input))
      return null;
    var text = input.Trim();
    if (
      text.Length > MaximumEmail
      || text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
      || !MailAddress.TryCreate(text, out var address)
      || address.Address != text
      || address.DisplayName.Length > 0
      || !address.Host.Contains('.')
      || address.Host.StartsWith('.')
      || address.Host.EndsWith('.')
    )
      return null;
    return text;
  }
}
