using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BillingPlatform.Identity.Application;
using Microsoft.IdentityModel.Tokens;

namespace BillingPlatform.Identity.Infrastructure;

internal sealed class JwtTokenService(JwtOptions options, TimeProvider timeProvider) : ITokenService
{
    public AccessToken Create(IdentityUserInfo user, Guid? organizationId)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(options.LifetimeMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (organizationId is not null)
        {
            claims.Add(new Claim("org_id", organizationId.Value.ToString()));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);

        return new AccessToken(handler.WriteToken(token), expiresAt);
    }
}
