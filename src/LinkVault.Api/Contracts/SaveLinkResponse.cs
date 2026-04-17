namespace LinkVault.Api.Contracts;

public sealed record SaveLinkResponse(string Status, string Message)
{
    public static SaveLinkResponse Saved() => new("saved", "Link saved.");

    public static SaveLinkResponse Duplicate() => new("duplicate", "Link already exists.");

    public static SaveLinkResponse Invalid(string message) => new("invalid", message);
}