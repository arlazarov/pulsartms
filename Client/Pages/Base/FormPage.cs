using Microsoft.AspNetCore.Components;

namespace Client.Pages.Base;

public abstract class FormPage : ComponentBase
{
  protected Dictionary<string, List<string>> Errors { get; set; } = [];

  protected void SetErrors(IEnumerable<string> errors)
  {
    Errors.Clear();

    foreach (var error in errors)
    {
      var parts = error.Split(':', 2);

      if (parts.Length != 2)
      {
        AddError("General", error);
        continue;
      }

      AddError(parts[0].Trim(), parts[1].Trim());
    }
  }

  protected bool HasError(string field)
  {
    return Errors.ContainsKey(field);
  }

  private void AddError(string field, string message)
  {
    if (!Errors.TryGetValue(field, out var messages))
    {
      messages = [];
      Errors[field] = messages;
    }

    messages.Add(message);
  }
}
