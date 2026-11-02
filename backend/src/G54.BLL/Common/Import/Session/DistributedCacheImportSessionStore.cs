using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace G54.BLL.Common.Import.Session;

/// <summary>Redis-backed <see cref="IImportSessionStore"/>, same <c>IDistributedCache</c> pattern as <c>PermissionService</c>.</summary>
public sealed class DistributedCacheImportSessionStore(IDistributedCache cache) : IImportSessionStore
{
    private static string KeyFor(Guid importId) => $"import:session:{importId}";

    public async Task<Guid> CreateAsync(ImportSession session, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var importId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(session);
        await cache.SetStringAsync(
            KeyFor(importId),
            json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            cancellationToken);
        return importId;
    }

    public async Task<ImportSession?> GetAsync(Guid importId, CancellationToken cancellationToken)
    {
        var json = await cache.GetStringAsync(KeyFor(importId), cancellationToken);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ImportSession>(json);
        }
        catch (JsonException)
        {
            await cache.RemoveAsync(KeyFor(importId), cancellationToken);
            return null;
        }
    }

    public Task RemoveAsync(Guid importId, CancellationToken cancellationToken) =>
        cache.RemoveAsync(KeyFor(importId), cancellationToken);
}
