using System.Security.Claims;
using Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Infrastructure.Identity;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
  public bool IsAuthenticated =>
    accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
  public string? IdentityUserId =>
    IsAuthenticated
      ? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
      : null;
}
