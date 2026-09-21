using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using API;
using API.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class RequestLimitTests
{
  [Fact]
  public async Task OnePersonRunningAwayCannotSpendAnotherPersonsAllowance()
  {
    var limiter = Options().GlobalLimiter!;
    var first = Asking("11111111-1111-1111-1111-111111111111");
    var second = Asking("22222222-2222-2222-2222-222222222222");

    var taken = 0;
    for (var i = 0; i < 601; i++)
      if ((await limiter.AcquireAsync(first)).IsAcquired)
        taken++;

    Assert.Equal(600, taken);
    Assert.False((await limiter.AcquireAsync(first)).IsAcquired);
    // The second dispatcher has not asked for anything and is unaffected.
    Assert.True((await limiter.AcquireAsync(second)).IsAcquired);
  }

  [Fact]
  public async Task SomeoneWhoHasNotSignedInIsCountedByAddress()
  {
    var limiter = Options().GlobalLimiter!;
    var one = Asking(null, "203.0.113.7");
    var same = Asking(null, "203.0.113.7");
    var elsewhere = Asking(null, "198.51.100.4");

    for (var i = 0; i < 600; i++)
      await limiter.AcquireAsync(one);

    Assert.False((await limiter.AcquireAsync(same)).IsAcquired);
    Assert.True((await limiter.AcquireAsync(elsewhere)).IsAcquired);
  }

  [Fact]
  public async Task ARefusalReadsAsASentenceRatherThanABare429()
  {
    var options = Options();
    var context = Asking("33333333-3333-3333-3333-333333333333");
    var body = new MemoryStream();
    context.Response.Body = body;
    var lease = await options.GlobalLimiter!.AcquireAsync(context);

    await options.OnRejected!(
      new() { HttpContext = context, Lease = lease },
      default
    );

    Assert.Equal(429, context.Response.StatusCode);
    body.Position = 0;
    var answer = JsonDocument.Parse(body).RootElement;
    Assert.False(answer.GetProperty("success").GetBoolean());
    Assert.Equal(
      "Too many requests just now. Wait a moment and try again.",
      answer.GetProperty("errors")[0].GetString()
    );
  }

  [Theory]
  [InlineData(typeof(RoutePlanningController), null, RequestLimits.Planning)]
  [InlineData(typeof(AuthController), "Login", RequestLimits.SignIn)]
  [InlineData(typeof(AuthController), "Refresh", RequestLimits.SignIn)]
  public void TheEndpointsThatCostMoneyOrGetGuessedAtSayWhichLimitTheyUse(
    Type controller,
    string? action,
    string policy
  )
  {
    var target = action is null
      ? controller
      : (MemberInfo)controller.GetMethod(action)!;

    Assert.Equal(
      policy,
      target.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName
    );
  }

  private static RateLimiterOptions Options()
  {
    var services = new ServiceCollection();
    services.AddRequestLimits();
    return services
      .BuildServiceProvider()
      .GetRequiredService<IOptions<RateLimiterOptions>>()
      .Value;
  }

  private static DefaultHttpContext Asking(
    string? user,
    string address = "203.0.113.1"
  )
  {
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(address);
    if (user is not null)
      context.User = new(
        new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)])
      );
    return context;
  }
}
