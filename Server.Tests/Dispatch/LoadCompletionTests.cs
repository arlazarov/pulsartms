using System.Text.Json;
using Application.Features.Dispatch.Models;
using Domain.Rules;

namespace Server.Tests.Dispatch;

// Whether a load is done used to be decided in the browser, by a rule the
// server could not see. It is decided here now, once, and sent.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class LoadCompletionTests
{
  [Fact]
  public void ALoadMarkedCompletedIsCompletedWhateverItsStopsSay() =>
    Assert.True(
      LoadCompletion.IsCompleted("Completed", null, null, false, false, false)
    );

  [Theory]
  [InlineData("Drop Off")]
  [InlineData("delivery")]
  public void ARecordedFinalDeliveryCompletesTheLoad(string job) =>
    Assert.True(
      LoadCompletion.IsCompleted("in_transit", job, null, true, false, false)
    );

  [Fact]
  public void ALoadThatDoesNotEndInADeliveryIsNotCompletedByItsLastStop() =>
    Assert.False(
      LoadCompletion.IsCompleted(
        "in_transit",
        "Pick Up",
        true,
        true,
        true,
        true
      )
    );

  // A dispatcher's word on the final stop wins over what was recorded, in
  // both directions.
  [Fact]
  public void TheOverrideOnTheFinalStopWinsOverWhatWasRecorded()
  {
    Assert.False(
      LoadCompletion.IsCompleted(
        "in_transit",
        "Delivery",
        false,
        true,
        true,
        true
      )
    );
    Assert.True(
      LoadCompletion.IsCompleted(
        "in_transit",
        "Delivery",
        true,
        false,
        false,
        false
      )
    );
  }

  // A delivery confirmed by hand on a load whose pickup never happened is a
  // mistake, not a completed load.
  [Fact]
  public void AHandConfirmedDeliveryCountsOnlyOnceEveryCargoStopIsDone()
  {
    Assert.False(
      LoadCompletion.IsCompleted(
        "in_transit",
        "Delivery",
        null,
        false,
        true,
        false
      )
    );
    Assert.True(
      LoadCompletion.IsCompleted(
        "in_transit",
        "Delivery",
        null,
        false,
        true,
        true
      )
    );
  }

  // The browser reads both verdicts off the wire rather than working them
  // out, so both must be on it.
  [Fact]
  public void BothVerdictsAreSent()
  {
    var load = new DispatchResponse
    {
      Status = "in_transit",
      Stops =
      [
        new()
        {
          Sequence = 1,
          Job = "Pick Up",
          PickedUpAt = DateTime.UtcNow,
        },
        new()
        {
          Sequence = 2,
          Job = "Delivery",
          DeliveredAt = DateTime.UtcNow,
        },
      ],
    };
    var json = JsonSerializer.Serialize(
      load,
      new JsonSerializerOptions(JsonSerializerDefaults.Web)
    );
    using var document = JsonDocument.Parse(json);
    Assert.True(document.RootElement.GetProperty("completed").GetBoolean());
    foreach (
      var stop in document.RootElement.GetProperty("stops").EnumerateArray()
    )
      Assert.True(stop.GetProperty("isCompleted").GetBoolean());
  }

  // A stop waiting for a handoff is not done however it is marked - the one
  // thing the browser's own copy of the rule did not know.
  [Fact]
  public void AStopAwaitingHandoffIsSentAsNotCompleted()
  {
    var stop = new DispatchStopResponse
    {
      AwaitingHandoff = true,
      DeliveredAt = DateTime.UtcNow,
    };
    Assert.False(stop.IsCompleted);
  }
}
