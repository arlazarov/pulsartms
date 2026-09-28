using Application.Features.Messaging.Services;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Support;

// A send hold over a previous binary the test controls; Open is one with
// no previous binary running.
internal static class TestSendHold
{
  public static SendHold Open() => With(new Previous(false)).Hold;

  public static (SendHold Hold, Previous Previous) With(
    Previous previous,
    TimeProvider? clock = null
  ) =>
    (
      new(
        new ServiceCollection()
          .AddSingleton<IPreviousBinary>(previous)
          .BuildServiceProvider()
          .GetRequiredService<IServiceScopeFactory>(),
        clock ?? TimeProvider.System
      ),
      previous
    );

  public sealed class Previous(bool runs) : IPreviousBinary
  {
    public bool Runs { get; set; } = runs;
    public int Asked { get; private set; }

    public Task<bool> RunsAsync(CancellationToken ct)
    {
      Asked++;
      return Task.FromResult(Runs);
    }
  }
}
