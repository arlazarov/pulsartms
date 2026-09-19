using Client;
using Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();

builder.Services.AddScoped<TokenStorageService>();
builder.Services.AddScoped<AuthHeaderHandler>();

builder.Services.AddScoped<AppAuthenticationStateProvider>();

builder.Services.AddScoped<ApiService>();
builder.Services.AddScoped<PlanningDisplayCache>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
  sp.GetRequiredService<AppAuthenticationStateProvider>()
);

builder.Services.AddScoped(sp =>
{
  var handler = sp.GetRequiredService<AuthHeaderHandler>();

  handler.InnerHandler = new HttpClientHandler();

  var baseAddress = builder.HostEnvironment.IsDevelopment()
    ? "http://localhost:5086/"
    : builder.HostEnvironment.BaseAddress;

  var client = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
  // Tells the server this client reads route geometry as an encoded string.
  // A server that does not know the header ignores it and sends points.
  client.DefaultRequestHeaders.Add("X-Route-Geometry", "encoded");
  return client;
});

builder.Services.AddScoped<AuthService>();

await builder.Build().RunAsync();
