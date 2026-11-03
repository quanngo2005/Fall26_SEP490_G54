using G54.BLL.Common.Import.Reading;

namespace G54.BLL.Common.Import.Session;

/// <summary>
/// The only state kept between preview and commit: the raw rows from the uploaded file. Everything else
/// (classification, DB lookups) is recomputed from current DB state on every read, so a stale cache can
/// never produce a stale decision - only a missing/expired session, which the store reports as null.
/// </summary>
public sealed record ImportSession(
    string Type,
    Guid OwnerUserId,
    string FileName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<RawRow> Rows,
    DateTimeOffset CreatedAt);
