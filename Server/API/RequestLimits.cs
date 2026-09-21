using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Application.Models;
using Microsoft.AspNetCore.RateLimiting;

namespace API;

// How often one signed-in person, or one address that has not signed in
// yet, may ask for something.
//
// The numbers are set from what using the product actually costs. A tab
// refreshes hours every fifteen seconds and the map asks for rather more,
// so a dispatcher with several tabs open sits in the low tens per minute;
// the ordinary limit is far above that and is there to stop a loop that
// has run away, not to pace a person. Planning is separate because every
// call behind it can buy a road from a provider. Signing in is separate
// because that is what gets guessed at.
//
// A refused request answers in the same shape as every other refusal, so
// the browser shows the sentence rather than a bare 429.
public static class RequestLimits
{
  public const string Planning = "planning";
  public const string SignIn = "sign-in";

  public static void AddRequestLimits(this IServiceCollection services) =>
    services.AddRateLimiter(limiter =>
    {
      limiter.GlobalLimiter = PartitionedRateLimiter.Create<
        HttpContext,
        string
      >(context =>
        RateLimitPartition.GetTokenBucketLimiter(
          Who(context),
          _ =>
            new()
            {
              TokenLimit = 600,
              TokensPerPeriod = 300,
              ReplenishmentPeriod = TimeSpan.FromMinutes(1),
              QueueLimit = 0,
              AutoReplenishment = true,
            }
        )
      );

      limiter.AddPolicy(
        Planning,
        context =>
          RateLimitPartition.GetTokenBucketLimiter(
            Who(context),
            _ =>
              new()
              {
                TokenLimit = 40,
                TokensPerPeriod = 20,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
              }
          )
      );

      limiter.AddPolicy(
        SignIn,
        context =>
          RateLimitPartition.GetFixedWindowLimiter(
            Address(context),
            _ =>
              new()
              {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
              }
          )
      );

      limiter.OnRejected = async (context, ct) =>
      {
        context.HttpContext.Response.StatusCode = 429;
        if (
          context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after)
        )
          context.HttpContext.Response.Headers.RetryAfter = (
            (int)after.TotalSeconds
          ).ToString(CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(
          RequestResponse<object>.Fail(
            "Too many requests just now. Wait a moment and try again.",
            429
          ),
          ct
        );
      };
    });

  // A signed-in person is limited as a person, wherever they are asking
  // from. Everyone else is limited by address, because that is all there
  // is to go on before signing in.
  private static string Who(HttpContext context) =>
    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
      is { Length: > 0 } user
      ? "user:" + user
      : "address:" + Address(context);

  private static string Address(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
