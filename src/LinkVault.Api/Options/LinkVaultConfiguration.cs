namespace LinkVault.Api.Options;

public static class LinkVaultConfiguration
{
    private const string SectionName = "LinkVault";
    private const string DataPathKey = "DataPath";
    private const string AllowedExtensionOriginsKey = "AllowedExtensionOrigins";
    private const string DataPathEnvironmentVariable = "LINK_VAULT_DATA_PATH";
    private const string AllowedExtensionOriginsEnvironmentVariable = "LINK_VAULT_ALLOWED_EXTENSION_ORIGINS";

    private static readonly string[] DefaultListenUrls =
    [
        "http://localhost:5678",
        "http://127.0.0.1:5678",
        "http://[::1]:5678"
    ];

    public static string[] GetValidatedListenUrls(IConfiguration configuration)
    {
        var rawUrls = configuration[WebHostDefaults.ServerUrlsKey] ?? configuration["ASPNETCORE_URLS"];
        if (string.IsNullOrWhiteSpace(rawUrls))
        {
            return DefaultListenUrls;
        }

        var urls = rawUrls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (urls.Length == 0)
        {
            return DefaultListenUrls;
        }

        foreach (var url in urls)
        {
            EnsureLoopbackOnly(url);
        }

        return urls;
    }

    public static string GetDataPath(IConfiguration configuration)
    {
        var configuredPath = configuration.GetSection(SectionName)[DataPathKey]
            ?? Environment.GetEnvironmentVariable(DataPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "LinkVault", "links.json");
    }

    public static string[] GetAllowedExtensionOrigins(IConfiguration configuration)
    {
        var rawOrigins = configuration.GetSection(SectionName)[AllowedExtensionOriginsKey]
            ?? Environment.GetEnvironmentVariable(AllowedExtensionOriginsEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(rawOrigins))
        {
            throw new InvalidOperationException(
                $"Configure {SectionName}:{AllowedExtensionOriginsKey} or set {AllowedExtensionOriginsEnvironmentVariable} to one or more chrome-extension:// origins.");
        }

        var origins = rawOrigins
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (origins.Length == 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{AllowedExtensionOriginsKey} must include at least one explicit chrome-extension:// origin.");
        }

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, "chrome-extension", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                uri.AbsolutePath != "/")
            {
                throw new InvalidOperationException(
                    $"Invalid extension origin '{origin}'. Use chrome-extension://<EXTENSION_ID>.");
            }
        }

        return origins;
    }

    private static void EnsureLoopbackOnly(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid listen URL '{url}'.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || uri.Port != 5678)
        {
            throw new InvalidOperationException(
                $"Link Vault v1 only supports loopback HTTP on port 5678. Invalid URL: '{url}'.");
        }

        var host = uri.Host;
        var isLoopback = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (System.Net.IPAddress.TryParse(host, out var ipAddress) && System.Net.IPAddress.IsLoopback(ipAddress));

        if (!isLoopback)
        {
            throw new InvalidOperationException(
                $"Link Vault v1 must bind only to loopback hosts. Invalid URL: '{url}'.");
        }

        if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException($"Listen URL '{url}' must not include a path.");
        }
    }
}