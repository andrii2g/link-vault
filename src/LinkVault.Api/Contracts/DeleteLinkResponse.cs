namespace LinkVault.Api.Contracts;

public sealed record DeleteLinkResponse(string Status)
{
    public static DeleteLinkResponse Deleted() => new("deleted");

    public static DeleteLinkResponse Ignored() => new("ignored");

    public static DeleteLinkResponse Invalid() => new("invalid");
}
