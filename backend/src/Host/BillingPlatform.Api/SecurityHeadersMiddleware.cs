namespace BillingPlatform.Api;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; frame-ancestors 'none'; object-src 'none'";

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var response = ((HttpContext)state).Response;
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["X-Frame-Options"] = "DENY";
            response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;

            return Task.CompletedTask;
        }, context);

        await next(context);
    }
}
