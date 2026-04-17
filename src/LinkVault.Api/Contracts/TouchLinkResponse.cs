namespace LinkVault.Api.Contracts;

public sealed record TouchLinkResponse(string Status)
{
    public static TouchLinkResponse Updated() => new("updated");

    public static TouchLinkResponse Ignored() => new("ignored");

    public static TouchLinkResponse Invalid() => new("invalid");
}