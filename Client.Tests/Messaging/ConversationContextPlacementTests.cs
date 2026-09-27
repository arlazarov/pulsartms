using Bunit;
using Client.Models.DTO.Messaging;
using Client.Pages.Messages;
using Client.Tests.Support;

namespace Client.Tests.Messaging;

// Stage 3b of docs/architecture/current-work.md: Messenger names the
// driver's loads as the board does - the current load is the one the
// server placed as current, not the first listed; work planning passed
// without a delivery shows its conflict; a stale place needs a refresh.
[Trait("Category", "Messaging")]
[Trait("Kind", "Component")]
public sealed class ConversationContextPlacementTests
{
  [Fact]
  public void TheCurrentLoadIsTheServersNotTheFirstListed()
  {
    using var context = new ClientComponentContext(
      (_, _) => throw new InvalidOperationException("No request expected.")
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var passed = Load(1395, "earlier", "route_passed_not_delivered");
    var current = Load(1412, "current", null);
    var stale = Load(1420, "stale", null);

    var panel = context.Render<ConversationContextPanel>(p =>
      p.Add(x => x.ConversationId, Guid.NewGuid())
        .Add(
          x => x.Context,
          new ConversationContext(
            Guid.NewGuid(),
            "Driver",
            "one-truck",
            [],
            [passed, current, stale]
          )
        )
    );

    var sections = panel.FindAll(".messages__ctx");
    var head = sections.Single(x =>
      x.QuerySelector("h3")?.TextContent == "Current load"
    );
    Assert.Contains("1412", head.TextContent);
    Assert.DoesNotContain("1395", head.TextContent);
    var others = sections.Single(x =>
      x.QuerySelector("h3")?.TextContent == "Other loads"
    );
    var lines = others.QuerySelectorAll(".messages__next");
    Assert.Equal(2, lines.Length);
    var conflict = lines[0].QuerySelector(".messages__pill.is-conflict");
    Assert.Equal("Route passed · not delivered", conflict?.TextContent);
    Assert.Equal(
      "Needs refresh",
      lines[1].QuerySelector(".messages__pill")?.TextContent
    );
  }

  // Loads the server left out of the list are counted, conflicts named.
  [Fact]
  public void LoadsBeyondTheListAreCountedWithTheirConflicts()
  {
    using var context = new ClientComponentContext(
      (_, _) => throw new InvalidOperationException("No request expected.")
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var panel = context.Render<ConversationContextPanel>(p =>
      p.Add(x => x.ConversationId, Guid.NewGuid())
        .Add(
          x => x.Context,
          new ConversationContext(
            Guid.NewGuid(),
            "Driver",
            "one-truck",
            [],
            [Load(1407, "current", null)]
          )
          {
            OmittedLoads = 2,
            OmittedConflicts = 2,
          }
        )
    );
    Assert.Contains(
      "2 more loads, 2 with a route passed but not delivered",
      panel.Markup
    );
  }

  // Filing a driver's file offers the server's current load by default;
  // with none - a stale, unknown or conflicting place - the dispatcher
  // chooses, and the first load listed is not taken for them.
  [Theory]
  [InlineData("current", null, true)]
  [InlineData("stale", null, false)]
  [InlineData("earlier", "route_passed_not_delivered", false)]
  [InlineData("unknown", null, false)]
  public void FilingOffersOnlyTheServersCurrentLoad(
    string phase,
    string? conflict,
    bool offered
  )
  {
    using var context = new ClientComponentContext(
      (_, _) => throw new InvalidOperationException("No request expected.")
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var load = Load(1395, phase, conflict);
    var filing = context.Render<FileToLoad>(p =>
      p.Add(
          x => x.Attachment,
          new AttachmentView(
            Guid.NewGuid(),
            "bol.pdf",
            "pdf",
            "ready",
            true,
            null,
            []
          )
        )
        .Add(x => x.Loads, [load])
    );
    Assert.Equal(offered, filing.Markup.Contains("the driver's current load"));
    filing.Find("button.btn--primary").Click();
    var chosen = filing.Find("select").GetAttribute("value") ?? "";
    Assert.Equal(offered ? load.Id.ToString() : "", chosen);
  }

  // The loads change while the form is open: a default follows the
  // server's current load and goes when it is stale or gone, while a load
  // the dispatcher picked stays picked.
  [Fact]
  public void ADefaultFollowsTheServerAndAChoiceStays()
  {
    using var context = new ClientComponentContext(
      (_, _) => throw new InvalidOperationException("No request expected.")
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var a = Load(1395, "current", null);
    var attachment = new AttachmentView(
      Guid.NewGuid(),
      "bol.pdf",
      "pdf",
      "ready",
      true,
      null,
      []
    );
    var filing = context.Render<FileToLoad>(p =>
      p.Add(x => x.Attachment, attachment).Add(x => x.Loads, [a])
    );
    filing.Find("button.btn--primary").Click();
    string Chosen() => filing.Find("select").GetAttribute("value") ?? "";
    Assert.Equal(a.Id.ToString(), Chosen());

    var staleA = a with { Phase = "stale" };
    filing.Render(p => p.Add(x => x.Loads, [staleA]));
    Assert.Equal("", Chosen());

    var b = Load(1412, "current", null);
    filing.Render(p => p.Add(x => x.Loads, [staleA, b]));
    Assert.Equal(b.Id.ToString(), Chosen());

    filing.Find("select").Change(staleA.Id.ToString());
    var c = Load(1420, "current", null);
    filing.Render(p =>
      p.Add(x => x.Loads, [staleA, b with { Phase = "next" }, c])
    );
    Assert.Equal(staleA.Id.ToString(), Chosen());
  }

  private static ContextLoad Load(int number, string phase, string? conflict) =>
    new(Guid.NewGuid(), number, "Customer", "active", ["Toronto"])
    {
      Phase = phase,
      Conflict = conflict,
    };
}
