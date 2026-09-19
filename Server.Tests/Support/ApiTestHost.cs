using System.Security.Claims;
using System.Text.Encodings.Web;
using API;
using API.Controllers;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Server.Tests.Support;

// Hosts the real MVC pipeline (routing, JSON, ETag handling, compression) over a scripted
// mediator: no database, providers, identity or background workers.
public sealed class ApiTestHost : IAsyncDisposable
{
  private readonly WebApplication app;
  public ScriptedMediator Mediator { get; }
  public HttpClient Client { get; }

  private ApiTestHost(WebApplication app, ScriptedMediator mediator)
  {
    this.app = app;
    Mediator = mediator;
    Client = app.GetTestClient();
  }

  public static async Task<ApiTestHost> StartAsync()
  {
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
    builder.WebHost.UseTestServer();
    builder.Logging.ClearProviders();
    builder.Services.AddApiHttp();
    // The test assembly is not a web SDK project, so the API assembly is not discovered as a part.
    builder.Services.AddControllers().AddApplicationPart(typeof(BaseController).Assembly);
    builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", null);
    builder.Services.AddAuthorization();
    var mediator = new ScriptedMediator();
    builder.Services.AddSingleton<IMediator>(mediator);
    var app = builder.Build();
    app.UseResponseCompression();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    await app.StartAsync();
    return new ApiTestHost(app, mediator);
  }

  public async ValueTask DisposeAsync()
  {
    Client.Dispose();
    await app.StopAsync();
    await app.DisposeAsync();
  }

  public sealed class ScriptedMediator : IMediator
  {
    public Func<object, object?> Script { get; set; } = _ => throw new InvalidOperationException("No scripted response.");
    public List<object> Requests { get; } = [];

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
      Requests.Add(request);
      return Task.FromResult((TResponse)Script(request)!);
    }
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
    public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task Publish(object notification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => throw new NotSupportedException();
  }

  private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
  {
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
      var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Role, "Admin")], "Test");
      return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
    }
  }
}
