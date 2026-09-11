using Client.Shared.DriverStatus;
using System.Globalization;
using Client.Models.DTO.Planning;
using Client.Shared;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class StopHoursDisplayTests
{
    [Theory]
    [InlineData(null, "—")]
    [InlineData(0, "0h 00m")]
    [InlineData(301, "+5h 01m")]
    [InlineData(-80, "−1h 20m")]
    [InlineData(int.MinValue, "−35791394h 08m")]
    public void SignedCycleFormatsTheServerValueWithoutClamping(int? minutes, string expected) =>
        Assert.Equal(expected, StopHoursDisplay.Signed(minutes));

    [Theory]
    [InlineData("recap", 0, "success")]
    [InlineData("restart", 0, "success")]
    [InlineData("recap", 10, "danger")]
    [InlineData("restart", 10, "danger")]
    [InlineData("recap", null, "muted")]
    [InlineData("restart", null, "muted")]
    [InlineData("recap", -1, "muted")]
    [InlineData("restart", -1, "muted")]
    public void AlternativeColorDependsOnKnownLatenessNotRestKind(string kind, int? late, string expected)
    {
        var alternative = new StopHoursAlternative(kind, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            late, 300, null, null);
        Assert.Equal(expected, StopHoursDisplay.AlternativeTone(alternative));
    }

    [Fact]
    public void TimestampKeepsEnglishLabelsAndTheStopsLocalOffset()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("Sep 11 · 12:05 AM", StopHoursDisplay.Timestamp(new(2026, 9, 11, 0, 5, 0, TimeSpan.FromHours(-4))));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("2026-09-11T00:05:00+14:00", "Sep 11")]
    [InlineData("2026-09-10T23:55:00-07:00", "Sep 10")]
    public void RecapDateUsesTheProvidedHomeOffsetWithoutShowingTime(string at, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(expected, StopHoursDisplay.RecapDate(DateTimeOffset.Parse(at, CultureInfo.InvariantCulture)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(20, -100, 0, false, false)]
    [InlineData(-1, -121, 0, false, true)]
    [InlineData(20, 10, 5, false, true)]
    [InlineData(20, 10, 0, true, true)]
    public void ArrivalFeasibilityIncludesEarlierDrivingButNotDestinationServiceDebt(
        int arrival, int departure, int shortfall, bool earlierShortage, bool expected)
    {
        var hours = new StopHoursForecast(arrival, departure, shortfall,
            earlierShortage ? DateTimeOffset.UnixEpoch : null, true, [], null);
        Assert.Equal(expected, StopHoursDisplay.CycleShort(hours));
        Assert.False(StopHoursDisplay.CycleShort(hours with { CycleVerified = false }));
    }
}
