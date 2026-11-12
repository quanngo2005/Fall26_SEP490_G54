using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.TransactionImport;
using G54.BLL.Dtos.WorkTransaction;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class WorkTransactionService(AppDbContext db, TimeProvider time) : IWorkTransactionService
{
    private const int MaxCodes = 10;
    private const int MaxCodeLength = 100;

    public async Task<WorkTransactionsDto> ListAsync(Guid workId, Guid userId, CancellationToken ct = default)
    {
        var work = await LoadOwnWorkAsync(workId, userId, ct);
        return await ToDtoAsync(work, ct);
    }

    public async Task<WorkTransactionsDto> AddAsync(
        Guid workId, Guid userId, AddWorkTransactionsRequestDto request, CancellationToken ct = default)
    {
        var work = await LoadOwnWorkAsync(workId, userId, ct);
        var codes = (request.TransactionCodes ?? []).Select(c => c?.Trim() ?? string.Empty).Where(c => c.Length > 0).Distinct().ToList();
        if (codes.Count == 0 || codes.Any(c => c.Length > MaxCodeLength))
        {
            throw new ValidationException($"transactionCodes must hold 1+ codes of at most {MaxCodeLength} characters.");
        }

        var canSubmit = WorkStateMachine.TryGetNextStatus(work.Status, WorkAction.Submit, out var submitted);
        if (work.Status != WorkStatus.Submitted && !canSubmit)
        {
            throw new BusinessRuleException(ErrorCodes.WorkInvalidState, statusCode: StatusCodes.Status409Conflict);
        }

        if (await db.WorkTransactions.CountAsync(w => w.WorkId == workId, ct) + codes.Count > MaxCodes)
        {
            throw new BusinessRuleException(ErrorCodes.WorkTransactionLimit, statusCode: StatusCodes.Status409Conflict);
        }

        if (await db.WorkTransactions.AnyAsync(w => codes.Contains(w.TransactionCode), ct))
        {
            throw new BusinessRuleException(ErrorCodes.WorkTransactionDuplicate, statusCode: StatusCodes.Status409Conflict);
        }

        var now = time.GetUtcNow();
        var added = codes.Select(c => new WorkTransaction
        {
            Id = Guid.NewGuid(),
            WorkId = workId,
            TransactionCode = c,
            Status = WorkTransactionStatus.InReview,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();
        db.WorkTransactions.AddRange(added);
        if (work.Status != WorkStatus.Submitted)
        {
            work.Status = submitted;
            work.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        foreach (var item in added)
        {
            await ReconcileAsync(item, work, ct);
            await db.SaveChangesAsync(ct);
        }

        await CompleteIfAllMatchedAsync(work, ct);
        return await ToDtoAsync(work, ct);
    }

    public async Task<WorkTransactionsDto> RemoveAsync(Guid workId, Guid transactionId, Guid userId, CancellationToken ct = default)
    {
        var work = await LoadOwnWorkAsync(workId, userId, ct);
        var item = await db.WorkTransactions.FirstOrDefaultAsync(w => w.Id == transactionId && w.WorkId == workId, ct)
            ?? throw new NotFoundException("Work transaction not found.");
        if (item.Status == WorkTransactionStatus.Matched)
        {
            throw new BusinessRuleException(ErrorCodes.WorkInvalidState, statusCode: StatusCodes.Status409Conflict);
        }

        db.WorkTransactions.Remove(item);
        await db.SaveChangesAsync(ct);

        if (work.Status == WorkStatus.Submitted && !await db.WorkTransactions.AnyAsync(w => w.WorkId == workId, ct)
            && WorkStateMachine.TryGetNextStatus(work.Status, WorkAction.Reopen, out var reopened))
        {
            work.Status = reopened;
            work.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        else
        {
            await CompleteIfAllMatchedAsync(work, ct);
        }

        return await ToDtoAsync(work, ct);
    }

    public async Task<ReconcileSummaryDto> ReconcileBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        var pending = await db.WorkTransactions
            .Where(w => w.Status == WorkTransactionStatus.InReview
                && db.ImportedTransactions.Any(t => t.ImportBatchId == batchId && t.TransactionCode == w.TransactionCode))
            .ToListAsync(ct);
        foreach (var item in pending)
        {
            var work = await LoadWorkAsync(item.WorkId, ct);
            await ReconcileAsync(item, work, ct);
            await db.SaveChangesAsync(ct);
            await CompleteIfAllMatchedAsync(work, ct);
        }

        return new ReconcileSummaryDto(
            pending.Count(i => i.Status == WorkTransactionStatus.Matched),
            pending.Count(i => i.Status == WorkTransactionStatus.NotMatched),
            pending.Count(i => i.Status == WorkTransactionStatus.InReview));
    }

    // A code with no imported row yet, or one that cannot be credited yet (no target, locked period), stays IN_REVIEW
    // with the error code as reason so it is retried by the next import; a mismatch is final.
    private async Task ReconcileAsync(WorkTransaction item, Work work, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var imported = await db.ImportedTransactions.FirstOrDefaultAsync(t => t.TransactionCode == item.TransactionCode, ct);
        if (imported is null)
        {
            Set(item, WorkTransactionStatus.InReview, null, now);
        }
        else if (imported.EmployeeId != work.AssigneeEmployeeId)
        {
            Set(item, WorkTransactionStatus.NotMatched, "EMPLOYEE_MISMATCH", now);
        }
        else if (work.WorkType?.ProductCode is not { } product
            || !string.Equals(product, imported.ProductCode, StringComparison.OrdinalIgnoreCase))
        {
            Set(item, WorkTransactionStatus.NotMatched, "PRODUCT_MISMATCH", now);
        }
        else
        {
            try
            {
                await VerifiedResultRecorder.RecordImportedAsync(db, item, work, imported, now, ct);
                item.ImportedTransactionId = imported.Id;
                Set(item, WorkTransactionStatus.Matched, null, now);
            }
            catch (AppException ex) when (ex.ErrorCode == ErrorCodes.MetricTypeMismatch)
            {
                // BR-09: automatic mapping is blocked and the result goes to the manager.
                Set(item, WorkTransactionStatus.NotMatched, ex.ErrorCode, now);
            }
            catch (AppException ex)
            {
                Set(item, WorkTransactionStatus.InReview, ex.ErrorCode, now);
            }
        }
    }

    private async Task CompleteIfAllMatchedAsync(Work work, CancellationToken ct)
    {
        if (work.Status != WorkStatus.Submitted)
        {
            return;
        }

        var statuses = await db.WorkTransactions.Where(w => w.WorkId == work.Id).Select(w => w.Status).ToListAsync(ct);
        if (statuses.Count > 0 && statuses.All(s => s == WorkTransactionStatus.Matched)
            && WorkStateMachine.TryGetNextStatus(work.Status, WorkAction.Verify, out var verified)
            && WorkStateMachine.TryGetNextStatus(verified, WorkAction.Complete, out var done))
        {
            work.Status = done;
            work.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    private static void Set(WorkTransaction item, string status, string? reason, DateTimeOffset now)
    {
        item.Status = status;
        item.Reason = reason;
        item.UpdatedAt = now;
    }

    private async Task<WorkTransactionsDto> ToDtoAsync(Work work, CancellationToken ct)
    {
        var items = await db.WorkTransactions.AsNoTracking().Where(w => w.WorkId == work.Id)
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.TransactionCode)
            .Select(w => new WorkTransactionDto(w.Id, w.TransactionCode, w.Status, w.Reason, w.UpdatedAt))
            .ToListAsync(ct);
        return new WorkTransactionsDto(work.Id, work.Status.ToEnumString(), items);
    }

    private async Task<Work> LoadWorkAsync(Guid workId, CancellationToken ct) =>
        await db.Works
            .Include(w => w.WorkType).ThenInclude(t => t!.WorkTypeKpis).ThenInclude(k => k.KpiDefinition)
            .FirstOrDefaultAsync(w => w.Id == workId, ct)
        ?? throw new NotFoundException("Work not found.");

    private async Task<Work> LoadOwnWorkAsync(Guid workId, Guid userId, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == userId || e.Account!.Id == userId, ct);
        if (employee is null || employee.Status != EmployeeStatus.Active)
        {
            throw new UnauthorizedException("Employee not found or inactive.");
        }

        var work = await LoadWorkAsync(workId, ct);
        return work.AssigneeEmployeeId == employee.Id
            ? work
            : throw new ForbiddenException("Only the assignee can manage the transaction codes of this work.");
    }
}
