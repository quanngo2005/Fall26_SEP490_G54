using G54.BLL.Dtos.Department;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>A department as held in the in-memory hierarchy.</summary>
internal sealed record DepartmentNode(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    DepartmentLevelType Level,
    RecordStatus Status,
    DateTimeOffset LastSyncAt,
    DateTimeOffset UpdatedAt);

/// <summary>Parent/child structure of all departments, used to resolve subtrees, ancestors and scope roots (cycle-safe).</summary>
internal sealed class DepartmentHierarchy
{
    private static readonly List<Guid> NoChildren = [];
    private readonly Dictionary<Guid, List<Guid>> _children;

    private DepartmentHierarchy(IReadOnlyCollection<DepartmentNode> nodes)
    {
        Nodes = nodes.ToDictionary(n => n.Id);
        _children = nodes
            .Where(n => n.ParentId.HasValue && Nodes.ContainsKey(n.ParentId.Value))
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());
    }

    public static async Task<DepartmentHierarchy> LoadAsync(IQueryable<Department> departments, CancellationToken cancellationToken)
    {
        var nodes = await departments.AsNoTracking()
            .Select(d => new DepartmentNode(
                d.Id,
                d.Code,
                d.Name,
                d.Description,
                d.ParentDepartmentId,
                d.LevelType,
                d.Status,
                d.LastSyncAt,
                d.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new DepartmentHierarchy(nodes);
    }

    public Dictionary<Guid, DepartmentNode> Nodes { get; }

    public List<Guid> ChildrenOf(Guid id) => _children.GetValueOrDefault(id) ?? NoChildren;

    /// <summary>The department and all of its descendants (cycle-safe).</summary>
    public List<Guid> Subtree(Guid id)
    {
        var result = new List<Guid>();
        var seen = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(id);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            result.Add(current);
            foreach (var child in ChildrenOf(current))
            {
                queue.Enqueue(child);
            }
        }

        return result;
    }

    /// <summary>Ancestors ordered root first, excluding the department itself.</summary>
    public List<DepartmentRefDto> Ancestors(Guid id)
    {
        var chain = new List<DepartmentRefDto>();
        var seen = new HashSet<Guid> { id };
        var current = Nodes[id].ParentId;
        while (current.HasValue && Nodes.TryGetValue(current.Value, out var node) && seen.Add(node.Id))
        {
            chain.Add(new DepartmentRefDto(node.Id, node.Code, node.Name));
            current = node.ParentId;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>Nearest BRANCH ancestor-or-self; the root-most ancestor when none exists.</summary>
    public Guid BranchRoot(Guid id)
    {
        var current = id;
        var seen = new HashSet<Guid>();
        while (seen.Add(current))
        {
            var node = Nodes[current];
            if (node.Level == DepartmentLevelType.Branch || !node.ParentId.HasValue
                || !Nodes.ContainsKey(node.ParentId.Value))
            {
                return current;
            }

            current = node.ParentId.Value;
        }

        return current;
    }
}
