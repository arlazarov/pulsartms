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
  public async Task FailedReadExposesAuthoritativeHttpStatusWithoutAddingItToThePayload(HttpStatusCode status)
  {
    using var client = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(status))))
      { BaseAddress = new("http://localhost/") };
    var result = await new ApiService(client).GetAsync<object>("api/dispatch/board");
    Assert.False(result.Success);
    Assert.Equal(status, result.HttpStatusCode);
    Assert.DoesNotContain("HttpStatusCode", JsonSerializer.Serialize(result));
  }

  [Fact]
  public async Task PayloadCannotOverrideTheActualHttpStatus()
  {
    using var client = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      { Content = JsonContent.Create(new { success = true, response = new { }, httpStatusCode = 403 }) })))
      { BaseAddress = new("http://localhost/") };
    var result = await new ApiService(client).GetAsync<object>("api/dispatch/board");
    Assert.True(result.Success);
    Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
  }
}
