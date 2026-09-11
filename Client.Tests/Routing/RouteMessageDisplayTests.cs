using Client.Shared;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RouteMessageDisplayTests
{
    [Theory]
    [InlineData("Route update queued.")]
    [InlineData("Route update pending.")]
    [InlineData("Route service is temporarily unavailable. Retrying automatically.")]
    [InlineData("Route is temporarily unavailable. Retrying automatically.")]
    [InlineData("Route could not be displayed. Retrying automatically.")]
    [InlineData("Automatic route update paused by the truck request budget. The saved route is retained.")]
    public void ExactMaintenanceMessagesAreQuietOnlyWithAValidVisibleRoute(string message)
    {
        Assert.Null(RouteMessageDisplay.For(message, true));
        Assert.Equal(message, RouteMessageDisplay.For(message, false));
    }

    [Theory]
    [InlineData("Delivery address needs verified coordinates.")]
    [InlineData("This load has multiple truck assignments.")]
    [InlineData("Access denied.")]
    [InlineData("No safe fuel plan found.")]
    [InlineData("Route update requires a delivery address.")]
    public void ActionableWarningsAreNeverSuppressed(string message) =>
        Assert.Equal(message, RouteMessageDisplay.For(message, true));

    [Theory]
    [InlineData("The address correction needs confirmation of the street and building number; no city-center fallback was used.")]
    [InlineData("The stop needs an unambiguous street-level address; no city-center fallback was used.")]
    public void AddressFailuresRemainActionableWithoutTechnicalFallbackExplanations(string message)
    {
        Assert.Equal("Check the stop address.", RouteMessageDisplay.For(message, true));
        Assert.Equal("Check the stop address.", RouteMessageDisplay.For(message, false));
        Assert.Equal("Check the stop address.", RouteMessageDisplay.Concise(message));
    }

    [Theory]
    [InlineData("Access denied.")]
    [InlineData("Fuel stop 1 exceeds the tank capacity.")]
    [InlineData("The route has changed. Reopen the editor.")]
    [InlineData("Check the street and building number.")]
    public void UnrecognizedFailuresKeepTheirOriginalMessage(string message) =>
        Assert.Equal(message, RouteMessageDisplay.Concise(message));
}
