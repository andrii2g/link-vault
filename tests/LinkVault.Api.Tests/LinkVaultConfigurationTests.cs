using LinkVault.Api.Options;
using Microsoft.Extensions.Configuration;

namespace LinkVault.Api.Tests;

public sealed class LinkVaultConfigurationTests
{
    [Fact]
    public void GetValidatedListenUrls_ReturnsContainerDefaultWhenUnset()
    {
        var configuration = new ConfigurationBuilder().Build();

        var urls = LinkVaultConfiguration.GetValidatedListenUrls(configuration);

        Assert.Equal(["http://0.0.0.0:5678"], urls);
    }

    [Fact]
    public void GetValidatedListenUrls_AcceptsContainerBindHosts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_URLS"] = "http://0.0.0.0:5678;http://[::]:5678"
            })
            .Build();

        var urls = LinkVaultConfiguration.GetValidatedListenUrls(configuration);

        Assert.Equal(["http://0.0.0.0:5678", "http://[::]:5678"], urls);
    }

    [Fact]
    public void GetValidatedListenUrls_RejectsLoopbackHost()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_URLS"] = "http://localhost:5678"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => LinkVaultConfiguration.GetValidatedListenUrls(configuration));

        Assert.Contains("0.0.0.0 or [::]", exception.Message);
    }

    [Fact]
    public void GetValidatedListenUrls_RejectsInvalidPortOrPath()
    {
        var invalidPort = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_URLS"] = "http://0.0.0.0:5000"
            })
            .Build();

        var invalidPath = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_URLS"] = "http://0.0.0.0:5678/api"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => LinkVaultConfiguration.GetValidatedListenUrls(invalidPort));
        Assert.Throws<InvalidOperationException>(() => LinkVaultConfiguration.GetValidatedListenUrls(invalidPath));
    }
}
