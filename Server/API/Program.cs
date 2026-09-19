using API;
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

app.UseResponseCompression();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/api/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
  { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/api/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
  { Predicate = check => check.Tags.Contains("ready") }).RequireAuthorization("Admin");

app.Run();
