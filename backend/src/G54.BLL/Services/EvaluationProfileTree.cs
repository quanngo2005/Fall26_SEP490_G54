using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>
/// A node of an evaluation profile's weight tree: a group (a name and children) or a KPI leaf
/// (<see cref="EvaluationProfileItem.KpiDefinitionId"/>). The weight of a node is relative to its siblings.
/// </summary>
public sealed record ProfileNode(EvaluationProfileItem Item, IReadOnlyList<ProfileNode> Children)
{
    public bool IsLeaf => Item.KpiDefinitionId.HasValue;

    /// <summary>
    /// The KPI of a leaf; null for a group. A leaf whose definition was not loaded is a caller error (a missing
    /// <c>Include</c>) and throws, so such a KPI is never silently treated as inactive.
    /// </summary>
    public KpiDefinition? Kpi => Item.KpiDefinitionId.HasValue
        ? Item.KpiDefinition ?? throw new InvalidOperationException(
            $"The KPI definition of evaluation profile item {Item.Id} was not loaded; include EvaluationProfileItems.KpiDefinition.")
        : null;
}

/// <summary>Builds and scores the weight tree of an evaluation profile from its flat items.</summary>
public static class EvaluationProfileTree
{
    /// <summary>The root nodes in display order. Items must be loaded flat; parents are linked by id.</summary>
    public static IReadOnlyList<ProfileNode> Build(IEnumerable<EvaluationProfileItem> items)
    {
        var all = items.ToList();
        var childrenOf = all
            .Where(i => i.ParentItemId.HasValue)
            .ToLookup(i => i.ParentItemId!.Value);

        return all
            .Where(i => !i.ParentItemId.HasValue)
            .OrderBy(i => i.DisplayOrder)
            .Select(i => BuildNode(i, childrenOf))
            .ToList();
    }

    /// <summary>
    /// Name of a root node: a group's own name. The database allows a KPI leaf at the root but the API rejects it, so
    /// the fallback to the KPI name only serves data that was not written through the API (for example a manual edit).
    /// </summary>
    public static string NodeName(ProfileNode node) =>
        node.Item.Name ?? node.Item.KpiDefinition?.Name ?? string.Empty;

    /// <summary>
    /// Code of the catalog KPI group the node's first KPI belongs to, empty when none was loaded. Group nodes carry no
    /// code of their own; this keeps the Business / Service / Operations classification of the evaluation form working.
    /// </summary>
    public static string CatalogGroupCode(ProfileNode node) =>
        Leaves(node).Select(leaf => leaf.Kpi?.KpiGroup?.Code).FirstOrDefault(code => code is not null) ?? string.Empty;

    /// <summary>The KPI leaves beneath a node (the node itself when it is a leaf), in display order.</summary>
    public static IEnumerable<ProfileNode> Leaves(ProfileNode node) =>
        node.IsLeaf ? [node] : node.Children.SelectMany(Leaves);

    /// <summary>
    /// Share of the root <paramref name="root"/> each KPI leaf carries, in percent: the leaf's weight among its siblings
    /// multiplied by the weights of its parent groups up to (not including) the root. Computed from the configured
    /// weights only, so the leaves of a root add up to 100 whether or not each has a score.
    /// </summary>
    public static IReadOnlyDictionary<Guid, decimal> LeafShares(ProfileNode root)
    {
        var shares = new Dictionary<Guid, decimal>();
        Collect(root, 100m, isRoot: true);
        return shares;

        void Collect(ProfileNode node, decimal share, bool isRoot)
        {
            if (node.IsLeaf)
            {
                shares[node.Item.KpiDefinitionId!.Value] = isRoot ? 100m : share;
                return;
            }

            foreach (var child in node.Children)
            {
                Collect(child, share * child.Item.WeightPercent / 100m, isRoot: false);
            }
        }
    }

    /// <summary>The leaves whose KPI definition is active.</summary>
    public static IEnumerable<ProfileNode> ActiveLeaves(ProfileNode node) =>
        Leaves(node).Where(leaf => leaf.Kpi is { Status: KpiStatus.Active });

    /// <summary>
    /// Mean of the scored parts weighted by their weight, normalised over the parts that have a score, so a part
    /// without a score leaves the others' share unchanged in proportion. Null when no part has a score.
    /// </summary>
    public static decimal? WeightedMean(IEnumerable<(decimal Weight, decimal? Score)> parts)
    {
        var scored = parts.Where(p => p.Score.HasValue).ToList();
        var totalWeight = scored.Sum(p => p.Weight);

        return totalWeight <= 0m
            ? null
            : scored.Sum(p => p.Score!.Value * p.Weight) / totalWeight;
    }

    /// <summary>
    /// Score of a node: a leaf takes <paramref name="leafScore"/>, a group the weighted mean of its children.
    /// </summary>
    public static decimal? ScoreNode(ProfileNode node, Func<ProfileNode, decimal?> leafScore) =>
        node.IsLeaf
            ? leafScore(node)
            : WeightedMean(node.Children.Select(child => (child.Item.WeightPercent, ScoreNode(child, leafScore))));

    private static ProfileNode BuildNode(EvaluationProfileItem item, ILookup<Guid, EvaluationProfileItem> childrenOf) =>
        new(
            item,
            childrenOf[item.Id]
                .OrderBy(child => child.DisplayOrder)
                .Select(child => BuildNode(child, childrenOf))
                .ToList());
}
