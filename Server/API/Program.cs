using API;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddApplicationServices();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
  app.MapOpenApi().AllowAnonymous();
  app.MapScalarApiReference().AllowAnonymous();
}

app.UseAuthorization();
app.UseOperationalCompression();
app.MapControllers();
app.MapHealthChecks(
    "/api/health/live",
    new HealthCheckOptions { Predicate = _ => false }
  )
  .AllowAnonymous();
app.MapHealthChecks(
    "/api/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }
  )
  .RequireAuthorization("Admin");

app.Run();
