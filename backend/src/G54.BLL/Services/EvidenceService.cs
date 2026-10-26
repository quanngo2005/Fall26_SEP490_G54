using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class EvidenceService(
    AppDbContext db,
    IPermissionService permissions,
    IStorageService storage) : IEvidenceService
{
    private static readonly int DepartmentScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Dept);
    private static readonly int BranchScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.Branch);
    private static readonly int AllScopeLevel = PermissionScopes.GetScopeLevel(PermissionScopes.All);

    public async Task<EvidenceFileDto> OpenAsync(Guid userId, Guid evidenceId, CancellationToken cancellationToken = default)
    {
        var evidence = await db.Evidences.AsNoTracking()
            .Include(e => e.ResultSubmission!)
                .ThenInclude(s => s.SubmitterEmployee)
            .Include(e => e.Appeal!)
                .ThenInclude(a => a.Employee)
            .FirstOrDefaultAsync(e => e.Id == evidenceId, cancellationToken)
            ?? throw new NotFoundException("Tệp bằng chứng", evidenceId);

        var caller = await db.Employees.AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(
                e => e.Id == userId || (e.Account != null && e.Account.Id == userId),
                cancellationToken);
        if (caller?.Account is null || caller.Status != EmployeeStatus.Active)
        {
            throw new ForbiddenException();
        }

        if (!await CanViewAsync(caller, evidence, cancellationToken))
        {
            throw new ForbiddenException("Bạn không có quyền xem tệp bằng chứng này.");
        }

        var extension = Path.GetExtension(evidence.FileName);
        var content = await storage.OpenEvidenceReadAsync(evidence.FilePath, cancellationToken);
        return new EvidenceFileDto(
            content,
            evidence.FileName,
            EvidenceFileValidator.ContentTypeFor(extension),
            EvidenceFileValidator.CanPreview(extension));
    }

    private async Task<bool> CanViewAsync(Employee caller, Evidence evidence, CancellationToken cancellationToken)
    {
        // Evidence of a submission or an appeal belongs to the employee who filed it. Evidence attached to a risk event
        // has no owner or scope rule yet, so only a global verifier sees it.
        var submitter = evidence.ResultSubmission?.SubmitterEmployee ?? evidence.Appeal?.Employee;
        if (submitter is not null && submitter.Id == caller.Id)
        {
            return true;
        }

        var granted = await permissions.GetEffectivePermissionsAsync(caller.Account!.Id, cancellationToken);
        var level = granted
            .Where(p => (p.Resource == "*" && p.Action == "*")
                || (string.Equals(p.Resource, "verification", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Action, "decide", StringComparison.OrdinalIgnoreCase)))
            .Select(p => PermissionScopes.GetScopeLevel(p.Scope))
            .DefaultIfEmpty(0)
            .Max();

        if (level >= AllScopeLevel)
        {
            return true;
        }

        if (level < DepartmentScopeLevel || submitter is null)
        {
            return false;
        }

        var parents = await db.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => new DepartmentLink(d.ParentDepartmentId, d.LevelType), cancellationToken);
        if (!parents.ContainsKey(caller.DepartmentId) || !parents.ContainsKey(submitter.DepartmentId))
        {
            return false;
        }

        var scopeRoot = level >= BranchScopeLevel ? BranchRoot(parents, caller.DepartmentId) : caller.DepartmentId;
        return IsSelfOrDescendant(parents, submitter.DepartmentId, scopeRoot);
    }

    /// <summary>True when <paramref name="rootId"/> is on the path from <paramref name="departmentId"/> up to the top.</summary>
    private static bool IsSelfOrDescendant(Dictionary<Guid, DepartmentLink> parents, Guid departmentId, Guid rootId)
    {
        var current = departmentId;
        var seen = new HashSet<Guid>();
        while (seen.Add(current))
        {
            if (current == rootId)
            {
                return true;
            }

            if (!parents.TryGetValue(current, out var link) || link.ParentId is not { } parent)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    private static Guid BranchRoot(Dictionary<Guid, DepartmentLink> parents, Guid departmentId)
    {
        var current = departmentId;
        var seen = new HashSet<Guid>();
        while (seen.Add(current))
        {
            var link = parents[current];
            if (link.Level == DepartmentLevelType.Branch
                || link.ParentId is not { } parent
                || !parents.ContainsKey(parent))
            {
                return current;
            }

            current = parent;
        }

        return current;
    }

    private readonly record struct DepartmentLink(Guid? ParentId, DepartmentLevelType Level);
}
