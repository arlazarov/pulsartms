using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Bunit;
using Client.Models.DTO;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Shared.Fuel.FuelRecalculateButton;
using Client.Tests.Support;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Fleet;

[Trait("Category", "Fuel")]
[Trait("Kind", "Component")]
public sealed class FuelRecalculateButtonTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingAwayAndBackRejectsOldCompletionWithoutClearingNewBusyState(bool failure)
    {
        using var fixture = new Fixture();
        var component = fixture.Render();
        var first = component.Find("button").ClickAsync(new MouseEventArgs());
        var old = await fixture.NextAsync();
        await component.InvokeAsync(() => component.Render(p => p.Add(x => x.DispatchId, Guid.NewGuid())));
        Assert.True(old.Token.IsCancellationRequested);
        Assert.False(component.Find("button").HasAttribute("disabled"));
        await component.InvokeAsync(() => component.Render(p => p.Add(x => x.DispatchId, fixture.Dispatch)));
        var second = component.Find("button").ClickAsync(new MouseEventArgs());
        var current = await fixture.NextAsync();
        var events = fixture.Busy.Count;
        old.Reply(fixture.Result(), failure);
        await first;
        Assert.Empty(fixture.Results);
        Assert.Equal(0, fixture.Failures);
        Assert.Equal(events, fixture.Busy.Count);
        Assert.True(component.Find("button").HasAttribute("disabled"));
        current.Reply(fixture.Result());
        await second;
        Assert.Single(fixture.Results);
        Assert.False(fixture.Busy[^1]);
    }

    [Fact]
    public async Task SwitchingDuringBusyCallbackCannotSendARequestForTheNewDispatch()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var component = fixture.Render();
        await component.InvokeAsync(() => component.Render(p => p.Add(x => x.BusyChanged, async (bool busy) =>
        {
            if (busy) { entered.SetResult(); await release.Task; }
        })));
        var click = component.Find("button").ClickAsync(new MouseEventArgs());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await component.InvokeAsync(() => component.Render(p => p.Add(x => x.DispatchId, Guid.NewGuid())));
        release.SetResult();
        await click.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fixture.Requests.Reader.TryRead(out _));
    }

    [Fact]
    public async Task AResponseForAnotherDispatchCannotBePublished()
    {
        using var fixture = new Fixture();
        var component = fixture.Render();
        var click = component.Find("button").ClickAsync(new MouseEventArgs());
        var pending = await fixture.NextAsync();
        pending.Reply(fixture.Result() with { DispatchId = Guid.NewGuid() });
        await click;
        Assert.Empty(fixture.Results);
        Assert.Equal(1, fixture.Failures);
        Assert.False(fixture.Busy[^1]);
    }

    private sealed class Pending(CancellationToken token)
    {
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource<HttpResponseMessage> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Reply(AutomaticPlanningResult result, bool failure = false) => Completion.SetResult(new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new RequestResponseDTO<AutomaticPlanningResult>
                { Success = !failure, Response = failure ? null : result, Errors = failure ? ["Fixture failure"] : [] })
        });
    }

    private sealed class Fixture : IDisposable
    {
        public BunitContext Context { get; } = new();
        public Guid Dispatch { get; } = Guid.NewGuid();
        public Guid Truck { get; } = Guid.NewGuid();
        public Channel<Pending> Requests { get; } = Channel.CreateUnbounded<Pending>();
        public List<AutomaticPlanningResult> Results { get; } = [];
        public List<bool> Busy { get; } = [];
        public int Failures { get; private set; }
        private readonly HttpClient http;
        public Fixture()
        {
            http = new(new StubHttpMessageHandler(async (_, ct) =>
            {
                var pending = new Pending(ct);
                await Requests.Writer.WriteAsync(pending);
                return await pending.Completion.Task;
            })) { BaseAddress = new("https://fixture.invalid/") };
            var api = new ApiService(http);
            Context.Services.AddSingleton(api);
            Context.Services.AddSingleton(new PlanningDisplayCache(api));
        }
        public IRenderedComponent<FuelRecalculateButton> Render() => Context.Render<FuelRecalculateButton>(p =>
            p.Add(x => x.DispatchId, Dispatch).Add(x => x.Recalculated, (AutomaticPlanningResult result) => Results.Add(result))
                .Add(x => x.BusyChanged, (bool busy) => Busy.Add(busy)).Add(x => x.Failed, () => Failures++));
        public Task<Pending> NextAsync() => Requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public AutomaticPlanningResult Result() => new(Truck, Dispatch, 1, null, null);
        public void Dispose() { Context.Dispose(); http.Dispose(); }
    }
}
