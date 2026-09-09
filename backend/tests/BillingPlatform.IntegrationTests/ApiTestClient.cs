using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BillingPlatform.IntegrationTests;

internal static class ApiTestClient
{
    public const string ValidPassword = "ValidPassword!123";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static async Task<AuthSessionResponse> RegisterAsync(
        HttpClient client,
        string organizationName,
        string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            organizationName,
            email,
            password = ValidPassword
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadSessionAsync(response);
    }

    public static async Task<AuthSessionResponse> LoginAsync(
        HttpClient client,
        string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = ValidPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadSessionAsync(response);
    }

    public static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string requestUri,
        string token,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static async Task<AuthSessionResponse> ReadSessionAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var rootProperties = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ToArray();
        var userProperties = document.RootElement
            .GetProperty("user")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        Assert.Equal(["token", "user"], rootProperties);
        Assert.Equal(
            ["email", "id", "organizationId", "organizationName"],
            userProperties);

        var session = JsonSerializer.Deserialize<AuthSessionResponse>(json, JsonOptions);
        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session.Token));
        Assert.NotEqual(Guid.Empty, session.User.Id);
        Assert.NotEqual(Guid.Empty, session.User.OrganizationId);

        return session;
    }
}

internal sealed record AuthSessionResponse(string Token, AuthUserResponse User);

internal sealed record AuthUserResponse(
    Guid Id,
    string Email,
    Guid OrganizationId,
    string OrganizationName);

internal sealed record CurrentUserResponse(Guid UserId, string Email, Guid OrganizationId);

internal sealed record OrganizationResponse(Guid Id, string Name);
