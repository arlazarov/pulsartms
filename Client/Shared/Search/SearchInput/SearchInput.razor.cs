using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Shared.Search.SearchInput;

public partial class SearchInput
{
    [Parameter, EditorRequired] public string Id { get; set; } = "";
    [Parameter] public string Placeholder { get; set; } = "Search";
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public IReadOnlyList<SearchSuggestion> Options { get; set; } = [];
    [Parameter] public bool Loading { get; set; }
    [Parameter] public EventCallback<string> Changed { get; set; }
    [Parameter] public EventCallback<string> Selected { get; set; }
    [Parameter] public EventCallback Enter { get; set; }
    private bool _open;
    private int _active = -1;
    private bool ShowOptions => _open && !string.IsNullOrWhiteSpace(Value);

    private Task InputAsync(ChangeEventArgs args)
    {
        _open = true;
        _active = -1;
        return Changed.InvokeAsync(args.Value?.ToString() ?? "");
    }
    private Task ChooseAsync(string value)
    {
        _open = false;
        _active = -1;
        return Selected.InvokeAsync(value);
    }
    private async Task KeyAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Escape") { _open = false; _active = -1; return; }
        if (string.IsNullOrWhiteSpace(Value)) return;
        if (args.Key is "ArrowDown" or "ArrowUp")
        {
            _open = true;
            if (Options.Count > 0) _active = args.Key == "ArrowDown" ? (_active + 1) % Options.Count : (_active <= 0 ? Options.Count - 1 : _active - 1);
        }
        else if (args.Key == "Enter")
        {
            if (ShowOptions && _active >= 0 && _active < Options.Count) await ChooseAsync(Options[_active].Value);
            else { _open = false; await Enter.InvokeAsync(); }
        }
    }
}
