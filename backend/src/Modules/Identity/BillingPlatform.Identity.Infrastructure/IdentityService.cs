using BillingPlatform.Identity.Application;
using BillingPlatform.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace BillingPlatform.Identity.Infrastructure;

internal sealed class IdentityService(UserManager<ApplicationUser> userManager) : IIdentityService
{
    public async Task<RegistrationResult> RegisterAsync(
        RegisterIdentityCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var email = command.Email.Trim();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email
        };

        var result = await userManager.CreateAsync(user, command.Password);

        return result.Succeeded
            ? new RegistrationResult(new IdentityUserInfo(user.Id, email), [])
            : new RegistrationResult(null, result.Errors.Select(error => error.Description).ToArray());
    }

    public async Task<IdentityUserInfo?> AuthenticateAsync(
        LoginIdentityCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(command.Email.Trim());
        if (user is null || !await userManager.CheckPasswordAsync(user, command.Password))
        {
            return null;
        }

        return new IdentityUserInfo(user.Id, user.Email!);
    }
}
