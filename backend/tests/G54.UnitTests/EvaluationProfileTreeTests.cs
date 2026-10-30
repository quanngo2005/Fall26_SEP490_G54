using G54.BLL.Services;
using G54.DAL.Entities;
using Xunit;

namespace G54.UnitTests;

public sealed class EvaluationProfileTreeTests
{
    private static EvaluationProfileItem Group(string name, decimal weight = 100m) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        WeightPercent = weight,
        DisplayOrder = 1,
    };

    private static EvaluationProfileItem Leaf(EvaluationProfileItem parent, KpiDefinition? loaded, Guid? kpiId = null) => new()
    {
        Id = Guid.NewGuid(),
        ParentItemId = parent.Id,
        KpiDefinitionId = kpiId ?? loaded?.Id,
        KpiDefinition = loaded,
        WeightPercent = 100m,
        DisplayOrder = 1,
    };

    private static KpiDefinition Kpi(KpiStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Code = $"K-{Guid.NewGuid():N}"[..8],
        Name = "KPI",
        Status = status,
    };

    [Fact]
    public void Kpi_ThrowsWhenALeafsDefinitionWasNotLoaded()
    {
        var group = Group("Kinh doanh");
        var leaf = Leaf(group, loaded: null, kpiId: Guid.NewGuid());
        var root = EvaluationProfileTree.Build([group, leaf]).Single();

        var ex = Assert.Throws<InvalidOperationException>(() => _ = root.Children.Single().Kpi);

        Assert.Contains(leaf.Id.ToString(), ex.Message);
    }

    [Fact]
    public void ActiveLeaves_ThrowsInsteadOfDroppingALeafWhoseDefinitionWasNotLoaded()
    {
        var group = Group("Kinh doanh");
        var root = EvaluationProfileTree.Build([group, Leaf(group, loaded: null, kpiId: Guid.NewGuid())]).Single();

        Assert.Throws<InvalidOperationException>(() => EvaluationProfileTree.ActiveLeaves(root).ToList());
    }

    [Fact]
    public void Kpi_IsNullForAGroup_AndTheDefinitionForALoadedLeaf()
    {
        var group = Group("Kinh doanh");
        var kpi = Kpi(KpiStatus.Active);
        var root = EvaluationProfileTree.Build([group, Leaf(group, kpi)]).Single();

        Assert.Null(root.Kpi);
        Assert.Same(kpi, root.Children.Single().Kpi);
    }

    [Fact]
    public void ActiveLeaves_KeepsLoadedActiveLeavesAndSkipsInactiveOnes()
    {
        var group = Group("Kinh doanh");
        var active = Kpi(KpiStatus.Active);
        var inactive = Kpi(KpiStatus.Inactive);
        var root = EvaluationProfileTree.Build([group, Leaf(group, active), Leaf(group, inactive)]).Single();

        var leaf = Assert.Single(EvaluationProfileTree.ActiveLeaves(root));

        Assert.Equal(active.Id, leaf.Kpi!.Id);
    }
}
