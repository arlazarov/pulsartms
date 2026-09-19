using System.IO.Compression;
using System.Security.Claims;
using API;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Integration")]
public sealed class OperationalCompressionTests
{
  [Theory]
  [InlineData("br")]
  [InlineData("gzip")]
  public async Task OnlyAuthenticatedOptedInOperationalResponsesAreCompressed(
    string encoding
  )
  {
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
    builder.Services.AddOperationalCompression();
    await using var app = builder.Build();
    app.UseRouting();
    app.Use(
      async (context, next) =>
      {
        if (context.Request.Path != "/anonymous")
          context.User = new(
            new ClaimsIdentity(
              [new(ClaimTypes.NameIdentifier, "fixture")],
              "fixture"
            )
          );
        await next(context);
      }
    );
    app.UseOperationalCompression();
    var payload = new
    {
      readings = Enumerable.Repeat("operational fixture", 1000).ToArray(),
    };
    app.MapGet("/operational", () => Results.Json(payload))
      .WithMetadata(new CompressResponseAttribute());
    app.MapGet("/anonymous", () => Results.Json(payload))
      .WithMetadata(new CompressResponseAttribute());
    app.MapGet("/credentials", () => Results.Json(payload));
    await app.StartAsync();
    using var client = new HttpClient { BaseAddress = new(app.Urls.Single()) };
    client.DefaultRequestHeaders.AcceptEncoding.ParseAdd(encoding);
    using var response = await client.GetAsync("/operational");
    response.EnsureSuccessStatusCode();
    Assert.Contains(encoding, response.Content.Headers.ContentEncoding);
    Assert.Contains("Accept-Encoding", response.Headers.Vary);
    var compressed = await response.Content.ReadAsByteArrayAsync();
    using var input = new MemoryStream(compressed);
    using Stream decompressed =
      encoding == "br"
        ? new BrotliStream(input, CompressionMode.Decompress)
        : new GZipStream(input, CompressionMode.Decompress);
    using var reader = new StreamReader(decompressed);
    var json = await reader.ReadToEndAsync();
    Assert.Contains("operational fixture", json);
    Assert.True(compressed.Length < json.Length / 4);
    foreach (var path in new[] { "/anonymous", "/credentials" })
    {
      using var uncompressed = await client.GetAsync(path);
      uncompressed.EnsureSuccessStatusCode();
      Assert.Empty(uncompressed.Content.Headers.ContentEncoding);
      Assert.Equal(json, await uncompressed.Content.ReadAsStringAsync());
    }
    await app.StopAsync();
  }
}
