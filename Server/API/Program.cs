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

// Liveness ran no checks at all and answered healthy for a process that had
// stopped working. It now fails when a background loop this instance was
// told to run has gone quiet, so the platform replaces the container instead
// of the fault being noticed hours later through a missing fuel stop.
app.MapHealthChecks(
    "/api/health/live",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") }
  )
  .AllowAnonymous();
app.MapHealthChecks(
    "/api/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }
  )
  .RequireAuthorization("Admin");

app.Run();
