namespace LinkVault.Api.Models;

public sealed record UrlItem(
    Guid Id,
    string Url,
    string? Title,
    string? Description,
    string[] Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);