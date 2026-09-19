using Application.Caching;
using Application.Features.Routing.Background;
using Application.Features.Routing.Options;
using Application.Features.Synchronization.Options;
using Application.Features.Synchronization.Services;
using Microsoft.Extensions.Options;

namespace Server.Tests.Support;

internal static class TestCache
{
  internal static ReadCache Create() =>
    new(Options.Create(new SynchronizationOptions()));

  internal static RoutePreparationQueue Preparation() =>
    new(Options.Create(new RoutePreparationOptions()), TimeProvider.System);
}
