using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Infrastructure.Integrations.Samsara;
using Infrastructure.Integrations.Torque;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Unit")]
public class ProviderCredentialTests
{
  [Theory]
  [InlineData(IntegrationProviderCatalog.Samsara)]
  [InlineData(IntegrationProviderCatalog.Torque)]
  public async Task RotationUsesLatestSnapshotWithoutChangingSharedHeaders(
    string provider
  )
  {
    var credentials = new StubProviderCredentials(("apiKey", "first-key"));
    var received = new List<string?>();
    using var client = new HttpClient(
      new Handler(
        (request, _) =>
        {
          received.Add(request.Headers.Authorization?.Parameter);
          return Task.FromResult(Response());
        }
      )
    );
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
      "Bearer",
      "unchanged-default"
    );
    var call = CreateCall(provider, client, credentials);

    await call(CancellationToken.None);
    credentials.Values = Values("second-key");
    await call(CancellationToken.None);

    Assert.Equal(["first-key", "second-key"], received);
    Assert.Equal(
      "unchanged-default",
      client.DefaultRequestHeaders.Authorization.Parameter
    );
    Assert.Equal([provider, provider], credentials.Requests);
  }

  [Theory]
  [InlineData(IntegrationProviderCatalog.Samsara)]
  [InlineData(IntegrationProviderCatalog.Torque)]
  public async Task ConcurrentRequestsKeepTheirOwnAuthorization(string provider)
  {
    var credentials = new StubProviderCredentials(("apiKey", "first-key"));
    var requests = Channel.CreateUnbounded<HttpRequestMessage>();
    var release = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var client = new HttpClient(
      new Handler(
        async (request, ct) =>
        {
          await requests.Writer.WriteAsync(request, ct);
          await release.Task.WaitAsync(ct);
          return Response();
        }
      )
    );
    var call = CreateCall(provider, client, credentials);
    var first = call(CancellationToken.None);
    var firstRequest = await requests
      .Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    credentials.Values = Values("second-key");
    var second = call(CancellationToken.None);
    var secondRequest = await requests
      .Reader.ReadAsync()
      .AsTask()
      .WaitAsync(TimeSpan.FromSeconds(5));
    try
    {
      Assert.NotSame(firstRequest, secondRequest);
      Assert.Equal("first-key", firstRequest.Headers.Authorization?.Parameter);
      Assert.Equal(
        "second-key",
        secondRequest.Headers.Authorization?.Parameter
      );
      Assert.Null(client.DefaultRequestHeaders.Authorization);
    }
    finally
    {
      release.TrySetResult();
      await Task.WhenAll(first, second);
    }
  }

  [Theory]
  [InlineData(IntegrationProviderCatalog.Samsara)]
  [InlineData(IntegrationProviderCatalog.Torque)]
  public async Task ResolverFailureNeverSendsAnOutboundRequest(string provider)
  {
    var requests = 0;
    using var client = new HttpClient(
      new Handler(
        (_, _) =>
        {
          requests++;
          return Task.FromResult(Response());
        }
      )
    );
    var expected = new InvalidOperationException(
      "Credential resolution unavailable."
    );
    using var cancellation = new CancellationTokenSource();
    var resolver = new DelegateCredentials(
      (id, ct) =>
      {
        Assert.Equal(provider, id);
        Assert.Equal(cancellation.Token, ct);
        return Task.FromException<IntegrationCredentialValues>(expected);
      }
    );

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
      () => CreateCall(provider, client, resolver)(cancellation.Token)
    );

    Assert.Same(expected, error);
    Assert.Equal(0, requests);
    Assert.Null(client.DefaultRequestHeaders.Authorization);
  }

  [Theory]
  [InlineData(IntegrationProviderCatalog.Samsara)]
  [InlineData(IntegrationProviderCatalog.Torque)]
  public async Task MissingTokenFailsBeforeSending(string provider)
  {
    var requests = 0;
    using var client = new HttpClient(
      new Handler(
        (_, _) =>
        {
          requests++;
          return Task.FromResult(Response());
        }
      )
    );
    var call = CreateCall(provider, client, new StubProviderCredentials());
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => call(CancellationToken.None)
    );
    Assert.Equal(0, requests);
  }

  [Fact]
  public async Task SamsaraCameraPostResolvesFreshCredentials()
  {
    var credentials = new StubProviderCredentials(("apiKey", "camera-key"));
    using var client = new HttpClient(
      new Handler(
        (request, _) =>
        {
          Assert.Equal(HttpMethod.Post, request.Method);
          Assert.Equal("camera-key", request.Headers.Authorization?.Parameter);
          return Task.FromResult(Response());
        }
      )
    );
    var service = new SamsaraApiService(client, credentials);
    await service.RequestCameraImageAsync(
      "vehicle",
      DateTimeOffset.UnixEpoch,
      CancellationToken.None
    );
    Assert.Equal(
      IntegrationProviderCatalog.Samsara,
      Assert.Single(credentials.Requests)
    );
    Assert.Null(client.DefaultRequestHeaders.Authorization);
  }

  [Theory]
  [InlineData("https://untrusted.example/fleet/drivers")]
  [InlineData("http://api.samsara.com/fleet/drivers")]
  [InlineData("https://api.samsara.com:444/fleet/drivers")]
  [InlineData("https://user@api.samsara.com/fleet/drivers")]
  [InlineData("//untrusted.example/fleet/drivers")]
  public async Task SamsaraRejectsOtherOriginsBeforeResolvingCredentials(
    string url
  )
  {
    var requests = 0;
    var resolver = new StubProviderCredentials(("apiKey", "never-send"));
    using var client = new HttpClient(
      new Handler(
        (_, _) =>
        {
          requests++;
          return Task.FromResult(Response());
        }
      )
    );
    var service = new SamsaraApiService(client, resolver);

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
      () => service.GetAsync<object>(url)
    );

    Assert.Empty(resolver.Requests);
    Assert.Equal(0, requests);
    Assert.DoesNotContain("never-send", error.ToString());
    Assert.DoesNotContain(url, error.Message);
  }

  [Fact]
  public async Task TorquePaginationResolvesCredentialsForEachPage()
  {
    var credentials = new StubProviderCredentials(("apiKey", "first-page"));
    var received = new List<string?>();
    using var client = new HttpClient(
      new Handler(
        (request, _) =>
        {
          received.Add(request.Headers.Authorization?.Parameter);
          credentials.Values = Values("second-page");
          return Task.FromResult(
            Response(
              """{"data":[{"loadNumber":1358}],"totalCount":2,"itemsPerPage":1}"""
            )
          );
        }
      )
    );
    await CreateCall(IntegrationProviderCatalog.Torque, client, credentials)(
      CancellationToken.None
    );
    Assert.Equal(["first-page", "second-page"], received);
    Assert.Equal(2, credentials.Requests.Count);
  }

  private static Func<CancellationToken, Task> CreateCall(
    string provider,
    HttpClient client,
    IIntegrationCredentials credentials
  )
  {
    if (provider == IntegrationProviderCatalog.Samsara)
    {
      var service = new SamsaraApiService(client, credentials);
      return async ct =>
      {
        await service.GetDriversAsync(ct);
      };
    }
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["TorqueAI:BaseUrl"] = "https://example.test",
        }
      )
      .Build();
    var torque = new TorqueApiService(client, configuration, credentials);
    return async ct =>
    {
      await torque.GetDispatchesAsync(new(2026, 9, 1), new(2026, 9, 1), ct);
    };
  }

  private static IntegrationCredentialValues Values(string key) =>
    new([KeyValuePair.Create("apiKey", key)]);

  private static HttpResponseMessage Response(
    string body = """{"data":[],"totalCount":0,"itemsPerPage":500}"""
  ) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

  private sealed class Handler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
  ) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken
    ) => send(request, cancellationToken);
  }

  private sealed class DelegateCredentials(
    Func<string, CancellationToken, Task<IntegrationCredentialValues>> get
  ) : IIntegrationCredentials
  {
    public Task<IntegrationCredentialValues> GetAsync(
      string provider,
      CancellationToken ct
    ) => get(provider, ct);
  }
}
