using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Infrastructure.Identity;

public sealed class SessionStartupFilter : IStartupFilter
{
  public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
  {
    // Endpoint metadata and CORS must be available before session rejection.
    app.UseExceptionHandler();
    app.UseHttpsRedirection();
    app.UseRouting();
    app.UseCors("Client");
    app.UseAuthentication();
    app.UseMiddleware<SessionValidationMiddleware>();
    next(app);
  };
}
