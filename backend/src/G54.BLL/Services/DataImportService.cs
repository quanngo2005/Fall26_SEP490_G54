using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Pipeline;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Session;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.DataImport;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

public sealed class DataImportService(
    IEnumerable<IImportDefinition> definitions,
    IImportSessionStore sessionStore,
    IExcelSheetReader reader,
    IOptions<SystemParametersOptions> parameters,
    TimeProvider time) : IDataImportService
{
    private const int MaxPageSize = 200;

    public byte[] GetTemplate(string type) => Resolve(type).BuildTemplateFile();

    public async Task<ImportPreviewDto> PreviewAsync(string type, string fileName, Stream file, Guid userId, CancellationToken cancellationToken)
    {
        var definition = Resolve(type);
        var sheet = reader.Read(file, parameters.Value.ImportMaxRows);
        var result = await definition.PreviewAsync(sheet, cancellationToken);

        var ttl = TimeSpan.FromMinutes(parameters.Value.ImportPreviewTtlMinutes);
        var createdAt = time.GetUtcNow();
        var session = new ImportSession(type, userId, fileName, sheet.Headers, sheet.Rows, createdAt);
        var importId = await sessionStore.CreateAsync(session, ttl, cancellationToken);

        return BuildDto(importId, session, createdAt.Add(ttl), result, new ImportPreviewQueryDto());
    }

    public async Task<ImportPreviewDto> GetPreviewAsync(Guid importId, ImportPreviewQueryDto query, Guid userId, CancellationToken cancellationToken)
    {
        var session = await GetOwnedSessionAsync(importId, userId, cancellationToken);
        var definition = Resolve(session.Type);
        var result = await definition.PreviewAsync(new ExcelSheetResult(session.Headers, session.Rows), cancellationToken);
        var expiresAt = session.CreatedAt.Add(TimeSpan.FromMinutes(parameters.Value.ImportPreviewTtlMinutes));

        return BuildDto(importId, session, expiresAt, result, query);
    }

    public async Task<ImportCommitResultDto> CommitAsync(Guid importId, Guid userId, CancellationToken cancellationToken)
    {
        var session = await GetOwnedSessionAsync(importId, userId, cancellationToken);
        var definition = Resolve(session.Type);
        var result = await definition.CommitAsync(new ExcelSheetResult(session.Headers, session.Rows), cancellationToken);
        await sessionStore.RemoveAsync(importId, cancellationToken);

        return new ImportCommitResultDto(
            result.Created,
            result.Updated,
            result.Unchanged,
            result.NewAccounts.Select(a => new ImportNewAccountDto(a.EmployeeCode, a.Username, a.TemporaryPassword)).ToList());
    }

    public async Task CancelAsync(Guid importId, Guid userId, CancellationToken cancellationToken)
    {
        await GetOwnedSessionAsync(importId, userId, cancellationToken);
        await sessionStore.RemoveAsync(importId, cancellationToken);
    }

    private async Task<ImportSession> GetOwnedSessionAsync(Guid importId, Guid userId, CancellationToken cancellationToken)
    {
        var session = await sessionStore.GetAsync(importId, cancellationToken);
        // Different owner looks identical to "not found": no IDOR signal for guessing valid import ids.
        if (session is null || session.OwnerUserId != userId)
        {
            throw new NotFoundException("Phiên import không tồn tại hoặc đã hết hạn.");
        }

        return session;
    }

    private IImportDefinition Resolve(string type) =>
        definitions.FirstOrDefault(d => string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase))
        ?? throw new NotFoundException($"Loại import '{type}' không tồn tại.", ErrorCodes.DataImportTypeNotFound);

    private static ImportPreviewDto BuildDto(
        Guid importId, ImportSession session, DateTimeOffset expiresAt, ImportPipelineResult result, ImportPreviewQueryDto query)
    {
        var filtered = string.IsNullOrWhiteSpace(query.Status)
            ? result.Rows
            : result.Rows.Where(r => string.Equals(r.Action, query.Status, StringComparison.OrdinalIgnoreCase)).ToList();

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, MaxPageSize);
        var paged = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ImportPreviewRowDto(
                r.RowNumber,
                r.Action,
                r.Data,
                r.Errors.Select(e => new ImportErrorDto(e.Column, e.Code, e.Message)).ToList()))
            .ToList();

        return new ImportPreviewDto(
            importId,
            session.Type,
            session.FileName,
            expiresAt,
            new ImportSummaryDto(
                result.Summary.TotalRows, result.Summary.Valid, result.Summary.Invalid,
                result.Summary.Create, result.Summary.Update, result.Summary.Unchanged),
            result.RelatedEntities.Select(e => new ImportRelatedEntityDto(e.Kind, e.Code, e.Name, e.ParentCode, e.Action)).ToList(),
            new PagedResult<ImportPreviewRowDto>(paged, filtered.Count, page, pageSize));
    }
}
