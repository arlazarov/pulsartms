using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Client.Services;
using Client.Tests.Support;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class ApiResponseStatusTests
{
  [Theory]
  [InlineData(HttpStatusCode.Unauthorized)]
  [InlineData(HttpStatusCode.Forbidden)]
  [InlineData(HttpStatusCode.ServiceUnavailable)]
  public async Task FailedReadExposesAuthoritativeHttpStatusWithoutAddingItToThePayload(
    HttpStatusCode status
  )
  {
    using var client = new HttpClient(
      new StubHttpMessageHandler(
        (_, _) => Task.FromResult(new HttpResponseMessage(status))
      )
    )
    {
      BaseAddress = new("http://localhost/"),
    };
    var result = await new ApiService(client).GetAsync<object>(
      "api/dispatch/board"
    );
    Assert.False(result.Success);
    Assert.Equal(status, result.HttpStatusCode);
    Assert.DoesNotContain("HttpStatusCode", JsonSerializer.Serialize(result));
  }

  [Fact]
  public async Task PayloadCannotOverrideTheActualHttpStatus()
  {
    using var client = new HttpClient(
      new StubHttpMessageHandler(
        (_, _) =>
          Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
              Content = JsonContent.Create(
                new
                {
                  success = true,
                  response = new { },
                  httpStatusCode = 403,
                }
              ),
            }
          )
      )
    )
    {
      BaseAddress = new("http://localhost/"),
    };
    var result = await new ApiService(client).GetAsync<object>(
      "api/dispatch/board"
    );
    Assert.True(result.Success);
    Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
  }

  // A crashed server answers with the name of the class that threw. The
  // dispatcher pressing "Calculate automatically" on a truck parked at its
  // delivery was shown "System.ArgumentException" where a sentence belongs.
  [Theory]
  [InlineData("System.ArgumentException", false)]
  [InlineData("Npgsql.PostgresException", false)]
  [InlineData("The fuel plan changed in another session.", true)]
  [InlineData("Validation failed", true)]
  public async Task AnExceptionTypeIsNeverShownAsAMessage(
    string title,
    bool kept
  )
  {
    using var client = new HttpClient(
      new StubHttpMessageHandler(
        (_, _) =>
          Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
              Content = JsonContent.Create(new { title, status = 500 }),
            }
          )
      )
    )
    {
      BaseAddress = new("http://localhost/"),
    };
    var result = await new ApiService(client).GetAsync<object>("api/anything");
    Assert.False(result.Success);
    var message = Assert.Single(result.Errors!);
    Assert.Equal(
      kept ? title : "The request failed. Please try again.",
      message
    );
  }
}
