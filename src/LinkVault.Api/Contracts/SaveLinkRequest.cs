using System.Text.Json.Serialization;

namespace LinkVault.Api.Contracts;

public sealed class SaveLinkRequest
{
    public string? Url { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string[]? Tags { get; init; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalFields { get; init; }

    public static bool TryNormalize(
        SaveLinkRequest request,
        out NormalizedSaveLinkRequest normalized,
        out string validationMessage)
    {
        var trimmedUrl = request.Url?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedUrl) ||
            !Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            normalized = default;
            validationMessage = "Only absolute http and https URLs are supported.";
            return false;
        }

        normalized = new NormalizedSaveLinkRequest(
            trimmedUrl,
            NormalizeOptionalText(request.Title),
            NormalizeOptionalText(request.Description),
            NormalizeTags(request.Tags));

        validationMessage = string.Empty;
        return true;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string[] NormalizeTags(string[]? tags)
    {
        if (tags is null || tags.Length == 0)
        {
            return [];
        }

        return tags
            .Select(tag => tag?.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .ToArray();
    }
}

public readonly record struct NormalizedSaveLinkRequest(
    string Url,
    string? Title,
    string? Description,
    string[] Tags);