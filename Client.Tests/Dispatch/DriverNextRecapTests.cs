using Client.Shared.DriverStatus.DriverNextRecap;
using Client.Shared.DriverStatus;
using Bunit;
using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DriverNextRecapTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void VerifiedCurrentDriverRecapShowsOnlyHomeDateAndCreditedHours()
    {
        using var context = Context();
        var component = context.Render<DriverNextRecap>(p => p.Add(x => x.Snapshot, Snapshot()));

        Assert.Equal("Sep 11", component.Find("time").TextContent);
        Assert.Contains("+3h 05m", component.Find("strong").TextContent);
        Assert.EndsWith("-04:00", component.Find("time").GetAttribute("datetime"));
        Assert.DoesNotContain("00:00", component.Find(".driver-next-recap").TextContent);
        Assert.DoesNotContain("UTC", component.Find(".driver-next-recap").TextContent);
        Assert.DoesNotContain("home", component.Find(".driver-next-recap").TextContent);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unverified")]
    [InlineData("no-time")]
    [InlineData("no-hours")]
    [InlineData("zero")]
    [InlineData("returned")]
    [InlineData("expired")]
    [InlineData("future-calculation")]
    [InlineData("invalid-validity")]
    public void UnavailableSnapshotKeepsOneQuietLabeledPlaceholder(string invalid)
    {
        using var context = Context();
        DriverCycleSnapshot? snapshot = Snapshot();
        snapshot = invalid switch
        {
            "missing" => null,
            "unverified" => snapshot with { Cycle = snapshot.Cycle with { RecapVerified = false } },
            "no-time" => snapshot with { Cycle = snapshot.Cycle with { NextRecapAt = null } },
            "no-hours" => snapshot with { Cycle = snapshot.Cycle with { NextRecapMinutes = null } },
            "zero" => snapshot with { Cycle = snapshot.Cycle with { NextRecapMinutes = 0 } },
            "returned" => snapshot with { Cycle = snapshot.Cycle with { NextRecapAt = Start } },
            "expired" => snapshot with { CalculatedAt = Start.AddMinutes(-20).UtcDateTime,
                ValidUntil = Start.AddMinutes(-15).UtcDateTime },
            "invalid-validity" => snapshot with { ValidUntil = Start.UtcDateTime },
            _ => snapshot with { CalculatedAt = Start.AddMinutes(1).UtcDateTime }
        };
        var component = context.Render<DriverNextRecap>(p => p.Add(x => x.Snapshot, snapshot));

        Assert.Single(component.FindAll(".driver-next-recap"));
        Assert.Equal("Next recap", component.Find(".driver-next-recap > span").TextContent);
        Assert.Equal("—", component.Find("strong").TextContent.Trim());
        Assert.Empty(component.FindAll("time, [role=status], [title]"));
    }

    [Fact]
    public void RepeatedSnapshotKeepsOriginalExpiryAndReadyReplacementUpdatesAtomically()
    {
        var clock = new FakeTimeProvider(Start);
        using var context = Context(clock);
        var snapshot = Snapshot();
        var component = context.Render<DriverNextRecap>(p => p.Add(x => x.Snapshot, snapshot));
        var complete = component.Markup;
        clock.Advance(TimeSpan.FromMinutes(1));
        component.Render(p => p.Add(x => x.Snapshot, snapshot));
        Assert.Equal(complete, component.Markup);

        clock.Advance(TimeSpan.FromMinutes(1));
        component.Render(p => p.Add(x => x.Snapshot, snapshot));
        Assert.Equal(complete, component.Markup);
        clock.Advance(TimeSpan.FromMinutes(15));
        component.Render(p => p.Add(x => x.Snapshot, snapshot));
        Assert.Equal("—", component.Find("strong").TextContent.Trim());
        Assert.Empty(component.FindAll("time"));

        component.Render(p => p.Add(x => x.Snapshot, snapshot with { CalculatedAt = clock.GetUtcNow().UtcDateTime,
            ValidUntil = clock.GetUtcNow().AddMinutes(2).UtcDateTime, Cycle = snapshot.Cycle with { NextRecapMinutes = 60 } }));
        Assert.Contains("+1h 00m", component.Find("strong").TextContent);
        Assert.DoesNotContain("+3h 05m", component.Markup);
    }

    [Fact]
    public void ReturnedRecapOrRemovedTruckSnapshotCannotKeepThePreviousValue()
    {
        var clock = new FakeTimeProvider(Start);
        using var context = Context(clock);
        var snapshot = Snapshot();
        var component = context.Render<DriverNextRecap>(p => p.Add(x => x.Snapshot, snapshot));
        component.Render(p => p.Add(x => x.Snapshot, null));
        Assert.Equal("—", component.Find("strong").TextContent.Trim());

        var replacement = snapshot with { Cycle = snapshot.Cycle with { NextRecapAt = Start.AddMinutes(5), NextRecapMinutes = 60 } };
        component.Render(p => p.Add(x => x.Snapshot, replacement));
        Assert.Contains("+1h 00m", component.Find("strong").TextContent);
        Assert.DoesNotContain("+3h 05m", component.Markup);
        clock.Advance(TimeSpan.FromMinutes(5));
        component.Render(p => p.Add(x => x.Snapshot, replacement));
        Assert.Equal("—", component.Find("strong").TextContent.Trim());
    }

    private static BunitContext Context(FakeTimeProvider? clock = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<TimeProvider>(clock ?? new FakeTimeProvider(Start));
        return context;
    }

    private static DriverCycleSnapshot Snapshot() => new(Start.UtcDateTime, Start.AddMinutes(2).UtcDateTime,
        new(1400, new(2026, 9, 11, 0, 0, 0, TimeSpan.FromHours(-4)), 185, "America/New_York", true));
}
