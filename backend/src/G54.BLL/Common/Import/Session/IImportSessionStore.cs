namespace G54.BLL.Common.Import.Session;

/// <summary>
/// Hides Redis (<c>IDistributedCache</c>) behind a store so the engine does not create an "import" table
/// (ERD decision) and so tests can fake storage. A session disappears on TTL expiry or Redis restart;
/// callers then re-upload.
/// </summary>
public interface IImportSessionStore
{
    /// <summary>Stores the session under a new id with the given TTL and returns that id.</summary>
    Task<Guid> CreateAsync(ImportSession session, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Returns null when the id does not exist or has expired.</summary>
    Task<ImportSession?> GetAsync(Guid importId, CancellationToken cancellationToken);

    Task RemoveAsync(Guid importId, CancellationToken cancellationToken);
}
