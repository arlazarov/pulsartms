using System.Net;
using System.Net.Http.Json;
using Client.Services;
using Client.Tests.Support;

namespace Client.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public sealed class ApiResponseStreamingTests
{
  [Theory]
  [InlineData("GET")]
  [InlineData("POST")]
  [InlineData("PUT")]
  [InlineData("PATCH")]
  [InlineData("DELETE")]
  public async Task ResponsesStreamWithoutAnIntermediateBodyBuffer(
    string method
  )
  {
    var content = new StreamingContent();
    using var client = new HttpClient(
      new StubHttpMessageHandler(
        async (request, cancellationToken) =>
        {
          Assert.Equal(method, request.Method.Method);
          if (method is "POST" or "PUT" or "PATCH")
            Assert.Equal(
              "saved",
              await request.Content!.ReadFromJsonAsync<string>(
                cancellationToken
              )
            );
          else
            Assert.Null(request.Content);
          return new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = content,
          };
        }
      )
    )
    {
      BaseAddress = new("http://localhost/"),
    };
    var api = new ApiService(client);
    var result = await (
      method switch
      {
        "GET" => api.GetAsync<string>("api/result"),
        "POST" => api.PostAsync<string, string>("api/result", "saved"),
        "PUT" => api.PutAsync<string, string>("api/result", "saved"),
        "PATCH" => api.PatchAsync<string, string>("api/result", "saved"),
        "DELETE" => api.DeleteAsync<string>("api/result"),
        _ => throw new InvalidOperationException(),
      }
    );

    Assert.True(result.Success);
    Assert.Equal("saved", result.Response);
    Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
    Assert.Equal(0, content.BufferedReads);
    Assert.Equal(1, content.StreamReads);
    Assert.True(content.Disposed);
  }

  private sealed class StreamingContent : HttpContent
  {
    private static readonly byte[] Body =
      "{\"success\":true,\"response\":\"saved\"}"u8.ToArray();
    public int BufferedReads { get; private set; }
    public int StreamReads { get; private set; }
    public bool Disposed { get; private set; }

    protected override Task SerializeToStreamAsync(
      Stream stream,
      TransportContext? context
    )
    {
      BufferedReads++;
      return stream.WriteAsync(Body).AsTask();
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
      StreamReads++;
      return Task.FromResult<Stream>(new MemoryStream(Body, writable: false));
    }

    protected override Task<Stream> CreateContentReadStreamAsync(
      CancellationToken cancellationToken
    ) => CreateContentReadStreamAsync();

    protected override bool TryComputeLength(out long length)
    {
      length = Body.Length;
      return true;
    }

    protected override void Dispose(bool disposing)
    {
      Disposed = true;
      base.Dispose(disposing);
    }
  }
}
