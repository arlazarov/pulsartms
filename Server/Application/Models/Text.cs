namespace Application.Models;

public static class Text
{
  // The same question the validation package asked of an email address,
  // and no more: one "@" with something on each side. It never tried to
  // decide whether an address can receive mail - only sending to it can
  // answer that - and neither does this.
  public static bool LooksLikeEmail(string? value)
  {
    var at = value?.IndexOf('@') ?? -1;
    return at > 0 && at < value!.Length - 1 && value.IndexOf('@', at + 1) < 0;
  }
}
