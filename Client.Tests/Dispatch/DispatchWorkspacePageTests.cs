using System.Net;
using System.Net.Http.Json;
using AngleSharp.Dom;
using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Client.Models.DTO.Execution;
using Client.Pages.Dispatch;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchWorkspacePageTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CompletionDraftKeepsNavigationAndPreviewsEarlierStops(
    bool discard
  )
  {
    var data = Workspace();
    data.Stops.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Sequence = 2,
        CanEdit = true,
        Job = "Pickup",
        Name = "Second facility",
      }
    );
    data.Stops.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Sequence = 3,
        CanEdit = true,
        Job = "Delivery",
        Name = "Third facility",
      }
    );
    foreach (var stop in data.Stops)
    {
      stop.CanCorrect = true;
      data.Load.Stops.Add(new() { Id = stop.Id, Job = stop.Job });
    }
    var writes = new List<StopCorrectionRequest>();
    using var context = Context(
      async (request, ct) =>
      {
        if (request.RequestUri!.AbsolutePath.StartsWith("/api/fleet/"))
          return MileageComponentResponses.Ok(
            new { items = Array.Empty<object>() }
          );
        if (request.Method == HttpMethod.Get)
          return MileageComponentResponses.Ok(data);
        var body = (
          await request.Content!.ReadFromJsonAsync<StopCorrectionRequest>(ct)
        )!;
        writes.Add(body);
        Assert.EndsWith(
          $"/stops/{data.Stops[2].Id}/correction",
          request.RequestUri.AbsolutePath
        );
        Assert.Equal("completed", body.Completion);
        foreach (var stop in data.Load.Stops)
          stop.CompletionOverride = true;
        data.Revision++;
        return MileageComponentResponses.Ok(data);
      }
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    page.WaitForAssertion(
      () => Assert.Single(page.FindAll("#correction-status"))
    );
    async Task Select(int index) =>
      await page.Find(
          $"[data-stop-id='{data.Stops[index].Id}'] "
            + ".stop-workspace__select"
        )
        .ClickAsync(new());
    await Select(2);
    await page.Find("#correction-status").ClickAsync(new());
    Assert.Equal(3, page.FindAll(".stop-workspace__badge--complete").Count);
    await Select(0);
    Assert.Equal(
      "true",
      page.Find("#correction-status").GetAttribute("aria-pressed")
    );
    await Select(2);
    Assert.Equal(
      "true",
      page.Find("#correction-status").GetAttribute("aria-pressed")
    );
    Assert.Empty(writes);
    await Click(page, discard ? "Discard" : "Save changes");
    page.WaitForAssertion(
      () => Assert.True(Button(page, "Save changes").HasAttribute("disabled"))
    );
    Assert.Equal(discard ? 0 : 1, writes.Count);
    Assert.Equal(
      discard ? 0 : 3,
      page.FindAll(".stop-workspace__badge--complete").Count
    );
    await Select(1);
  }

  [Theory]
  [InlineData(false, false, false)]
  [InlineData(true, false, false)]
  [InlineData(false, true, false)]
  [InlineData(false, false, true)]
  public async Task StopDraftsSurviveStopSelectionAndSaveTogether(
    bool discard,
    bool retry,
    bool overlap
  )
  {
    var data = Workspace();
    var truck = Guid.NewGuid();
    var firstDriver = Guid.NewGuid();
    var secondDriver = Guid.NewGuid();
    Guid? finalSecondDriver = overlap ? null : secondDriver;
    data.Stops.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Sequence = 2,
        CanEdit = true,
        Job = "Delivery",
        Name = "Second facility",
      }
    );
    foreach (var stop in data.Stops)
    {
      stop.CanCorrect = true;
      stop.TruckId = truck;
      data.Load.Stops.Add(new() { Id = stop.Id, Job = stop.Job });
    }
    var first = data.Stops[0].Id;
    var second = data.Stops[1].Id;
    var resourceReads = 0;
    var writes = new List<StopCorrectionRequest>();
    using var context = Context(
      async (request, ct) =>
      {
        var path = request.RequestUri!.AbsolutePath;
        if (path.StartsWith("/api/fleet/"))
          resourceReads++;
        if (path == "/api/fleet/drivers")
          return MileageComponentResponses.Ok(
            new
            {
              items = new[]
              {
                new
                {
                  id = firstDriver,
                  name = "First driver",
                  isActive = true,
                },
                new
                {
                  id = secondDriver,
                  name = "Second driver",
                  isActive = true,
                },
              },
            }
          );
        if (path.StartsWith("/api/fleet/"))
          return MileageComponentResponses.Ok(
            new
            {
              items = new[]
              {
                new
                {
                  id = truck,
                  unitNumber = "101",
                  isActive = true,
                },
              },
            }
          );
        if (request.Method == HttpMethod.Get)
          return MileageComponentResponses.Ok(data);
        var body = (
          await request.Content!.ReadFromJsonAsync<StopCorrectionRequest>(ct)
        )!;
        writes.Add(body);
        if (retry && writes.Count == 2)
          return new(HttpStatusCode.ServiceUnavailable);
        Assert.Equal(data.Revision, body.ExpectedRevision);
        var target = path.Contains(first.ToString())
          ? data.Stops[0]
          : data.Stops[1];
        foreach (
          var row in body.AssignmentScope == "onward"
            ? data.Stops
            : new List<DispatchWorkspaceStop> { target }
        )
        {
          row.DriverId = body.DriverId;
          row.DriverName =
            body.DriverId == firstDriver ? "First driver"
            : body.DriverId.HasValue ? "Second driver"
            : "";
        }
        data.Revision++;
        return MileageComponentResponses.Ok(data);
      }
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    async Task Select(Guid stopId)
    {
      await page.Find($"[data-stop-id='{stopId}'] .stop-workspace__select")
        .ClickAsync(new());
      page.WaitForAssertion(
        () =>
          Assert.False(page.Find("#correction-driver").HasAttribute("disabled"))
      );
    }
    page.WaitForAssertion(
      () =>
        Assert.False(page.Find("#correction-driver").HasAttribute("disabled"))
    );
    async Task ChooseDriver(string name)
    {
      await page.Find("#correction-driver").InputAsync(name);
      page.WaitForAssertion(() => Assert.Single(page.FindAll("[role=option]")));
      await page.Find("[role=option]").ClickAsync(new());
    }
    await Select(second);
    await Select(first);
    Assert.Equal(3, resourceReads);
    await ChooseDriver("First driver");
    if (overlap)
      await page.Find("#correction-scope").ChangeAsync("onward");
    await Select(second);
    if (overlap)
      Assert.Equal(
        "First driver",
        page.Find("#correction-driver").GetAttribute("value")
      );
    await ChooseDriver(overlap ? "No driver" : "Second driver");
    await Select(first);
    Assert.Equal(
      "First driver",
      page.Find("#correction-driver").GetAttribute("value")
    );
    Assert.Contains(
      "First driver",
      page.Find($"[data-stop-id='{first}']").TextContent
    );
    if (!overlap)
      Assert.Contains(
        "Second driver",
        page.Find($"[data-stop-id='{second}']").TextContent
      );
    else
      Assert.DoesNotContain(
        "First driver",
        page.Find($"[data-stop-id='{second}']").TextContent
      );
    Assert.Empty(writes);
    Assert.Equal(3, resourceReads);
    await Click(page, discard ? "Discard" : "Save changes");
    if (discard)
    {
      Assert.Empty(writes);
      Assert.DoesNotContain(
        "First driver",
        page.Find($"[data-stop-id='{first}']").TextContent
      );
      Assert.DoesNotContain(
        "Second driver",
        page.Find($"[data-stop-id='{second}']").TextContent
      );
    }
    else
    {
      page.WaitForAssertion(() => Assert.Equal(2, writes.Count));
      if (retry)
      {
        page.WaitForAssertion(
          () => Assert.Contains("Remaining stop changes", page.Markup)
        );
        Assert.Contains("Remaining stop changes", page.Markup);
        await Click(page, "Retry save");
        page.WaitForAssertion(() => Assert.Equal(3, writes.Count));
        Assert.Equal(writes[1].IdempotencyKey, writes[2].IdempotencyKey);
        Assert.Equal(writes[1].ExpectedRevision, writes[2].ExpectedRevision);
      }
      Assert.Equal(firstDriver, data.Stops[0].DriverId);
      Assert.Equal(finalSecondDriver, data.Stops[1].DriverId);
    }
    page.WaitForAssertion(
      () => Assert.Contains("All changes saved", page.Markup)
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task HeaderOwnsStopSaveAndDiscard(bool operation)
  {
    var data = Workspace();
    var stop = data.Stops[0];
    stop.CanCorrect = true;
    data.Load.Stops =
    [
      new()
      {
        Id = stop.Id,
        Job = "Pick Up",
        StateAfter = "Loaded",
        CompletionIdentity = "source",
      },
    ];
    var writes = new List<string>();
    using var context = Context(
      async (request, ct) =>
      {
        var path = request.RequestUri!.AbsolutePath;
        if (path.StartsWith("/api/fleet/"))
          return MileageComponentResponses.Ok(
            new { items = Array.Empty<object>(), totalCount = 0 }
          );
        if (request.Method == HttpMethod.Get)
          return MileageComponentResponses.Ok(data);
        writes.Add(path);
        if (operation)
        {
          var body =
            await request.Content!.ReadFromJsonAsync<StopOperationUpdate>(ct);
          data.Load.Stops[0].ManualAction = body!.Action;
          data.Load.Stops[0].ManualStateAfter = body.StateAfter;
          data.Load.Stops[0].OperationRevision++;
          return MileageComponentResponses.Ok(
            new StopOperationState(
              body.Action,
              body.StateAfter,
              1,
              DateTime.UtcNow
            )
          );
        }
        data.Load.Stops[0].CompletionOverride = true;
        data.Revision++;
        return MileageComponentResponses.Ok(data);
      }
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    page.WaitForElement("#correction-status");
    async Task Edit()
    {
      if (operation)
        await page.InvokeAsync(
          () =>
            page.Find(".stop-operation select")
              .ChangeAsync(new() { Value = "Driver start" })
        );
      else
        await page.InvokeAsync(
          () => page.Find("#correction-status").ClickAsync(new())
        );
    }
    await Edit();
    Assert.Empty(
      page.FindAll(
        ".stop-correction__actions, .stop-operation__actions .btn--primary"
      )
    );
    Assert.False(Button(page, "Save changes").HasAttribute("disabled"));
    await Click(page, "Discard");
    Assert.Empty(writes);
    Assert.True(Button(page, "Save changes").HasAttribute("disabled"));
    await Edit();
    await Click(page, "Save changes");
    Assert.Single(writes);
    Assert.EndsWith(operation ? "/operation" : "/correction", writes[0]);
    page.WaitForAssertion(
      () => Assert.True(Button(page, "Save changes").HasAttribute("disabled"))
    );
  }

  [Fact]
  public async Task OnlyExplicitSaveWritesAndIncludesDraftAndOpenedVersion()
  {
    var data = Workspace();
    UpdateDispatchWorkspaceRequest? body = null;
    using var context = Context(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return MileageComponentResponses.Ok(data);
        var content = request.Content!;
        body = await content.ReadFromJsonAsync<UpdateDispatchWorkspaceRequest>(
          ct
        );
        data.Metadata = body!.Metadata;
        data.Revision++;
        return MileageComponentResponses.Ok(data);
      }
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    await Input(page, "Keep trailer sealed.");
    Assert.Null(body);
    Assert.Contains("Unsaved changes", page.Markup);
    await Click(page, "Save changes");
    Assert.Equal(7, body!.ExpectedRevision);
    Assert.Equal(data.SourceFingerprint, body.SourceFingerprint);
    Assert.NotEqual(Guid.Empty, body.IdempotencyKey);
    Assert.Equal("Keep trailer sealed.", body.Metadata.LoadInstructions);
    Assert.Equal(data.Stops.Select(x => x.Id), body.Stops.Select(x => x.Id));
    Assert.Contains("All changes saved", page.Markup);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task SavedForecastRefreshKeepsEditorAndAnyNewerDraft(
    bool editAgain
  )
  {
    var data = Workspace();
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var started = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var reads = 0;
    using var context = Context(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
        {
          if (++reads == 1)
            return MileageComponentResponses.Ok(data);
          started.SetResult();
          return await pending.Task;
        }
        var content = request.Content!;
        var body =
          await content.ReadFromJsonAsync<UpdateDispatchWorkspaceRequest>(ct);
        data.Metadata = body!.Metadata;
        data.Revision++;
        return MileageComponentResponses.Ok(data);
      }
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    await Input(page, "Saved instructions");
    var save = Click(page, "Save changes");
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    page.WaitForAssertion(
      () =>
        Assert.False(page.Find("#load-instructions").HasAttribute("disabled"))
    );
    var editor = page.FindComponent<DispatchStopWorkspace>().Instance;
    if (editAgain)
      await Input(page, "Newer unsaved instructions");
    pending.SetResult(MileageComponentResponses.Ok(data));
    await save;
    Assert.Same(editor, page.FindComponent<DispatchStopWorkspace>().Instance);
    if (editAgain)
    {
      Assert.Contains("Newer unsaved instructions", page.Markup);
      Assert.Contains("Unsaved changes", page.Markup);
    }
    else
      Assert.Contains("All changes saved", page.Markup);
  }

  [Fact]
  public async Task UncertainSaveKeepsExactRetryAndLocksFields()
  {
    var data = Workspace();
    var writes = new List<UpdateDispatchWorkspaceRequest>();
    using var context = Context(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
          return MileageComponentResponses.Ok(data);
        var content = request.Content!;
        writes.Add(
          (await content.ReadFromJsonAsync<UpdateDispatchWorkspaceRequest>(ct))!
        );
        if (writes.Count == 1)
          return new(HttpStatusCode.GatewayTimeout);
        data.Metadata = writes[^1].Metadata;
        return MileageComponentResponses.Ok(data);
      }
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    await Input(page, "Do not lose this draft");
    await Click(page, "Save changes");
    Assert.True(page.Find("#load-instructions").HasAttribute("disabled"));
    Assert.True(
      page.Find(".dispatch-details__execution").HasAttribute("disabled")
    );
    Assert.Contains("Do not lose this draft", page.Markup);
    await Click(page, "Retry save");
    Assert.Equal(2, writes.Count);
    Assert.Equal(writes[0].IdempotencyKey, writes[1].IdempotencyKey);
    Assert.Equal(
      writes[0].Metadata.LoadInstructions,
      writes[1].Metadata.LoadInstructions
    );
  }

  [Fact]
  public async Task ConflictAndTabChangesRetainDraftUntilExplicitDiscard()
  {
    var data = Workspace();
    using var context = Context(
      (request, _) =>
        Task.FromResult(
          request.Method == HttpMethod.Get
            ? MileageComponentResponses.Ok(data)
            : MileageComponentResponses.Error<DispatchWorkspaceResponse>(
              HttpStatusCode.Conflict,
              "The load changed. Reload it."
            )
        )
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    page.WaitForElement(".dispatch-details__execution");
    Assert.False(
      page.Find(".dispatch-details__execution").HasAttribute("disabled")
    );
    await Input(page, "Keep me");
    Assert.True(
      page.Find(".dispatch-details__execution").HasAttribute("disabled")
    );
    await Click(page, "Broker & billing");
    await Click(page, "Overview");
    Assert.Contains("Keep me", page.Markup);
    await Click(page, "Save changes");
    Assert.Contains("Keep me", page.Markup);
    Assert.True(Button(page, "Save changes").HasAttribute("disabled"));
    Assert.True(
      page.Find(".dispatch-details__execution").HasAttribute("disabled")
    );
    await Click(page, "Discard");
    Assert.DoesNotContain("Keep me", page.Markup);
    Assert.False(
      page.Find(".dispatch-details__execution").HasAttribute("disabled")
    );
  }

  [Fact]
  public async Task OverviewKeepsResourceActionsAndDraftThroughSectionChanges()
  {
    var data = Workspace();
    var requests = new List<string>();
    using var context = Context(
      (_, _) => Task.FromResult(MileageComponentResponses.Ok(data)),
      request =>
        requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}")
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    page.WaitForElement("#load-instructions");
    page.WaitForAssertion(() => Assert.Equal(3, requests.Count));
    var initialRequests = requests.ToArray();
    Assert.Equal(
      ["Broker & billing", "Overview", "History"],
      page.FindAll(".dispatch-details__tab").Select(x => x.TextContent.Trim())
    );
    AssertCurrentTab(page, "Overview");
    var execution = page.Find(".dispatch-details__execution");
    Assert.Null(execution.Closest("[hidden]"));
    Assert.False(execution.HasAttribute("disabled"));
    Assert.DoesNotContain(
      page.FindAll("button"),
      button => button.TextContent.Trim() == "Manage Switch"
    );
    Assert.Empty(page.FindAll(".truck-assignment"));
    var switching = page.FindComponent<DispatchSwitchSection>().Instance;
    var stopEditor = page.FindComponent<DispatchStopWorkspace>().Instance;

    await page.Find("#load-instructions").InputAsync("Keep trailer sealed.");
    foreach (var tab in new[] { "Broker & billing", "History" })
    {
      await Click(page, tab);
      AssertCurrentTab(page, tab);
      Assert.NotNull(
        page.Find(".dispatch-details__execution").Closest("[hidden]")
      );
    }
    await Click(page, "Overview");

    AssertCurrentTab(page, "Overview");
    Assert.Null(page.Find(".dispatch-details__execution").Closest("[hidden]"));
    Assert.Same(
      switching,
      page.FindComponent<DispatchSwitchSection>().Instance
    );
    Assert.Same(
      stopEditor,
      page.FindComponent<DispatchStopWorkspace>().Instance
    );
    Assert.Equal(
      "Keep trailer sealed.",
      page.Find("#load-instructions").GetAttribute("value")
    );
    Assert.Empty(page.FindAll(".truck-assignment"));
    Assert.Equal(initialRequests, requests);
    Assert.All(requests, request => Assert.StartsWith("GET ", request));
  }

  [Fact]
  public async Task SelectedStopOpensContextualTransferWithoutBusinessWrites()
  {
    var data = Workspace();
    var target = Guid.NewGuid();
    data.Stops.Add(
      new()
      {
        Id = target,
        Sequence = 2,
        CanEdit = true,
        CanMove = true,
        CanRemove = true,
        SegmentKey = "ordinary",
        Job = "Delivery",
        Name = "Selected receiver",
        Address = "2 Main St",
        City = "Miami",
        Country = "US",
        AppointmentMode = "unscheduled",
      }
    );
    var requests = new List<string>();
    using var context = Context(
      (request, _) =>
        Task.FromResult(
          request.RequestUri!.AbsolutePath.EndsWith("/switch-workspace")
            ? MileageComponentResponses.Ok(
              new SwitchWorkspace(
                [
                  new(
                    data.Load.Id,
                    2048,
                    data.SourceFingerprint,
                    null,
                    null,
                    null,
                    data.Stops.Select(stop => new SwitchVisitOption(
                        stop.Id,
                        stop.Sequence,
                        stop.Job,
                        stop.Name,
                        stop.Address,
                        null,
                        null,
                        false
                      ))
                      .ToArray()
                  ),
                ],
                [],
                [],
                [],
                []
              )
            )
            : MileageComponentResponses.Ok(data)
        ),
      request =>
        requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}")
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    page.WaitForAssertion(() => Assert.Equal(3, requests.Count));
    await page.WaitForElement(
        $"[data-stop-id='{target}'] .stop-workspace__select"
      )
      .ClickAsync(new MouseEventArgs());
    var initialRequests = requests.ToArray();

    await Click(page, "+ Add stop");
    Assert.Contains(
      "After stop 2",
      page.Find(".stop-workspace__add").TextContent
    );
    await Click(page, "Drop / Hook trailer");

    AssertCurrentTab(page, "Overview");
    Assert.Equal(
      $"stop-editor-{target}",
      page.Find(".stop-workspace__editor").Id
    );
    var heading = page.WaitForElement(".dispatch-switch h2");
    Assert.Equal("Transfer at selected stop", heading.TextContent.Trim());
    Assert.Equal("-1", heading.GetAttribute("tabindex"));
    Assert.Null(heading.Closest("[hidden]"));
    var focus = Assert.Single(
      context.JSInterop.Invocations,
      call => call.Identifier == "Blazor._internal.domWrapper.focus"
    );
    Assert.Equal(false, focus.Arguments[1]);
    page.Render();
    Assert.Single(
      context.JSInterop.Invocations,
      call => call.Identifier == "Blazor._internal.domWrapper.focus"
    );
    Assert.Equal(initialRequests.Length + 1, requests.Count);
    Assert.EndsWith("/execution/switch-workspace", requests[^1]);
    Assert.All(requests, request => Assert.StartsWith("GET ", request));
  }

  [Fact]
  public async Task BrokerAndRateShareOneTabAndRetainDraftsWithoutNewRequests()
  {
    var data = Workspace();
    data.Metadata.BrokerCompany = "Opened broker";
    data.Metadata.BrokerContact = "Opened contact";
    data.Metadata.Price = 2500m;
    var requests = new List<string>();
    using var context = Context(
      (_, _) => Task.FromResult(MileageComponentResponses.Ok(data)),
      request =>
        requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}")
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    page.WaitForElement("#load-instructions");
    page.WaitForAssertion(() => Assert.Equal(3, requests.Count));
    var initialRequests = requests.ToArray();
    var broker = page.FindComponent<DispatchBroker>().Instance;
    var billing = page.FindComponent<DispatchBilling>().Instance;

    AssertCurrentTab(page, "Overview");
    Assert.Empty(page.FindAll(".dispatch-details__workspace .dispatch-broker"));
    Assert.NotNull(page.Find(".dispatch-broker").Closest("[hidden]"));
    Assert.NotNull(Field(page, "Load rate").Closest("[hidden]"));
    await Click(page, "Broker & billing");

    AssertCurrentTab(page, "Broker & billing");
    Assert.Null(page.Find(".dispatch-broker").Closest("[hidden]"));
    Assert.Null(Field(page, "Load rate").Closest("[hidden]"));
    Assert.Equal(
      "Opened broker",
      Field(page, "Broker company").GetAttribute("value")
    );
    Assert.Empty(page.FindAll(".dispatch-broker details"));
    Assert.Equal("2500", Field(page, "Load rate").GetAttribute("value"));
    await Field(page, "Broker company").InputAsync("Draft broker");
    await Field(page, "Contact name").InputAsync("Draft contact");
    await Field(page, "Load rate").ChangeAsync("3125.50");
    await Click(page, "Overview");

    AssertCurrentTab(page, "Overview");
    Assert.NotNull(page.Find(".dispatch-broker").Closest("[hidden]"));
    Assert.NotNull(Field(page, "Load rate").Closest("[hidden]"));
    await Click(page, "Broker & billing");

    AssertCurrentTab(page, "Broker & billing");
    Assert.Same(broker, page.FindComponent<DispatchBroker>().Instance);
    Assert.Same(billing, page.FindComponent<DispatchBilling>().Instance);
    Assert.Equal(
      "Draft broker",
      Field(page, "Broker company").GetAttribute("value")
    );
    Assert.Equal(
      "Draft contact",
      Field(page, "Contact name").GetAttribute("value")
    );
    Assert.Equal("3125.50", Field(page, "Load rate").GetAttribute("value"));
    Assert.Contains("Unsaved changes", page.Markup);
    Assert.Equal(initialRequests, requests);
    Assert.All(requests, request => Assert.StartsWith("GET ", request));
    Assert.Equal("Opened broker", data.Metadata.BrokerCompany);
    Assert.Equal(2500m, data.Metadata.Price);
  }

  [Fact]
  public async Task SecondDispatcherCannotOverwriteFirstSavedVersion()
  {
    var data = Workspace();
    var reads = 0;
    var versions = new List<long>();
    using var context = Context(
      async (request, ct) =>
      {
        if (request.Method == HttpMethod.Get)
        {
          reads++;
          return MileageComponentResponses.Ok(data);
        }
        var content = request.Content!;
        var body =
          await content.ReadFromJsonAsync<UpdateDispatchWorkspaceRequest>(ct);
        versions.Add(body!.ExpectedRevision);
        if (body.ExpectedRevision != data.Revision)
          return MileageComponentResponses.Error<DispatchWorkspaceResponse>(
            HttpStatusCode.Conflict,
            "Another dispatcher updated this load. Reload the saved version."
          );
        data.Metadata = body.Metadata;
        data.Revision++;
        return MileageComponentResponses.Ok(data);
      }
    );
    var first = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    var second = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    await Input(first, "First dispatcher's saved instructions");
    await Input(second, "Second dispatcher's unsaved instructions");
    await Click(first, "Save changes");
    await Click(second, "Save changes");

    Assert.Equal([7L, 7L], versions);
    Assert.Equal(3, reads);
    Assert.Contains("Another dispatcher updated", second.Markup);
    Assert.Contains("Second dispatcher's unsaved instructions", second.Markup);
    Assert.Contains("First dispatcher's saved instructions", first.Markup);
    Assert.Equal(
      "First dispatcher's saved instructions",
      data.Metadata.LoadInstructions
    );
    Assert.True(Button(second, "Save changes").HasAttribute("disabled"));
    await Click(second, "Reload saved version");
    Assert.Equal(3, reads);
    Assert.Contains("Second dispatcher's unsaved instructions", second.Markup);
    await Click(second, "Discard draft and reload");
    Assert.Equal(4, reads);
    Assert.Contains("First dispatcher's saved instructions", second.Markup);
    Assert.DoesNotContain(
      "Second dispatcher's unsaved instructions",
      second.Markup
    );
  }

  [Fact]
  public async Task OldReadCannotReplaceSameLoadAfterLeavingAndReturning()
  {
    var first = Workspace();
    var other = Workspace();
    var pending = new TaskCompletionSource<HttpResponseMessage>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var calls = 0;
    using var context = Context(
      (request, _) =>
      {
        if (request.RequestUri!.AbsolutePath.Contains(first.Load.Id.ToString()))
        {
          if (++calls == 1)
            return pending.Task;
          first.Metadata.CustomerName = "Fresh customer";
          return Task.FromResult(MileageComponentResponses.Ok(first));
        }
        return Task.FromResult(MileageComponentResponses.Ok(other));
      }
    );
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, first.Load.Id)
    );
    page.Render(p => p.Add(x => x.Id, other.Load.Id));
    page.WaitForElement("#load-instructions");
    page.Render(p => p.Add(x => x.Id, first.Load.Id));
    page.WaitForAssertion(() => Assert.Contains("Fresh customer", page.Markup));
    var old = Workspace();
    old.Load.Id = first.Load.Id;
    old.Metadata.CustomerName = "Stale customer";
    pending.SetResult(MileageComponentResponses.Ok(old));
    await page.InvokeAsync(() => Task.CompletedTask);
    Assert.DoesNotContain("Stale customer", page.Markup);
  }

  [Fact]
  public async Task InternalNavigationRequiresExplicitDiscard()
  {
    var data = Workspace();
    using var context = Context(
      (_, _) => Task.FromResult(MileageComponentResponses.Ok(data))
    );
    var navigation = context.Services.GetRequiredService<NavigationManager>();
    var page = context.Render<DispatchDetails>(p =>
      p.Add(x => x.Id, data.Load.Id)
    );
    await Input(page, "Unsaved dispatch work");
    await page.InvokeAsync(() => navigation.NavigateTo("/dispatch"));
    page.WaitForElement("[role=alertdialog]");
    Assert.DoesNotContain("/dispatch", new Uri(navigation.Uri).AbsolutePath);
    await Click(page, "Keep editing");
    Assert.Contains("Unsaved dispatch work", page.Markup);
    await page.InvokeAsync(() => navigation.NavigateTo("/dispatch"));
    await Click(page, "Discard and leave");
    Assert.EndsWith("/dispatch", navigation.Uri);
  }

  private static ClientComponentContext Context(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
    Action<HttpRequestMessage>? observe = null
  )
  {
    var context = new ClientComponentContext(
      (request, ct) =>
      {
        observe?.Invoke(request);
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/documents"))
          return Task.FromResult(
            MileageComponentResponses.Ok(new List<DispatchDocumentInfo>())
          );
        if (path.EndsWith("/activity"))
        {
          var id = Guid.Parse(request.RequestUri.Segments[^2].Trim('/'));
          return Task.FromResult(
            MileageComponentResponses.Ok(
              new DispatchActivityPage(id, 0, [], null, 0, [], null)
            )
          );
        }
        return send(request, ct);
      }
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var auth = context.AddAuthorization();
    auth.SetAuthorized("Dispatcher");
    auth.SetRoles("Dispatch");
    return context;
  }

  private static DispatchWorkspaceResponse Workspace() =>
    new()
    {
      Load = new()
      {
        Id = Guid.NewGuid(),
        LoadNumber = 2048,
        Status = "assigned",
      },
      Revision = 7,
      SourceFingerprint = new string('A', 64),
      CanEdit = true,
      Metadata = new() { OrderNumber = "ORDER-20", Currency = "USD" },
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          CanEdit = true,
          CanMove = true,
          CanRemove = true,
          SegmentKey = "ordinary",
          Job = "Pickup",
          Name = "First facility",
          Address = "1 Main St",
          City = "Miami",
          Country = "US",
          AppointmentMode = "unscheduled",
        },
      ],
    };

  private static Task Click(
    IRenderedComponent<DispatchDetails> page,
    string label
  ) => page.InvokeAsync(() => Button(page, label).ClickAsync(new()));

  private static Task Input(
    IRenderedComponent<DispatchDetails> page,
    string value
  )
  {
    page.WaitForElement("#load-instructions");
    return page.InvokeAsync(
      () => page.Find("#load-instructions").InputAsync(new() { Value = value })
    );
  }

  private static IElement Button(
    IRenderedComponent<DispatchDetails> page,
    string label
  ) => page.FindAll("button").Single(x => x.TextContent.Trim() == label);

  private static void AssertCurrentTab(
    IRenderedComponent<DispatchDetails> page,
    string label
  )
  {
    var current = Assert.Single(
      page.FindAll(".dispatch-details__tab[aria-current='page']")
    );
    Assert.Equal(label, current.TextContent.Trim());
  }

  private static IElement Field(
    IRenderedComponent<DispatchDetails> page,
    string label
  ) =>
    page.FindAll(".dispatch-details__commercial label")
      .Single(x => x.TextContent.Trim() == label)
      .QuerySelector("input")!;
}
