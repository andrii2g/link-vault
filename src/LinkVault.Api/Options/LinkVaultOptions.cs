namespace LinkVault.Api.Options;

public sealed class LinkVaultOptions
{
    public required string DataPath { get; set; }

    public required string[] AllowedExtensionOrigins { get; set; }
}