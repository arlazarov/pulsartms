using Bunit;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;
using Client.Tests.Support;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchAssignmentReviewTests
{
  [Fact]
  public void ImportedNamesRemainSeparateFromAcceptedWorkAndDoNotWrite()
  {
    using var context = new ClientComponentContext(
      (_, _) =>
        throw new InvalidOperationException("Comparison must not call an API.")
    );
    var source = new DispatchAssignmentProposal
    {
      Header = Resources("Imported header", null),
      Visits =
      [
        new(1, "Pick Up", "First location", Resources("Unknown truck", null)),
        new(
          2,
          "Delivery",
          "Last location",
          Resources("Matched truck", Guid.NewGuid())
        ),
      ],
    };
    var component = context.Render<DispatchAssignmentReview>(p =>
      p.Add(x => x.Source, source)
        .Add(
          x => x.Accepted,
          [
            new(
              Guid.NewGuid(),
              4,
              "active",
              "Accepted pickup",
              "Accepted delivery",
              Resources("Accepted truck", Guid.NewGuid())
            ),
          ]
        )
    );
    var accepted = component.Find("section[aria-label='Accepted assignments']");
    var imported = component.Find(
      "section[aria-label='Imported assignment proposal']"
    );
    Assert.Contains("Accepted truck", accepted.TextContent);
    Assert.DoesNotContain("Unknown truck", accepted.TextContent);
    Assert.Contains("Imported header", imported.TextContent);
    Assert.Contains("Unknown truck", imported.TextContent);
    Assert.Contains("Matched truck", imported.TextContent);
    Assert.Equal(2, component.FindAll(".assignment-review__unresolved").Count);
    Assert.Empty(component.FindAll("button, input, select"));

    component.Render(p =>
      p.Add(x => x.Source, new DispatchAssignmentProposal())
        .Add(x => x.Accepted, Array.Empty<DispatchAcceptedAssignment>())
    );
    Assert.DoesNotContain("Accepted truck", component.Markup);
    Assert.DoesNotContain("Unknown truck", component.Markup);
    Assert.Contains(
      "No resource assignment has been accepted.",
      component.Markup
    );
  }

  private static DispatchResourceProposal Resources(string truck, Guid? id) =>
    new(truck, "", "", "", id, null, null, null);
}
