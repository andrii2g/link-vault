namespace LinkVault.Api.Contracts;

public sealed class TouchLinkRequest
{
    public string? Url { get; init; }

    public static bool TryNormalize(TouchLinkRequest request, out string normalizedUrl)
    {
        var trimmedUrl = request.Url?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedUrl) ||
            !Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            normalizedUrl = string.Empty;
            return false;
        }

        normalizedUrl = trimmedUrl;
        return true;
    }
}