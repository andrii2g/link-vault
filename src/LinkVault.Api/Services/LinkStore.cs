using LinkVault.Api.Contracts;
using LinkVault.Api.Models;
using LinkVault.Api.Options;
using LinkVault.Api.Serialization;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace LinkVault.Api.Services;

public enum SaveResult
{
    Saved,
    Duplicate,
    StorageFailure
}

public enum TouchResult
{
    Updated,
    Ignored,
    StorageFailure
}

public enum DeleteResult
{
    Deleted,
    Ignored,
    StorageFailure
}

public sealed class LinkStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ILogger<LinkStore> _logger;
    private readonly string _dataPath;

    public LinkStore(IOptions<LinkVaultOptions> options, ILogger<LinkStore> logger)
    {
        _logger = logger;
        _dataPath = options.Value.DataPath;
    }

    public async Task LogStartupStorageStatusAsync(ILogger startupLogger, CancellationToken cancellationToken)
    {
        var status = await GetStorageStatusAsync(cancellationToken);
        if (status == StorageStatus.Healthy)
        {
            startupLogger.LogInformation("Link store ready at {DataPath}", _dataPath);
            return;
        }

        startupLogger.LogError(
            "Link store corruption detected at startup. POST /links, PATCH /links, and DELETE /links will fail until {DataPath} is repaired.",
            _dataPath);
    }

    public async Task<SaveResult> SaveAsync(NormalizedSaveLinkRequest request, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            var readResult = await TryReadItemsAsync(cancellationToken);
            if (!readResult.Success)
            {
                _logger.LogError(
                    "Failed to save URL '{Url}' because the link store at {DataPath} is corrupted or unreadable.",
                    request.Url,
                    _dataPath);
                return SaveResult.StorageFailure;
            }

            var items = readResult.Items!;
            var normalizedKey = NormalizeForDeduplication(request.Url);
            if (items.Any(item => NormalizeForDeduplication(item.Url) == normalizedKey))
            {
                _logger.LogInformation("Duplicate link skipped: {Url}", request.Url);
                return SaveResult.Duplicate;
            }

            items.Add(new UrlItem(
                Guid.NewGuid(),
                request.Url,
                request.Title,
                request.Description,
                request.Tags,
                DateTimeOffset.UtcNow,
                null));

            try
            {
                await WriteAtomicallyAsync(items, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Atomic write failed for link store {DataPath}", _dataPath);
                return SaveResult.StorageFailure;
            }

            _logger.LogInformation("Saved link: {Url}", request.Url);
            return SaveResult.Saved;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<TouchResult> TouchAsync(string url, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var readResult = await TryReadItemsAsync(cancellationToken);
            if (!readResult.Success)
            {
                _logger.LogError("Failed to touch URL '{Url}' because the link store at {DataPath} is corrupted or unreadable.", url, _dataPath);
                return TouchResult.StorageFailure;
            }

            var items = readResult.Items!;
            var normalizedKey = NormalizeForDeduplication(url);
            var index = items.FindIndex(item => NormalizeForDeduplication(item.Url) == normalizedKey);
            if (index < 0)
            {
                return TouchResult.Ignored;
            }

            var existing = items[index];
            items[index] = existing with { UpdatedAt = DateTimeOffset.UtcNow };

            try
            {
                await WriteAtomicallyAsync(items, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Atomic write failed for link touch in store {DataPath}", _dataPath);
                return TouchResult.StorageFailure;
            }

            return TouchResult.Updated;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<DeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var readResult = await TryReadItemsAsync(cancellationToken);
            if (!readResult.Success)
            {
                _logger.LogError("Failed to delete link '{Id}' because the link store at {DataPath} is corrupted or unreadable.", id, _dataPath);
                return DeleteResult.StorageFailure;
            }

            var items = readResult.Items!;
            var removedCount = items.RemoveAll(item => item.Id == id);
            if (removedCount == 0)
            {
                return DeleteResult.Ignored;
            }

            try
            {
                await WriteAtomicallyAsync(items, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Atomic write failed for link delete in store {DataPath}", _dataPath);
                return DeleteResult.StorageFailure;
            }

            _logger.LogInformation("Deleted link: {Id}", id);
            return DeleteResult.Deleted;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<LinkReadResult> GetAllAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var readResult = await TryReadItemsAsync(cancellationToken);
            return readResult.Success
                ? new LinkReadResult(true, readResult.Items!.AsReadOnly())
                : LinkReadResult.Failure;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_dataPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(_dataPath))
        {
            return;
        }

        await WriteAtomicallyAsync([], cancellationToken);
    }

    private async Task<StorageReadResult> TryReadItemsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                _dataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            var items = await JsonSerializer.DeserializeAsync<List<UrlItem>>(stream, JsonOptions, cancellationToken);
            if (items is null)
            {
                _logger.LogError("Link store {DataPath} could not be deserialized into the expected collection shape.", _dataPath);
                return StorageReadResult.Failure;
            }

            if (!IsValidCollection(items))
            {
                _logger.LogError("Link store {DataPath} contains invalid schema content.", _dataPath);
                return StorageReadResult.Failure;
            }

            return new StorageReadResult(true, items);
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Malformed JSON detected in link store {DataPath}", _dataPath);
            return StorageReadResult.Failure;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Failed to read link store {DataPath}", _dataPath);
            return StorageReadResult.Failure;
        }
    }

    private async Task<StorageStatus> GetStorageStatusAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_dataPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_dataPath))
        {
            return StorageStatus.Healthy;
        }

        var result = await TryReadItemsAsync(cancellationToken);
        return result.Success ? StorageStatus.Healthy : StorageStatus.Corrupted;
    }

    private async Task WriteAtomicallyAsync(List<UrlItem> items, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_dataPath) ?? throw new InvalidOperationException("Data path directory is missing.");
        Directory.CreateDirectory(directory);

        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var tempPath = Path.Combine(directory, $"{Path.GetFileName(_dataPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    options: FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                if (File.Exists(_dataPath))
                {
                    File.Replace(tempPath, _dataPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, _dataPath);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
                TryDeleteTempFile(tempPath);
                await Task.Delay(25, cancellationToken);
            }
        }

        throw new IOException("Failed to write the link store atomically after one retry.", lastError);
    }

    private static bool IsValidCollection(List<UrlItem> items)
    {
        return items.All(item =>
            item.Id != Guid.Empty &&
            !string.IsNullOrWhiteSpace(item.Url) &&
            Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            item.Tags is not null);
    }

    private static string NormalizeForDeduplication(string url)
    {
        var trimmed = url.Trim();
        var schemeSeparatorIndex = trimmed.IndexOf("://", StringComparison.Ordinal);
        var normalizedScheme = trimmed[..schemeSeparatorIndex].ToLowerInvariant();
        var remainder = trimmed[(schemeSeparatorIndex + 3)..];
        var authorityEndIndex = remainder.IndexOfAny(['/', '?', '#']);
        var authority = authorityEndIndex >= 0 ? remainder[..authorityEndIndex] : remainder;
        var suffix = authorityEndIndex >= 0 ? remainder[authorityEndIndex..] : string.Empty;
        return $"{normalizedScheme}://{NormalizeAuthority(authority)}{suffix}";
    }

    private static string NormalizeAuthority(string authority)
    {
        var userInfoSplitIndex = authority.LastIndexOf('@');
        var userInfoPrefix = userInfoSplitIndex >= 0 ? authority[..(userInfoSplitIndex + 1)] : string.Empty;
        var hostPort = userInfoSplitIndex >= 0 ? authority[(userInfoSplitIndex + 1)..] : authority;

        if (hostPort.StartsWith("[", StringComparison.Ordinal))
        {
            var closingBracketIndex = hostPort.IndexOf(']');
            var host = hostPort[..(closingBracketIndex + 1)].ToLowerInvariant();
            var portSuffix = hostPort[(closingBracketIndex + 1)..];
            return userInfoPrefix + host + portSuffix;
        }

        var colonIndex = hostPort.LastIndexOf(':');
        if (colonIndex >= 0 && hostPort.Count(character => character == ':') == 1)
        {
            var host = hostPort[..colonIndex].ToLowerInvariant();
            var portSuffix = hostPort[colonIndex..];
            return userInfoPrefix + host + portSuffix;
        }

        return userInfoPrefix + hostPort.ToLowerInvariant();
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        UtcDateTimeOffsetJsonConverters.Apply(options);
        return options;
    }

    private readonly record struct StorageReadResult(bool Success, List<UrlItem>? Items)
    {
        public static StorageReadResult Failure => new(false, null);
    }

    public readonly record struct LinkReadResult(bool Success, IReadOnlyList<UrlItem> Items)
    {
        public static LinkReadResult Failure => new(false, []);
    }

    private enum StorageStatus
    {
        Healthy,
        Corrupted
    }
}
