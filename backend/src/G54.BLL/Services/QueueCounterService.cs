using ClosedXML.Excel;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Queue;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class QueueCounterService
{
    private readonly IUnitOfWork unit;
    private readonly QueueConnection queue;

    internal QueueCounterService(IUnitOfWork unit, QueueConnection queue)
    {
        this.unit = unit;
        this.queue = queue;
    }

    public async Task<CounterState> GetAsync(Guid accountId, CounterCommand? command, CancellationToken ct)
    {
        var staff = await unit.Employees.Query().Where(e => e.Account != null && e.Account.Id == accountId &&
            !e.Account.IsLocked && e.Account.UserRoles.Any(r => r.Role.Code == "EMPLOYEE") && e.Status == EmployeeStatus.Active && e.JobPosition.Status == RecordStatus.Active)
            .Select(e => new { e.Id, e.DepartmentId, e.JobPositionId, e.JobPosition.Code }).SingleOrDefaultAsync(ct);
        if (staff == null || staff.Code is not ("TELLER" or "SALES")) throw new ForbiddenException();
        var tree = await DepartmentHierarchy.LoadAsync(unit.Departments.Query(), ct);
        if (!string.Equals(queue.BranchCode, tree.Nodes[tree.BranchRoot(staff.DepartmentId)].Code, StringComparison.Ordinal))
            throw new ForbiddenException();
        var options = await unit.Repository<JobPositionWorkType>().Query().Where(x => x.JobPositionId == staff.JobPositionId &&
            x.WorkType.Status == RecordStatus.Active).Select(x => x.WorkType.Code).ToArrayAsync(ct);
        CounterState state;
        if (command == null)
            state = await queue.RequestAsync<CounterState>(HttpMethod.Get, $"/internal/counters/{staff.Id}", null, ct);
        else
        {
            if (command.WorkType != null && !options.Contains(command.WorkType, StringComparer.Ordinal))
                throw new BusinessRuleException("INVALID_WORK_TYPE");
            state = await queue.RequestAsync<CounterState>(HttpMethod.Post, "/internal/commands",
                new { command.RequestId, employee = staff.Id, position = staff.Code, command.Action, command.VisitId, command.Service, command.WorkType }, ct);
        }
        return state with { Options = options };
    }

    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        var rows = await queue.RequestAsync<QueueRating[]>(HttpMethod.Get, "/internal/ratings", null, ct);
        var ids = rows.Select(r => r.Id).ToArray();
        var transactions = await unit.WorkTransactions.Query().Where(t => ids.Contains(t.WorkId) && t.ImportedTransactionId != null)
            .Select(t => new { t.WorkId, t.TransactionCode, t.ImportedTransaction!.EmployeeId }).ToListAsync(ct);
        var employeeIds = rows.Select(x => x.Employee).Distinct().ToArray();
        var staff = await unit.Employees.Query().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.EmployeeCode, ct);
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(ServiceRatingImportTemplateProvider.SheetName);
        string[] headers = [ServiceRatingImportTemplateProvider.RatingDateHeader, ServiceRatingImportTemplateProvider.EmployeeCodeHeader,
            ServiceRatingImportTemplateProvider.TransactionCodeHeader, ServiceRatingImportTemplateProvider.ScoreHeader, ServiceRatingImportTemplateProvider.CustomerCommentHeader];
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        var index = 1;
        var byWork = transactions.ToLookup(t => t.WorkId);
        foreach (var rating in rows)
        {
            var codes = byWork[rating.Id].Where(t => t.EmployeeId == rating.Employee).ToArray();
            if (codes.Length != 1 || !staff.TryGetValue(rating.Employee, out var employeeCode)) continue;
            index++;
            sheet.Cell(index, 1).Value = rating.RatedAt.ToOffset(TimeSpan.FromHours(7)).Date;
            sheet.Cell(index, 2).Value = employeeCode;
            sheet.Cell(index, 3).Value = codes[0].TransactionCode;
            sheet.Cell(index, 4).Value = rating.Score;
            sheet.Cell(index, 5).Value = rating.Comment ?? string.Empty;
        }
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }
}
