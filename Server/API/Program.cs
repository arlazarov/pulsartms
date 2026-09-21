using API;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.UseContainerLogging();
builder.AddApplicationServices();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddRequestLimits();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
  app.MapOpenApi().AllowAnonymous();
  app.MapScalarApiReference().AllowAnonymous();
}

app.UseAuthorization();

// After authorization, so a signed-in person is counted as a person
// rather than as whatever address they happen to be asking from.
app.UseRateLimiter();
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
