namespace BillingPlatform.Identity.Application;

public sealed record RegisterIdentityCommand(string Email, string Password);

public sealed record LoginIdentityCommand(string Email, string Password);

public sealed record IdentityUserInfo(Guid Id, string Email);

public sealed record RegistrationResult(
    IdentityUserInfo? User,
    IReadOnlyCollection<string> Errors)
{
    public bool Succeeded => User is not null;
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface IIdentityService
{
    Task<RegistrationResult> RegisterAsync(
        RegisterIdentityCommand command,
        CancellationToken cancellationToken = default);

    Task<IdentityUserInfo?> AuthenticateAsync(
        LoginIdentityCommand command,
        CancellationToken cancellationToken = default);
}

public interface ITokenService
{
    AccessToken Create(IdentityUserInfo user, Guid? organizationId);
}
