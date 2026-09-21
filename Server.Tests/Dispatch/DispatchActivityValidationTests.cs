using Application.Features.Dispatch.Activity;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchActivityValidationTests
{
  [Theory]
  [InlineData("driver-called", "Reached dispatch", true)]
  [InlineData("called-driver", "Everything is OK", true)]
  [InlineData("note", "Broker confirmed", true)]
  [InlineData("status-change", "Delivered", false)]
  [InlineData("note", " ", false)]
  public void AcceptsOnlyBoundedJournalKinds(
    string kind,
    string text,
    bool valid
  )
  {
    var command = new AddDispatchActivityCommand(
      Guid.NewGuid(),
      new(Guid.NewGuid(), 0, kind, text, null, null, false)
    );
    Assert.Equal(valid, !command.Wrong().Any());
    Assert.NotEmpty(
      (
        command with
        {
          Update = command.Update with { Text = new string('x', 4001) },
        }
      ).Wrong()
    );
  }
}
