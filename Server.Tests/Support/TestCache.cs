using Application.Caching;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Microsoft.Extensions.Options;

namespace Server.Tests.Support;

internal static class TestCache
{
  internal static ReadCache Create() => new(Options.Create(new SynchronizationOptions()));
  internal static Application.Features.Routing.Background.RoutePreparationQueue Preparation() =>
    new(Options.Create(new Application.Features.Routing.Options.RoutePreparationOptions()), TimeProvider.System);
}
