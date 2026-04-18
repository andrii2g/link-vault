using LinkVault.Api.Contracts;
using LinkVault.Api.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LinkVault.Api.Tests;

public sealed class SaveApiTests : IDisposable
{
    private const string AllowedOrigin = "chrome-extension://test-extension-id";

    private readonly string _tempDirectory;
    private readonly string _dataPath;
    private readonly string? _previousDataPath;
    private readonly string? _previousAllowedOrigins;
    private readonly LinkVaultApiFactory _factory;
    private readonly HttpClient _client;

    public SaveApiTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LinkVault.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _dataPath = Path.Combine(_tempDirectory, "links.json");

        _previousDataPath = Environment.GetEnvironmentVariable("LINK_VAULT_DATA_PATH");
        _previousAllowedOrigins = Environment.GetEnvironmentVariable("LINK_VAULT_ALLOWED_EXTENSION_ORIGINS");
        Environment.SetEnvironmentVariable("LINK_VAULT_DATA_PATH", _dataPath);
        Environment.SetEnvironmentVariable("LINK_VAULT_ALLOWED_EXTENSION_ORIGINS", AllowedOrigin);

        _factory = new LinkVaultApiFactory();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task PostLinks_WritesNewItemWithServerGeneratedFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync("/links", new SaveLinkRequest
        {
            Url = " https://example.com/path ",
            Title = " Example ",
            Description = " Desc ",
            Tags = [" one ", "", " two " ]
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SaveLinkResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("saved", body.Status);

        var items = await ReadItemsAsync(cancellationToken);
        var item = Assert.Single(items);
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal("https://example.com/path", item.Url);
        Assert.Equal("Example", item.Title);
        Assert.Equal("Desc", item.Description);
        Assert.Equal(["one", "two"], item.Tags);
        Assert.True(item.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Null(item.UpdatedAt);
    }

    [Fact]
    public async Task PostLinks_DetectsDuplicatesWithoutNormalizingDefaultPorts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await _client.PostAsJsonAsync("/links", new SaveLinkRequest { Url = "https://EXAMPLE.com/demo" }, cancellationToken);

        var duplicateResponse = await _client.PostAsJsonAsync("/links", new SaveLinkRequest { Url = "https://example.com/demo" }, cancellationToken);
        var distinctResponse = await _client.PostAsJsonAsync("/links", new SaveLinkRequest { Url = "https://example.com:443/demo" }, cancellationToken);

        Assert.Equal("duplicate", (await duplicateResponse.Content.ReadFromJsonAsync<SaveLinkResponse>(cancellationToken))?.Status);
        Assert.Equal("saved", (await distinctResponse.Content.ReadFromJsonAsync<SaveLinkResponse>(cancellationToken))?.Status);

        var items = await ReadItemsAsync(cancellationToken);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task PostLinks_ReturnsInvalidForNonHttpUrls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync("/links", new SaveLinkRequest { Url = "chrome://settings" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SaveLinkResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("invalid", body.Status);
        Assert.False(File.Exists(_dataPath));
    }

    [Fact]
    public async Task PostLinks_IgnoresClientControlledFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var payload = new
        {
            url = "https://example.com/with-client-fields",
            id = Guid.Empty,
            createdAt = "2000-01-01T00:00:00Z",
            updatedAt = "2000-01-01T00:00:00Z"
        };

        var response = await _client.PostAsJsonAsync("/links", payload, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadItemsAsync(cancellationToken);
        var item = Assert.Single(items);
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.NotEqual(DateTimeOffset.Parse("2000-01-01T00:00:00Z"), item.CreatedAt);
        Assert.Null(item.UpdatedAt);
    }

    [Fact]
    public async Task PostLinks_SerializesConcurrentWritesWithoutCorruptingJson()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var tasks = Enumerable.Range(0, 12)
            .Select(index => _client.PostAsJsonAsync("/links", new SaveLinkRequest { Url = $"https://example.com/{index}" }, cancellationToken))
            .ToArray();

        await Task.WhenAll(tasks).WaitAsync(cancellationToken);

        var items = await ReadItemsAsync(cancellationToken);
        Assert.Equal(12, items.Count);
        Assert.All(tasks, task => Assert.Equal(HttpStatusCode.OK, task.Result.StatusCode));
    }

    [Fact]
    public async Task PatchLinks_UpdatesUpdatedAtWhenUrlExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _client.PostAsJsonAsync("/links", new SaveLinkRequest { Url = "https://example.com/touched" }, cancellationToken);

        var response = await _client.PatchAsJsonAsync("/links", new TouchLinkRequest { Url = "https://example.com/touched" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TouchLinkResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("updated", body.Status);

        var item = Assert.Single(await ReadItemsAsync(cancellationToken));
        Assert.NotNull(item.UpdatedAt);
        Assert.True(item.UpdatedAt > item.CreatedAt);
    }

    [Fact]
    public async Task DeleteLinks_RemovesItemWhenIdExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _client.PostAsJsonAsync("/links", new SaveLinkRequest { Url = "https://example.com/delete-me" }, cancellationToken);
        var item = Assert.Single(await ReadItemsAsync(cancellationToken));

        var response = await _client.DeleteAsync($"/links?id={item.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeleteLinkResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("deleted", body.Status);
        Assert.Empty(await ReadItemsAsync(cancellationToken));
    }

    [Fact]
    public async Task DeleteLinks_IgnoresUnknownId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _client.DeleteAsync($"/links?id={Guid.NewGuid()}", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeleteLinkResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("ignored", body.Status);
    }

    [Fact]
    public async Task PostAndPatchLinks_WriteTimestampsWithIdenticalPrecision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await _client.PostAsJsonAsync("/links", new SaveLinkRequest { Url = "https://example.com/precision" }, cancellationToken);
        await _client.PatchAsJsonAsync("/links", new TouchLinkRequest { Url = "https://example.com/precision" }, cancellationToken);

        var json = await File.ReadAllTextAsync(_dataPath, cancellationToken);

        Assert.Matches(new Regex("\"createdAt\":\\s*\"\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{3}Z\""), json);
        Assert.Matches(new Regex("\"updatedAt\":\\s*\"\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{3}Z\""), json);
    }

    [Fact]
    public async Task PatchLinks_IgnoresUnknownUrl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await _client.PatchAsJsonAsync("/links", new TouchLinkRequest { Url = "https://example.com/missing" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TouchLinkResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("ignored", body.Status);
        Assert.Empty(await ReadItemsAsync(cancellationToken));
    }

    [Fact]
    public async Task LinksPage_RendersSavedItemsAndDeleteButtons()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await _client.PostAsJsonAsync("/links", new SaveLinkRequest
        {
            Url = "https://example.com/alpha",
            Title = "Alpha",
            Description = "First link",
            Tags = ["one", "two"]
        }, cancellationToken);

        var response = await _client.GetAsync("/links", cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var item = Assert.Single(await ReadItemsAsync(cancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Saved links", html);
        Assert.Contains("Alpha", html);
        Assert.Contains("https://example.com/alpha", html);
        Assert.Contains("First link", html);
        Assert.Contains("one", html);
        Assert.Contains("Delete", html);
        Assert.Contains($"data-delete-id=\"{item.Id}\"", html);
    }

    [Fact]
    public async Task PostLinks_ReturnsServerErrorWhenStorageIsCorrupted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await File.WriteAllTextAsync(_dataPath, "{ not valid json", cancellationToken);

        var response = await _client.PostAsJsonAsync("/links", new SaveLinkRequest { Url = "https://example.com" }, cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var storedText = await File.ReadAllTextAsync(_dataPath, cancellationToken);
        Assert.Equal("{ not valid json", storedText);
    }

    [Fact]
    public async Task Health_ReturnsOkEvenWhenStorageIsCorrupted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await File.WriteAllTextAsync(_dataPath, "{ not valid json", cancellationToken);

        var response = await _client.GetAsync("/health", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("OK", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Environment.SetEnvironmentVariable("LINK_VAULT_DATA_PATH", _previousDataPath);
        Environment.SetEnvironmentVariable("LINK_VAULT_ALLOWED_EXTENSION_ORIGINS", _previousAllowedOrigins);
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private async Task<List<UrlItem>> ReadItemsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_dataPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_dataPath);
        var items = await JsonSerializer.DeserializeAsync<List<UrlItem>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
        return Assert.IsType<List<UrlItem>>(items);
    }
}
