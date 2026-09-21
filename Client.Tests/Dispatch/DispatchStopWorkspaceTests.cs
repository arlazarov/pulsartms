using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchStopWorkspaceTests
{
  [Fact]
  public async Task EverySingleClickActivatesTheStop()
  {
    using var context = Context();
    var stop = Stop("Delivery");
    var events = new List<string>();
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [stop])
        .Add(x => x.CanEdit, true)
        .Add(x => x.StopActivated, id => events.Add("select"))
    );
    var row = component.Find(".stop-workspace__select");
    await row.ClickAsync(new());
    await row.ClickAsync(new());
    await row.ClickAsync(new());
    Assert.Equal(new[] { "select", "select", "select" }, events);
  }

  [Fact]
  public void StopFieldsStayVisibleInTwoGroupsWithActionsInsideSelectedEditor()
  {
    using var context = Context();
    var stop = Stop("Delivery");
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [stop])
        .Add(x => x.CanEdit, true)
        .Add(
          x => x.SelectedStopActions,
          builder =>
            builder.AddMarkupContent(
              0,
              "<button data-test-action>Change status</button>"
            )
        )
    );
    Assert.Empty(component.FindAll(".stop-workspace__fields details"));
    Assert.Empty(component.FindAll(".stop-workspace__header"));
    Assert.Equal(
      "1 stops",
      component
        .Find(".stop-workspace__table-heading .stop-workspace__count")
        .TextContent
    );
    Assert.Equal(
      "+ Add stop",
      component
        .Find(".stop-workspace__table-heading button")
        .GetAttribute("aria-label")
    );
    Assert.Equal(
      2,
      component
        .FindAll(".stop-workspace__fields > .stop-workspace__column")
        .Count
    );
    Assert.NotNull(
      component.Find(".stop-workspace__editor [data-test-action]")
    );
    Assert.NotNull(component.Find($"#stop-{stop.Id}-contact"));
    Assert.NotNull(component.Find($"#stop-{stop.Id}-facility"));
    Assert.Empty(
      component.FindAll(".stop-workspace__editor > .stop-workspace__forecast")
    );
  }

  [Fact]
  public async Task AddAfterLockedSelectionNeverFallsBackToAnotherStop()
  {
    using var context = Context();
    context.Services.AddSingleton(TimeProvider.System);
    var locked = Stop("Recorded pickup");
    locked.CanEdit = locked.CanMove = locked.CanRemove = false;
    var future = Stop("Future delivery");
    var stops = new List<DispatchWorkspaceStop> { locked, future };
    DispatchTransferStart? transfer = null;
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, stops)
        .Add(x => x.CanEdit, true)
        .Add(x => x.SelectedStopId, locked.Id)
        .Add(
          x => x.TransferRequested,
          (DispatchTransferStart value) => transfer = value
        )
    );
    await component
      .FindAll("button")
      .Single(button => button.TextContent.Trim() == "+ Add stop")
      .ClickAsync(new());
    var pickup = component
      .FindAll("button")
      .Single(button => button.TextContent.Trim() == "Pickup");
    Assert.True(pickup.HasAttribute("disabled"));
    await pickup.ClickAsync(new());
    Assert.Equal(2, stops.Count);
    await component
      .FindAll("button")
      .Single(button => button.TextContent.Trim() == "Drop / Hook trailer")
      .ClickAsync(new());
    Assert.Equal(new(locked.Id, "drop_hook"), transfer);
    Assert.Equal(2, stops.Count);
  }

  [Fact]
  public async Task NewDeliveryIsInsertedImmediatelyAfterSelectedStop()
  {
    using var context = Context();
    var first = Stop("First");
    var second = Stop("Second");
    var stops = new List<DispatchWorkspaceStop> { first, second };
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, stops)
        .Add(x => x.CanEdit, true)
        .Add(x => x.SelectedStopId, first.Id)
    );
    await component
      .FindAll("button")
      .Single(button => button.TextContent.Trim() == "+ Add stop")
      .ClickAsync(new());
    Assert.Equal(2, stops.Count);
    await component
      .FindAll("button")
      .Single(button => button.TextContent.Trim() == "Delivery")
      .ClickAsync(new());
    Assert.Equal(3, stops.Count);
    Assert.Equal(first.Id, stops[0].Id);
    Assert.Equal(second.Id, stops[2].Id);
    Assert.True(stops[1].IsNew);
    Assert.Equal("Delivery", stops[1].Job);
    Assert.NotEqual(Guid.Empty, stops[1].Id);
  }

  [Fact]
  public void CompactRowsKeepEachStopsFullResourceNamesAndMissingDriver()
  {
    using var context = Context();
    var first = Stop("Distribution facility with a long complete name");
    first.TruckNumber = "0054777";
    first.TrailerNumber = "TR-44120";
    first.DriverName = "James William Respicio Campos";
    first.CoDriverName = "Alexandra Maria Hernandez";
    var next = Stop("Next facility");
    next.TruckNumber = "11005";
    next.TrailerNumber = "9P1175";
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [first, next]).Add(x => x.CanEdit, true)
    );
    var firstRow = component.Find($"[data-stop-id='{first.Id}']");
    var nextRow = component.Find($"[data-stop-id='{next.Id}']");

    Assert.Equal(
      first.Name,
      firstRow.QuerySelector(".stop-workspace__heading > strong")!.TextContent
    );
    Assert.Equal(
      first.DriverName,
      firstRow.QuerySelector(".stop-workspace__driver strong")!.TextContent
    );
    Assert.Equal(
      first.CoDriverName,
      firstRow
        .QuerySelector(".stop-workspace__co-driver strong")!
        .TextContent.Trim()
    );
    Assert.Equal(
      [first.TruckNumber, first.TrailerNumber],
      firstRow
        .QuerySelectorAll(".stop-workspace__equipment strong")
        .Select(value => value.TextContent.Trim())
    );
    Assert.Equal(
      "—",
      nextRow.QuerySelector(".stop-workspace__driver strong")!.TextContent
    );
    Assert.Null(nextRow.QuerySelector(".stop-workspace__co-driver"));
    Assert.DoesNotContain(first.DriverName, nextRow.TextContent);
    Assert.Null(
      firstRow
        .QuerySelector(".stop-workspace__assignment-preview")!
        .Closest(".stop-workspace__identity")
    );
    Assert.Empty(component.FindAll(".stop-workspace__list input"));
  }

  [Fact]
  public async Task CompactWindowKeepsBothDatesAndClockDraftsInOneGroup()
  {
    using var context = Context();
    var stop = Stop("Window delivery");
    stop.AppointmentMode = "window";
    stop.ScheduledDate = new DateOnly(2026, 9, 14);
    stop.ScheduledDate2 = new DateOnly(2026, 9, 15);
    stop.ScheduledTime = new TimeOnly(14, 0);
    stop.ScheduledTime2 = new TimeOnly(20, 0);
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [stop]).Add(x => x.CanEdit, true)
    );
    var fields = component.FindComponent<DispatchStopFields>().Instance;
    var timeGroup = component.Find(".stop-workspace__appointment-times");

    Assert.Equal(
      ["date", "time", "end-date", "end-time"],
      timeGroup
        .QuerySelectorAll("input")
        .Select(input => input.Id!.Replace($"stop-{stop.Id}-", ""))
    );
    Assert.Equal(
      "2026-09-14",
      timeGroup.QuerySelector("input[type='date']")!.GetAttribute("value")
    );
    await component
      .Find($"#stop-{stop.Id}-end-time")
      .InputAsync(new ChangeEventArgs { Value = "09:15 PM" });

    Assert.Same(fields, component.FindComponent<DispatchStopFields>().Instance);
    Assert.Equal(new DateOnly(2026, 9, 14), stop.ScheduledDate);
    Assert.Equal(new DateOnly(2026, 9, 15), stop.ScheduledDate2);
    Assert.Equal(new TimeOnly(14, 0), stop.ScheduledTime);
    Assert.Equal(new TimeOnly(21, 15), stop.ScheduledTime2);
    Assert.Null(
      component
        .Find($"#stop-{stop.Id}-timezone")
        .Closest(".stop-workspace__appointment-times")
    );
    Assert.Null(timeGroup.Closest("details"));
  }

  [Fact]
  public void CompletedStopUsesOneBadgeWithoutARedundantSummaryForecast()
  {
    using var context = Context();
    context.Services.AddSingleton(TimeProvider.System);
    var completed = Stop("Recorded pickup");
    completed.CanEdit = completed.CanMove = completed.CanRemove = false;
    var upcoming = Stop("Next delivery");
    var load = new DispatchResponse
    {
      Id = Guid.NewGuid(),
      Status = "in_transit",
      Stops =
      [
        new()
        {
          Id = completed.Id,
          ExecutionCompleted = true,
          IsCompleted = true,
        },
        new() { Id = upcoming.Id },
      ],
    };
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [completed, upcoming])
        .Add(x => x.Load, load)
        .Add(x => x.CanEdit, true)
        .Add(x => x.SelectedStopId, completed.Id)
    );

    Assert.Single(
      component.FindAll(".stop-workspace__badge"),
      badge => badge.TextContent.Trim() == "Completed"
    );
    Assert.DoesNotContain("✓ Completed", component.Markup);
    Assert.Empty(
      component.FindAll(
        $"[data-stop-id='{completed.Id}'] > .stop-workspace__forecast"
      )
    );
    Assert.DoesNotContain(
      component.FindComponents<DispatchStopForecast>(),
      forecast => forecast.Instance.Detailed
    );
    component
      .Find($"[data-stop-id='{upcoming.Id}'] .stop-workspace__select")
      .Click();
    Assert.Contains(
      "ETA —",
      component
        .Find($"[data-stop-id='{upcoming.Id}'] .stop-workspace__forecast")
        .TextContent
    );
  }

  [Fact]
  public async Task SelectingRowsKeepsTheListAndSeparateEditorRegionStable()
  {
    using var context = Context();
    var first = Stop("First");
    var second = Stop("Second");
    var stops = new List<DispatchWorkspaceStop> { first, second };
    Guid? reported = first.Id;
    var selectionChanges = 0;
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, stops)
        .Add(x => x.CanEdit, true)
        .Add(x => x.SelectedStopId, first.Id)
        .Add(
          x => x.SelectedStopIdChanged,
          id =>
          {
            reported = id;
            selectionChanges++;
          }
        )
    );
    var listText = component.Find(".stop-workspace__list").TextContent;
    var listElementCount = component.FindAll(".stop-workspace__list *").Count;
    var detailsId = component.Find(".stop-workspace__details").Id;
    Assert.Equal(
      "true",
      component
        .Find($"[data-stop-id='{first.Id}'] .stop-workspace__select")
        .GetAttribute("aria-pressed")
    );
    Assert.Equal(
      "false",
      component
        .Find($"[data-stop-id='{second.Id}'] .stop-workspace__select")
        .GetAttribute("aria-pressed")
    );

    await component
      .Find($"[data-stop-id='{first.Id}'] .stop-workspace__select")
      .ClickAsync(new MouseEventArgs());

    Assert.Equal(first.Id, reported);
    Assert.Equal(0, selectionChanges);
    Assert.Equal(
      $"stop-editor-{first.Id}",
      component.Find(".stop-workspace__editor").Id
    );
    await component
      .Find($"[data-stop-id='{second.Id}'] .stop-workspace__select")
      .ClickAsync(new MouseEventArgs());

    Assert.Equal(second.Id, reported);
    Assert.Equal(1, selectionChanges);
    Assert.Equal(
      $"stop-editor-{second.Id}",
      component.Find(".stop-workspace__editor").Id
    );
    Assert.Equal(listText, component.Find(".stop-workspace__list").TextContent);
    Assert.Equal(
      listElementCount,
      component.FindAll(".stop-workspace__list *").Count
    );
    Assert.Empty(component.FindAll(".stop-workspace__list input"));
    Assert.Null(component.Find(".stop-workspace__editor").Closest("ol"));
    Assert.Equal(
      detailsId,
      component
        .Find(".stop-workspace__working-area > .stop-workspace__details")
        .Id
    );
    Assert.All(
      component.FindAll(".stop-workspace__select"),
      button =>
      {
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal(detailsId, button.GetAttribute("aria-controls"));
        Assert.False(button.HasAttribute("aria-expanded"));
      }
    );
    Assert.Equal(
      "false",
      component
        .Find($"[data-stop-id='{first.Id}'] .stop-workspace__select")
        .GetAttribute("aria-pressed")
    );
    Assert.Equal(
      "true",
      component
        .Find($"[data-stop-id='{second.Id}'] .stop-workspace__select")
        .GetAttribute("aria-pressed")
    );
    Assert.All(
      context.JSInterop.Invocations,
      call => Assert.Contains(call.Identifier, new[] { "import", "revealStop" })
    );
    Assert.Equal(
      new[] { first.Id.ToString(), second.Id.ToString() },
      context
        .JSInterop.Invocations.Where(call => call.Identifier == "revealStop")
        .Select(call => (string)call.Arguments[1]!)
    );
  }

  [Fact]
  public void ExternalSelectionTargetsTheExactStopWithoutChangingTheList()
  {
    using var context = Context();
    var first = Stop("First");
    var second = Stop("Second");
    var stops = new List<DispatchWorkspaceStop> { first, second };
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, stops)
        .Add(x => x.CanEdit, true)
        .Add(x => x.SelectedStopId, first.Id)
    );
    var listText = component.Find(".stop-workspace__list").TextContent;
    var detailsId = component.Find(".stop-workspace__details").Id;

    component.Render(p => p.Add(x => x.SelectedStopId, second.Id));

    Assert.Equal(
      $"stop-editor-{second.Id}",
      component.Find(".stop-workspace__editor").Id
    );
    var title = component.Find($"#{detailsId}-title");
    Assert.Contains("Stop 2", title.TextContent);
    Assert.Contains("Second", title.TextContent);
    Assert.Contains("visually-hidden", title.ClassList);
    Assert.Empty(component.FindAll(".stop-workspace__editor-heading"));
    Assert.Empty(component.FindAll(".stop-workspace__editor-number"));
    Assert.Equal(
      title.Id,
      component.Find(".stop-workspace__details").GetAttribute("aria-labelledby")
    );
    component.Render(p => p.Add(x => x.SelectedStopId, null));
    component.Render();

    Assert.Empty(component.FindAll(".stop-workspace__editor"));
    Assert.Equal(detailsId, component.Find(".stop-workspace__details").Id);
    Assert.Equal(listText, component.Find(".stop-workspace__list").TextContent);
    Assert.All(
      component.FindAll(".stop-workspace__select"),
      button => Assert.Equal("false", button.GetAttribute("aria-pressed"))
    );
  }

  [Fact]
  public void ExistingAndNewStopsKeepLocationAndContactFieldsVisible()
  {
    using var context = Context();
    var stop = Stop("Destination");
    stop.Address = "123 Long Facility Road";
    stop.City = "Allentown";
    stop.Province = "PA";
    stop.Country = "US";
    stop.ZipCode = "18101";
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [stop]).Add(x => x.CanEdit, true)
    );

    Assert.Equal(
      "123 Long Facility Road",
      component.Find($"#stop-{stop.Id}-street").GetAttribute("value")
    );
    Assert.Equal(
      "Allentown",
      component.Find($"#stop-{stop.Id}-city").GetAttribute("value")
    );
    Assert.Empty(component.FindAll(".stop-workspace__fields details"));
    Assert.Null(component.Find($"#stop-{stop.Id}-contact").Closest("details"));
    Assert.Null(
      component.Find($"#stop-{stop.Id}-reference").Closest("details")
    );
    Assert.Null(component.Find($"#stop-{stop.Id}-mode").Closest("details"));
    Assert.Null(
      component.Find($"#stop-{stop.Id}-instructions").Closest("details")
    );

    stop.IsNew = true;
    component.Render();

    Assert.Empty(component.FindAll(".stop-workspace__fields details"));
  }

  [Fact]
  public async Task EditingChangesOnlyTheBoundDraft()
  {
    using var context = Context();
    var stop = Stop("Destination");
    var changes = 0;
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [stop])
        .Add(x => x.CanEdit, true)
        .Add(x => x.Changed, () => changes++)
    );

    await component
      .Find($"#stop-{stop.Id}-facility")
      .InputAsync(new ChangeEventArgs { Value = "New name" });

    Assert.Equal("New name", stop.Name);
    Assert.Equal(1, changes);
    Assert.Empty(component.FindAll("button[type='submit']"));
  }

  [Fact]
  public async Task ReorderingPreservesSelectedStopEditorAndResourceSnapshot()
  {
    using var context = Context();
    var first = Stop("First");
    var second = Stop("Second");
    second.TruckNumber = "11005";
    second.DriverName = "James William Respicio Campos";
    var stops = new List<DispatchWorkspaceStop> { first, second };
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, stops)
        .Add(x => x.CanEdit, true)
        .Add(x => x.SelectedStopId, second.Id)
    );

    await component
      .Find("button[aria-label='Reorder stop 2']")
      .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });

    Assert.Same(second, stops[0]);
    Assert.Single(component.FindAll(".stop-workspace__editor"));
    Assert.Equal(
      $"stop-editor-{second.Id}",
      component.Find(".stop-workspace__editor").Id
    );
    Assert.Contains(
      "11005",
      component.Find(".stop-workspace__resources").TextContent
    );
    Assert.Equal(
      second.DriverName,
      component
        .Find($"[data-stop-id='{second.Id}'] .stop-workspace__driver strong")
        .TextContent
    );
    Assert.Empty(component.FindAll(".stop-workspace__assignment select"));
    Assert.All(
      component.FindAll(".stop-workspace__drag"),
      handle => Assert.Equal("true", handle.GetAttribute("draggable"))
    );
  }

  [Fact]
  public async Task RemovalRequiresExplicitDraftConfirmation()
  {
    using var context = Context();
    var stop = Stop("Destination");
    var stops = new List<DispatchWorkspaceStop> { stop };
    var changes = 0;
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, stops)
        .Add(x => x.CanEdit, true)
        .Add(x => x.Changed, () => changes++)
    );

    await component
      .Find(".stop-workspace__remove button")
      .ClickAsync(new MouseEventArgs());
    Assert.Single(stops);
    Assert.Equal(0, changes);
    await component
      .Find(".stop-workspace__remove .btn--danger")
      .ClickAsync(new MouseEventArgs());

    Assert.Empty(stops);
    Assert.Equal(1, changes);
  }

  [Fact]
  public async Task InvalidClockPreventsParentSaveUntilCorrected()
  {
    using var context = Context();
    var stop = Stop("Destination");
    stop.AppointmentMode = "at";
    stop.ScheduledDate = new(2026, 9, 15);
    stop.ScheduledTime = new(9, 0);
    var next = Stop("Next destination");
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [stop, next]).Add(x => x.CanEdit, true)
    );

    await component
      .Find($"#stop-{stop.Id}-time")
      .InputAsync(new ChangeEventArgs { Value = "noon-ish" });
    await component
      .Find($"[data-stop-id='{next.Id}'] .stop-workspace__select")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(
      $"stop-editor-{next.Id}",
      component.Find(".stop-workspace__editor").Id
    );
    Assert.False(component.Instance.Validate(out var error));
    Assert.NotNull(error);
    component.Render();
    Assert.Single(component.FindAll(".stop-workspace__editor"));
    Assert.Equal(
      $"stop-editor-{stop.Id}",
      component.Find(".stop-workspace__editor").Id
    );
    Assert.Equal(
      "noon-ish",
      component.Find($"#stop-{stop.Id}-time").GetAttribute("value")
    );
    await component
      .Find($"#stop-{stop.Id}-time")
      .InputAsync(new ChangeEventArgs { Value = "12:00 PM" });

    Assert.True(component.Instance.Validate(out error));
    Assert.Null(error);
    Assert.Equal(new TimeOnly(12, 0), stop.ScheduledTime);
  }

  [Fact]
  public async Task ReplacingDraftDiscardsUncommittedClockText()
  {
    using var context = Context();
    var stop = Stop("Destination");
    stop.AppointmentMode = "at";
    stop.ScheduledTime = new(9, 0);
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [stop]).Add(x => x.CanEdit, true)
    );
    await component
      .Find($"#stop-{stop.Id}-time")
      .InputAsync(new ChangeEventArgs { Value = "broken" });
    Assert.False(component.Instance.Validate(out _));

    var restored = Stop("Destination");
    restored.Id = stop.Id;
    restored.AppointmentMode = "at";
    restored.ScheduledTime = new(9, 0);
    component.Render(p => p.Add(x => x.Stops, [restored]));

    Assert.True(component.Instance.Validate(out _));
    Assert.Equal(
      "09:00 AM",
      component.Find($"#stop-{stop.Id}-time").GetAttribute("value")
    );
  }

  [Fact]
  public void RecordedStopsAndReadOnlyWorkspacesOfferNoDraftChanges()
  {
    using var context = Context();
    var stop = Stop("Completed pickup");
    stop.CanEdit = stop.CanMove = stop.CanRemove = false;
    stop.LockReason = "This stop has recorded actual events.";
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [stop]).Add(x => x.CanEdit, true)
    );

    Assert.True(
      component.Find("fieldset.stop-workspace__fields").HasAttribute("disabled")
    );
    Assert.NotNull(component.Find($"#stop-{stop.Id}-street"));
    Assert.NotNull(component.Find($"#stop-{stop.Id}-contact"));
    Assert.NotNull(component.Find($"#stop-{stop.Id}-instructions"));
    Assert.Empty(component.FindAll(".stop-workspace__remove"));
    Assert.Empty(component.FindAll(".stop-workspace__order"));
    Assert.Contains(stop.LockReason, component.Markup);
    Assert.Empty(component.FindAll(".stop-workspace__drag"));
  }

  [Theory]
  [InlineData("drop", "planned", "Drop trailer", "Planned")]
  [InlineData("hook", "confirmed", "Hook trailer", "Confirmed")]
  public void ExplicitTransferShowsResourcesWithoutGuessingActualTime(
    string action,
    string status,
    string label,
    string statusLabel
  )
  {
    using var context = Context();
    var stop = Stop("Transfer yard");
    stop.Job = "Pick Up";
    stop.StopNo = "TRANSFER-42";
    stop.AppointmentReference = "YARD-22";
    stop.CanEdit = stop.CanMove = stop.CanRemove = false;
    stop.Transfer = new()
    {
      SwitchId = Guid.NewGuid(),
      Kind = "drop_hook",
      Action = action,
      Status = status,
      OutgoingTruckNumber = "11005",
      IncomingTruckNumber = "54777",
      OutgoingTrailerNumber = "9P1175",
      IncomingTrailerNumber = "9P1175",
      OutgoingDriverName = "Outgoing driver",
      IncomingDriverName = "Incoming driver",
    };
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, [stop]).Add(x => x.CanEdit, true)
    );

    Assert.Contains(label, component.Find(".stop-workspace__kind").TextContent);
    var transfer = component.Find(".stop-workspace__transfer");
    Assert.Contains("Trailer transfer", transfer.TextContent);
    Assert.Contains(statusLabel, transfer.TextContent);
    Assert.Contains("11005", transfer.TextContent);
    Assert.Contains("54777", transfer.TextContent);
    Assert.Contains("Outgoing driver", transfer.TextContent);
    Assert.Contains("Incoming driver", transfer.TextContent);
    Assert.DoesNotContain("AM", transfer.TextContent);
    Assert.DoesNotContain("PM", transfer.TextContent);
    Assert.DoesNotContain("PU #", component.Markup);
    Assert.Contains("TRANSFER-42", component.Markup);
    Assert.Contains("YARD-22", component.Markup);
    Assert.Empty(component.FindAll(".stop-workspace__fields"));
  }

  [Fact]
  public void SharedAddressDoesNotCreateATransferOrMergeVisits()
  {
    using var context = Context();
    var stops = new List<DispatchWorkspaceStop>
    {
      Stop("First visit"),
      Stop("Second visit"),
    };
    foreach (var stop in stops)
    {
      stop.Job = "Pick Up";
      stop.Address = "12 Yard Road";
      stop.City = "Max Meadows";
      stop.Province = "VA";
      stop.Country = "US";
    }
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, stops).Add(x => x.CanEdit, true)
    );

    Assert.Equal(2, component.FindAll(".stop-workspace__stop").Count);
    Assert.DoesNotContain("Visit 1 of 2", component.Markup);
    Assert.DoesNotContain("Visit 2 of 2", component.Markup);
    Assert.Empty(component.FindAll(".stop-workspace__transfer"));
    Assert.DoesNotContain("Drop trailer", component.Markup);
    Assert.DoesNotContain("Hook trailer", component.Markup);
  }

  [Fact]
  public async Task HandleAndDestinationTapReorderWithoutArrowButtons()
  {
    using var context = Context();
    var first = Stop("First");
    var second = Stop("Second");
    var stops = new List<DispatchWorkspaceStop> { first, second };
    var changed = 0;
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, stops)
        .Add(x => x.CanEdit, true)
        .Add(x => x.Changed, () => changed++)
    );
    Assert.Empty(
      component.FindAll(
        "button[title='Move stop up'], button[title='Move stop down']"
      )
    );
    await component
      .Find("button[aria-label='Reorder stop 2']")
      .ClickAsync(new MouseEventArgs());
    Assert.Equal(0, changed);
    await component
      .Find(".stop-workspace__select")
      .ClickAsync(new MouseEventArgs());
    Assert.Same(second, stops[0]);
    Assert.Equal(1, changed);
  }

  [Fact]
  public async Task DragPreviewMatchesInsertionWithoutMutatingUntilDrop()
  {
    using var context = Context();
    var first = Stop("First");
    var second = Stop("Second");
    var stops = new List<DispatchWorkspaceStop> { first, second };
    var changed = 0;
    var component = context.Render<DispatchStopWorkspace>(p =>
      p.Add(x => x.Stops, stops)
        .Add(x => x.CanEdit, true)
        .Add(x => x.Changed, () => changed++)
    );
    await component
      .Find("button[aria-label='Reorder stop 1']")
      .DragStartAsync(new DragEventArgs());
    await component
      .Find($"[data-stop-id='{second.Id}']")
      .DragOverAsync(new DragEventArgs());
    Assert.Contains(
      "is-drop-after",
      component.Find($"[data-stop-id='{second.Id}']").ClassName
    );
    Assert.Single(component.FindAll(".stop-workspace__drop-marker"));
    Assert.Same(first, stops[0]);
    Assert.Equal(0, changed);
    await component
      .Find($"[data-stop-id='{second.Id}']")
      .DropAsync(new DragEventArgs());
    Assert.Same(second, stops[0]);
    Assert.Equal(1, changed);
    Assert.Empty(component.FindAll(".stop-workspace__drop-marker"));
    await component
      .Find("button[aria-label='Reorder stop 2']")
      .DragStartAsync(new DragEventArgs());
    await component
      .Find($"[data-stop-id='{second.Id}']")
      .DragOverAsync(new DragEventArgs());
    Assert.Contains(
      "is-drop-before",
      component.Find($"[data-stop-id='{second.Id}']").ClassName
    );
    await component
      .Find("button[aria-label='Reorder stop 2']")
      .DragEndAsync(new DragEventArgs());
    Assert.Empty(component.FindAll(".stop-workspace__drop-marker"));
  }

  private static BunitContext Context()
  {
    var context = new BunitContext();
    context
      .JSInterop.SetupModule("./js/generated/dispatch/dispatch.js")
      .SetupVoid("revealStop", _ => true);
    return context;
  }

  private static DispatchWorkspaceStop Stop(string name) =>
    new()
    {
      Id = Guid.NewGuid(),
      Sequence = 1,
      Name = name,
      Job = "Delivery",
      CanEdit = true,
      CanMove = true,
      CanRemove = true,
      SegmentKey = "segment",
    };
}
