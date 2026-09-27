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
    Assert.True(LoadCompletion.IsCompleted("Completed", []));

  [Theory]
  [InlineData("Drop Off")]
  [InlineData("delivery")]
  public void ARecordedFinalDeliveryCompletesTheLoad(string job) =>
    Assert.True(
      LoadCompletion.IsCompleted(
        "in_transit",
        [Stop("Pick Up", done: true), Stop(job, recorded: true, done: true)]
      )
    );

  // Kept from the rule before stage 4b: work without a delivery is not
  // completed by its stops, however they are marked. Changed on purpose: a
  // later stop that is not a delivery (a trailer drop) no longer hides the
  // delivery before it - the cargo is delivered - but the load completes
  // only when that stop is done too (CompletedLoadsParityTests).
  [Fact]
  public void ALoadWithoutADeliveryIsNotCompletedByItsStops() =>
    Assert.False(
      LoadCompletion.IsCompleted(
        "in_transit",
        [
          Stop(
            "Pick Up",
            overridden: true,
            recorded: true,
            confirmed: true,
            done: true
          ),
        ]
      )
    );

  // A dispatcher's word on the final delivery wins over what was recorded,
  // in both directions.
  [Fact]
  public void TheOverrideOnTheFinalDeliveryWinsOverWhatWasRecorded()
  {
    Assert.False(
      LoadCompletion.IsCompleted(
        "in_transit",
        [
          Stop(
            "Delivery",
            overridden: false,
            recorded: true,
            confirmed: true,
            done: false
          ),
        ]
      )
    );
    Assert.True(
      LoadCompletion.IsCompleted(
        "in_transit",
        [Stop("Pick Up"), Stop("Delivery", overridden: true, done: true)]
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
        [Stop("Pick Up"), Stop("Delivery", confirmed: true, done: true)]
      )
    );
    Assert.True(
      LoadCompletion.IsCompleted(
        "in_transit",
        [
          Stop("Pick Up", done: true),
          Stop("Delivery", confirmed: true, done: true),
        ]
      )
    );
  }

  private static int sequence;

  private static CompletionStop Stop(
    string job,
    bool? overridden = null,
    bool recorded = false,
    bool confirmed = false,
    bool done = false
  ) =>
    new(
      Interlocked.Increment(ref sequence),
      job,
      false,
      overridden,
      recorded,
      confirmed,
      done
    );

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
    Assert.True(
      document.RootElement.GetProperty("cargoDelivered").GetBoolean()
    );
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
