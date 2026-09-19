using Bunit;
using Client.Models.DTO.Addresses;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;
using Client.Shared.Search.AddressAutocomplete;
using Client.Shared.Shipments.ShipmentPartyEditor;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchAddressSearchTests
{
  private static readonly PostalAddress Saved = new(
    "123 Main St",
    "Newark",
    "NJ",
    "US",
    "07101"
  );

  [Fact]
  public async Task DropdownShowsPostalCodesAndOnlyOneProviderAttribution()
  {
    using var context = new ClientComponentContext(
      (_, _) =>
        Task.FromResult(
          MileageComponentResponses.Ok(
            new AddressSuggestions(
              [
                new("", "Saved address", Saved),
                new("first", "First suggestion", PostalCode: "M1B 1B1"),
                new("second", "Second suggestion", PostalCode: "37201"),
              ]
            )
          )
        )
    );
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var cut = context.Render<AddressAutocomplete>();
    var input = cut.Find("input").InputAsync(new() { Value = "Main" });
    clock.Advance(TimeSpan.FromMilliseconds(300));
    await input;
    var dropdown = cut.Find(".stop-workspace__address-dropdown");
    Assert.Equal(3, dropdown.QuerySelectorAll("[role=option]").Length);
    Assert.Contains("07101", dropdown.TextContent);
    Assert.Contains("M1B 1B1", dropdown.TextContent);
    Assert.Contains("37201", dropdown.TextContent);
    Assert.Single(cut.FindAll(".stop-workspace__address-attribution"));
    Assert.All(
      cut.FindAll("[role=option]"),
      option => Assert.DoesNotContain("Google Maps", option.TextContent)
    );
  }

  [Fact]
  public async Task SuggestionsDebounceAndSelectionVerifiesStopCoordinates()
  {
    var calls = 0;
    using var context = new ClientComponentContext(
      (_, _) =>
      {
        calls++;
        return Task.FromResult(
          MileageComponentResponses.Ok(
            new AddressSuggestions([new("", "123 Main St", Saved)])
          )
        );
      }
    );
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var stop = new DispatchWorkspaceStop
    {
      Id = Guid.NewGuid(),
      Address = "Saved address",
    };
    var verified = 0;
    var cut = context.Render<DispatchStopFields>(p =>
      p.Add(x => x.Stop, stop)
        .Add(x => x.Clock, new DispatchStopClockDraft())
        .Add(
          x => x.AddressLookup,
          (query, _) =>
          {
            verified++;
            Assert.Equal("123 Main St, Newark, NJ, US, 07101", query);
            return Task.FromResult(
              new Client.Models.DTO.RequestResponseDTO<VerifiedDispatchAddress>
              {
                Success = true,
                Response = new(
                  "123 Main St",
                  "Newark",
                  "NJ",
                  "US",
                  "07101",
                  40.7m,
                  -74.1m
                ),
              }
            );
          }
        )
    );
    var input = cut.Find("input[type=search]")
      .InputAsync(new() { Value = "123 Main" });
    Assert.Equal(0, calls);
    clock.Advance(TimeSpan.FromMilliseconds(300));
    await input;
    Assert.Equal(1, calls);
    Assert.Equal(0, verified);
    Assert.Equal("Saved address", stop.Address);
    await cut.Find("[role=option]").ClickAsync(new());
    Assert.Equal("123 Main St", stop.Address);
    Assert.Equal(40.7m, stop.Latitude);
    Assert.Equal(1, verified);
  }

  [Fact]
  public async Task LateSuggestionsCannotReplaceNewerInput()
  {
    var pending = new TaskCompletionSource<HttpResponseMessage>();
    var started = new TaskCompletionSource();
    using var context = new ClientComponentContext(
      (_, _) =>
      {
        started.SetResult();
        return pending.Task;
      }
    );
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var cut = context.Render<AddressAutocomplete>();
    var old = cut.Find("input").InputAsync(new() { Value = "Old street" });
    clock.Advance(TimeSpan.FromMilliseconds(300));
    await started.Task;
    await cut.Find("input").InputAsync(new() { Value = "N" });
    pending.SetResult(
      MileageComponentResponses.Ok(
        new AddressSuggestions([new("old", "Old street")])
      )
    );
    await old;
    Assert.Empty(cut.FindAll("[role=option]"));
    Assert.Equal("N", cut.Find("input").GetAttribute("value"));
  }

  [Fact]
  public async Task PartySelectionPreservesNameAndContactAndDoesNotAutosave()
  {
    using var context = new ClientComponentContext(
      (_, _) =>
        Task.FromResult(
          MileageComponentResponses.Ok(
            new AddressSuggestions([new("", "123 Main St", Saved)])
          )
        )
    );
    var clock = new FakeTimeProvider();
    context.Services.AddSingleton<TimeProvider>(clock);
    var party = new Client.Models.DTO.Shipments.ShipmentParty
    {
      Name = "Legal party",
      ContactName = "Contact",
      AddressLine2 = "Old unit",
    };
    var changes = 0;
    var cut = context.Render<ShipmentPartyEditor>(p =>
      p.Add(x => x.Value, party).Add(x => x.Changed, () => changes++)
    );
    var input = cut.Find("input[type=search]")
      .InputAsync(new() { Value = "123 Main" });
    clock.Advance(TimeSpan.FromMilliseconds(300));
    await input;
    Assert.Equal(0, changes);
    await cut.Find("input[type=search]")
      .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
    await cut.Find("input[type=search]")
      .KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    Assert.Equal("123 Main St", party.AddressLine1);
    Assert.Equal("", party.AddressLine2);
    Assert.Equal("Legal party", party.Name);
    Assert.Equal("Contact", party.ContactName);
    Assert.Equal("07101", party.PostalCode);
    Assert.Equal(1, changes);
  }
}
