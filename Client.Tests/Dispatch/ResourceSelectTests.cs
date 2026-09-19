using Bunit;
using Client.Shared.Search.ResourceSelect;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class ResourceSelectTests
{
  [Fact]
  public async Task OptionalResourceCanBeClearedWithoutASearchIcon()
  {
    using var context = new BunitContext();
    Guid? selected = Guid.NewGuid();
    var cut = context.Render<ResourceSelect>(p =>
      p.Add(x => x.Id, "driver")
        .Add(x => x.Value, selected)
        .Add(x => x.EmptyLabel, "No driver")
        .Add(x => x.Options, [KeyValuePair.Create(selected.Value, "Alex")])
        .Add(x => x.ValueChanged, id => selected = id)
    );
    Assert.Empty(cut.FindAll("svg"));
    await cut.Find("input").FocusAsync(new());
    await cut.FindAll("[role=option]")
      .Single(x => x.TextContent.Trim() == "No driver")
      .ClickAsync(new());
    Assert.Null(selected);
  }

  [Fact]
  public async Task FindsTruckAmongHundredAndSelectsItsIdentityWithKeyboard()
  {
    using var context = new BunitContext();
    var options = Enumerable
      .Range(1, 100)
      .Select(x => KeyValuePair.Create(Guid.NewGuid(), $"TRUCK-{x:000}"))
      .ToList();
    Guid? selected = null;
    var cut = context.Render<ResourceSelect>(p =>
      p.Add(x => x.Id, "truck")
        .Add(x => x.Options, options)
        .Add(x => x.Value, options[0].Key)
        .Add(x => x.ValueChanged, id => selected = id)
    );
    var input = cut.Find("input");
    await input.FocusAsync(new());
    Assert.Equal(10, cut.FindAll("[role=option]").Count);
    await input.InputAsync("100");
    Assert.Equal("TRUCK-100", cut.Find("[role=option]").TextContent.Trim());
    Assert.Null(selected);
    await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
    await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    Assert.Equal(options[99].Key, selected);
    Assert.Empty(cut.FindAll("[role=listbox]"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task UnselectedSearchRetainsSavedTruck(bool escape)
  {
    using var context = new BunitContext();
    var id = Guid.NewGuid();
    var changes = 0;
    var cut = context.Render<ResourceSelect>(p =>
      p.Add(x => x.Id, "truck")
        .Add(x => x.Value, id)
        .Add(x => x.Options, [KeyValuePair.Create(id, "54777")])
        .Add(x => x.ValueChanged, _ => changes++)
    );
    var input = cut.Find("input");
    await input.FocusAsync(new());
    await input.InputAsync("missing");
    Assert.Contains("No matches found", cut.Markup);
    if (escape)
      await input.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
    else
      await input.BlurAsync(new());
    Assert.Equal("54777", input.GetAttribute("value"));
    Assert.Equal(0, changes);
    Assert.Empty(cut.FindAll("[role=listbox]"));
  }
}
