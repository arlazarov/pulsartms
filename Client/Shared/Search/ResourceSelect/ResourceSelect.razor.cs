using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Shared.Search.ResourceSelect;

public partial class ResourceSelect
{
  [Parameter, EditorRequired]
  public string Id { get; set; } = "";

  [Parameter]
  public string Placeholder { get; set; } = "Search by number";

  [Parameter]
  public string? EmptyLabel { get; set; }

  [Parameter]
  public Guid? Value { get; set; }

  [Parameter]
  public EventCallback<Guid?> ValueChanged { get; set; }

  [Parameter]
  public IReadOnlyList<KeyValuePair<Guid, string>> Options { get; set; } = [];

  [Parameter]
  public bool Disabled { get; set; }

  private bool _open;
  private string _query = "";
  private int _active = -1;
  private int _matchCount;
  private Guid? _openedValue;
  private List<KeyValuePair<Guid, string>> Matches { get; set; } = [];
  private string SelectedLabel =>
    Value.HasValue
      ? Options.FirstOrDefault(x => x.Key == Value).Value ?? ""
      : EmptyLabel ?? "";
  private string? ActiveId =>
    _open && _active >= 0 && _active < Matches.Count
      ? $"{Id}-option-{_active}"
      : null;

  protected override void OnParametersSet()
  {
    if (Disabled || _open && _openedValue != Value)
      Close();
    if (_open)
      Filter();
  }

  private void Open()
  {
    if (Disabled || _open)
      return;
    _open = true;
    _openedValue = Value;
    _query = "";
    Filter();
  }

  private void Close()
  {
    _open = false;
    _query = "";
    _active = -1;
  }

  private void Input(ChangeEventArgs args)
  {
    if (Disabled)
      return;
    Open();
    _query = args.Value?.ToString() ?? "";
    Filter();
  }

  private void Filter()
  {
    var query = _query.Trim();
    var matches = Options
      .Where(x => x.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
      .ToList();
    if (
      EmptyLabel is not null
      && EmptyLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
    )
      matches.Insert(0, KeyValuePair.Create(Guid.Empty, EmptyLabel));
    _matchCount = matches.Count;
    Matches = matches
      .OrderByDescending(x => x.Key == Guid.Empty)
      .ThenByDescending(x =>
        x.Value.Equals(query, StringComparison.OrdinalIgnoreCase)
      )
      .ThenByDescending(x =>
        x.Value.StartsWith(query, StringComparison.OrdinalIgnoreCase)
      )
      .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
      .Take(10)
      .ToList();
    _active = -1;
  }

  private async Task ChooseAsync(Guid id)
  {
    if (Disabled)
      return;
    Close();
    await ValueChanged.InvokeAsync(id == Guid.Empty ? null : id);
  }

  private async Task KeyAsync(KeyboardEventArgs args)
  {
    if (Disabled)
      return;
    if (args.Key == "Escape")
      Close();
    else if (args.Key is "ArrowDown" or "ArrowUp")
    {
      Open();
      if (Matches.Count > 0)
        _active =
          args.Key == "ArrowDown"
            ? (_active + 1) % Matches.Count
            : (_active <= 0 ? Matches.Count - 1 : _active - 1);
    }
    else if (args.Key == "Enter" && _open && _active >= 0)
      await ChooseAsync(Matches[_active].Key);
  }
}
