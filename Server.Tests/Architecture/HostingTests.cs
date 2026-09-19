using API;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Server.Tests.Support;

namespace Server.Tests.Architecture;

[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class HostingTests
{
  [Theory]
  [InlineData("br, gzip", "br")]
  [InlineData("gzip", "gzip")]
  public void ApiCompressesJsonResponsesForBrowsersThatAcceptIt(string acceptEncoding, string expected)
  {
    var builder = WebApplication.CreateBuilder();
    builder.AddApplicationServices();
    using var provider = builder.Services.BuildServiceProvider();
    var compression = provider.GetRequiredService<IResponseCompressionProvider>();
    var context = new DefaultHttpContext();
    context.Request.Headers.AcceptEncoding = acceptEncoding;
    context.Response.ContentType = "application/json; charset=utf-8";
    Assert.True(compression.CheckRequestAcceptsCompression(context));
    Assert.True(compression.ShouldCompressResponse(context));
    Assert.Equal(expected, compression.GetCompressionProvider(context)?.EncodingName);
  }

  [Fact]
  public void CompressionRunsBeforeEndpointsAndTheRuntimeStaysContainerSized()
  {
    var program = File.ReadAllText(Path.Combine(RepositoryFiles.Root(), "Server/API/Program.cs"));
    var compression = program.IndexOf("app.UseResponseCompression();", StringComparison.Ordinal);
    Assert.True(compression >= 0);
    Assert.True(compression < program.IndexOf("app.MapControllers();", StringComparison.Ordinal));
    var project = File.ReadAllText(Path.Combine(RepositoryFiles.Root(), "Server/API/API.csproj"));
    Assert.Contains("<InvariantGlobalization>true</InvariantGlobalization>", project);
    Assert.Contains("<ServerGarbageCollection>false</ServerGarbageCollection>", project);
    Assert.Contains("<PublishReadyToRun Condition=\"'$(RuntimeIdentifier)' != ''\">true</PublishReadyToRun>", project);
    var dockerfile = File.ReadAllText(Path.Combine(RepositoryFiles.Root(), "Server/API/Dockerfile"));
    Assert.Contains("dotnet restore \"Server/API/API.csproj\" -r linux-x64", dockerfile);
    Assert.Contains("-r linux-x64", dockerfile.Split("dotnet publish", 2)[1]);
    Assert.Contains("--self-contained false", dockerfile);
  }
}
