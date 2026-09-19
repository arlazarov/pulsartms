using Application;
using Infrastructure;

namespace API;

public static class DependencyInjection
{
  public static WebApplicationBuilder AddApplicationServices(
    this WebApplicationBuilder builder
  )
  {
    builder.Services.AddApplication(
      builder.Configuration["MediatR:LicenseKey"]
    );
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplicationOptions(builder.Configuration);

    builder.Services.AddOpenApi();
    builder.Services.AddControllers();
    builder.Services.AddOperationalCompression();

    builder.Services.AddCors(options =>
    {
      options.AddPolicy(
        "Client",
        policy =>
        {
          policy
            .WithOrigins("http://localhost:5067")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
        }
      );
    });

    return builder;
  }
}
