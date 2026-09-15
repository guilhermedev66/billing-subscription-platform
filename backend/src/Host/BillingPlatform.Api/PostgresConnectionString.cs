using System.Data.Common;

namespace BillingPlatform.Api;

internal static class PostgresConnectionString
{
    private const string ConfigurationKey = "ConnectionStrings:BillingPlatform";

    public static void Normalize(ConfigurationManager configuration)
    {
        var configuredValue = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredValue) ||
            !configuredValue.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!Uri.TryCreate(configuredValue, UriKind.Absolute, out var databaseUri) ||
            databaseUri.Scheme is not ("postgres" or "postgresql"))
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} contains an invalid PostgreSQL URL.");
        }

        var userInfoSeparator = databaseUri.UserInfo.IndexOf(':');
        var databaseName = databaseUri.AbsolutePath.Trim('/');
        if (userInfoSeparator <= 0 || string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} must include a username, password, and database name.");
        }

        var connectionString = new DbConnectionStringBuilder
        {
            ["Host"] = databaseUri.Host,
            ["Port"] = databaseUri.Port > 0 ? databaseUri.Port : 5432,
            ["Database"] = Uri.UnescapeDataString(databaseName),
            ["Username"] = Uri.UnescapeDataString(databaseUri.UserInfo[..userInfoSeparator]),
            ["Password"] = Uri.UnescapeDataString(databaseUri.UserInfo[(userInfoSeparator + 1)..])
        };

        configuration[ConfigurationKey] = connectionString.ConnectionString;
    }
}
