using Application.Interfaces;
using Application.Models;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity;

public class IdentityService(UserManager<AppUser> userManager, IReadCache? reads = null) : IIdentityService
{
  public async Task<IdentityServiceResult> CreateUserAsync(
    string email,
    string password,
    CancellationToken cancellationToken = default
  )
  {
    var user = new AppUser { UserName = email, Email = email };

    var result = await userManager.CreateAsync(user, password);

    if (!result.Succeeded)
    {
      return new IdentityServiceResult(false, null, [.. result.Errors.Select(x => x.Description)]);
    }

    reads?.Invalidate($"session:{user.Id}");
    return new IdentityServiceResult(true, user.Id);
  }

  public async Task<IdentityServiceResult> UpdateEmailAsync(
    string userId,
    string email,
    CancellationToken cancellationToken = default
  )
  {
    var user = await userManager.FindByIdAsync(userId);

    if (user is null)
    {
      return new IdentityServiceResult(
        false,
        null,
        new ValidationErrors("Identity user not found.")
      );
    }

    var emailResult = await userManager.SetEmailAsync(user, email);

    if (!emailResult.Succeeded)
    {
      return new IdentityServiceResult(
        false,
        null,
        [.. emailResult.Errors.Select(x => x.Description)]
      );
    }

    var userNameResult = await userManager.SetUserNameAsync(user, email);

    if (!userNameResult.Succeeded)
    {
      return new IdentityServiceResult(
        false,
        null,
        [.. userNameResult.Errors.Select(x => x.Description)]
      );
    }

    reads?.Invalidate($"session:{user.Id}");
    return new IdentityServiceResult(true, user.Id);
  }

  public async Task<IdentityServiceResult> UpdatePasswordAsync(
    string userId,
    string password,
    CancellationToken cancellationToken = default
  )
  {
    var user = await userManager.FindByIdAsync(userId);

    if (user is null)
    {
      return new IdentityServiceResult(
        false,
        null,
        new ValidationErrors("Identity user not found.")
      );
    }

    var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);

    var result = await userManager.ResetPasswordAsync(user, resetToken, password);

    if (!result.Succeeded)
    {
      return new IdentityServiceResult(false, null, [.. result.Errors.Select(x => x.Description)]);
    }

    reads?.Invalidate($"session:{user.Id}");
    return new IdentityServiceResult(true, user.Id);
  }

  public async Task<IdentityServiceResult> SetActiveAsync(
    string userId,
    bool isActive,
    CancellationToken cancellationToken = default
  )
  {
    var user = await userManager.FindByIdAsync(userId);

    if (user is null)
    {
      return new IdentityServiceResult(
        false,
        null,
        new ValidationErrors("Identity user not found.")
      );
    }

    var enableResult = await userManager.SetLockoutEnabledAsync(user, true);
    if (!enableResult.Succeeded)
      return new IdentityServiceResult(false, null, [.. enableResult.Errors.Select(x => x.Description)]);

    DateTimeOffset? lockoutEnd = isActive ? null : DateTimeOffset.MaxValue;

    var result = await userManager.SetLockoutEndDateAsync(user, lockoutEnd);

    if (!result.Succeeded)
    {
      return new IdentityServiceResult(false, null, [.. result.Errors.Select(x => x.Description)]);
    }

    var stampResult = await userManager.UpdateSecurityStampAsync(user);
    if (stampResult.Succeeded) reads?.Invalidate($"session:{user.Id}");
    return stampResult.Succeeded
      ? new IdentityServiceResult(true, user.Id)
      : new IdentityServiceResult(false, null, [.. stampResult.Errors.Select(x => x.Description)]);
  }

  public async Task<IdentityServiceResult> DeleteUserAsync(
    string userId,
    CancellationToken cancellationToken = default
  )
  {
    var user = await userManager.FindByIdAsync(userId);

    if (user is null)
    {
      return new IdentityServiceResult(
        false,
        null,
        new ValidationErrors("Identity user not found.")
      );
    }

    var result = await userManager.DeleteAsync(user);

    if (!result.Succeeded)
    {
      return new IdentityServiceResult(false, null, [.. result.Errors.Select(x => x.Description)]);
    }

    reads?.Invalidate($"session:{user.Id}");
    return new IdentityServiceResult(true, user.Id);
  }
}
