using G54.BLL.Services;

namespace G54.UnitTests;

/// <summary>
/// Pure-math tests for the Target Allocation v3 suggestion engine (docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md,
/// D1/D8). <see cref="SuggestionMath"/> has no I/O and no DI, so these exercise it directly rather than through the
/// full <c>TargetSuggestionService</c> (which needs a running Postgres <c>AppDbContext</c> — see
/// <c>docs/api/TARGET_ALLOCATION_CONTRACT.md</c>, "Suggestion engine", for the manual/HTTP verification that is
/// still outstanding).
/// </summary>
public sealed class TargetSuggestionServiceTests
{
    // =========================================================================
    // RoundToStep
    // =========================================================================

    // xUnit [Theory]/[InlineData] values must be public-visible, so the (internal) mode travels as an int and is
    // cast back just inside the test body.
    [Theory]
    [InlineData(1_234_567, 1_000_000, (int)SuggestionRoundingMode.Nearest, 1_000_000)]
    [InlineData(1_634_567, 1_000_000, (int)SuggestionRoundingMode.Nearest, 2_000_000)]
    [InlineData(1_234_567, 1_000_000, (int)SuggestionRoundingMode.Up, 2_000_000)]
    [InlineData(1_234_567, 1_000_000, (int)SuggestionRoundingMode.Down, 1_000_000)]
    [InlineData(2_000_000, 1_000_000, (int)SuggestionRoundingMode.Nearest, 2_000_000)]
    public void RoundToStep_RoundsToTheRequestedStepAndDirection(decimal value, decimal step, int modeValue, decimal expected)
    {
        Assert.Equal(expected, SuggestionMath.RoundToStep(value, step, (SuggestionRoundingMode)modeValue));
    }

    [Fact]
    public void RoundToStep_NonPositiveStep_ReturnsValueUnchanged()
    {
        Assert.Equal(1_234_567m, SuggestionMath.RoundToStep(1_234_567m, 0m, SuggestionRoundingMode.Nearest));
        Assert.Equal(1_234_567m, SuggestionMath.RoundToStep(1_234_567m, -5m, SuggestionRoundingMode.Up));
    }

    // =========================================================================
    // DistributeByShare: the invariant that must always hold is sum(result) == total.
    // =========================================================================

    [Theory]
    [InlineData((int)SuggestionRoundingMode.Nearest)]
    [InlineData((int)SuggestionRoundingMode.Up)]
    [InlineData((int)SuggestionRoundingMode.Down)]
    public void DistributeByShare_SumIsPreservedAfterRounding_ForEveryMode(int modeValue)
    {
        var mode = (SuggestionRoundingMode)modeValue;
        decimal[] weights = [1_320_000_000m, 1_440_000_000m, 1_500_000_000m, 1_740_000_000m];
        var result = SuggestionMath.DistributeByShare(6_000_000_000m, weights, 10_000_000m, mode, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal(6_000_000_000m, result.Sum());
        Assert.All(result, v => Assert.Equal(0m, v % 10_000_000m));
    }

    [Fact]
    public void DistributeByShare_SumIsPreserved_WhenStepDoesNotDivideWeightsEvenly()
    {
        decimal[] weights = [1m, 1m, 1m]; // 1,000/3 is not a whole number of steps
        var result = SuggestionMath.DistributeByShare(1_000m, weights, 1m, SuggestionRoundingMode.Nearest, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal(1_000m, result.Sum());
    }

    [Fact]
    public void DistributeByShare_LargestRemainder_BreaksTiesByWeightThenIndex()
    {
        // Four equal-weight cells splitting 1,003 at a step of 1 leaves a remainder of 3 units; with every weight
        // (and therefore every fractional remainder) tied, the lowest-index cells win, deterministically.
        decimal[] weights = [1m, 1m, 1m, 1m];
        var result = SuggestionMath.DistributeByShare(1_003m, weights, 1m, SuggestionRoundingMode.Down, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal(1_003m, result.Sum());
        Assert.Equal([251m, 251m, 251m, 250m], result);
    }

    [Fact]
    public void DistributeByShare_LargestRemainder_PrefersTheCellRoundedDownTheMost()
    {
        // Ideal shares (weights [1,2,3,4] of total 110): [11, 22, 33, 44]. DOWN-rounded to a step of 10:
        // [10, 20, 30, 40] (sum 100, diff 10 = exactly one step). Every cell's fractional remainder differs
        // ([1, 2, 3, 4]); the largest (the last cell, rounded down the most in absolute terms) gets the extra step.
        decimal[] weights = [1m, 2m, 3m, 4m];
        var result = SuggestionMath.DistributeByShare(110m, weights, 10m, SuggestionRoundingMode.Down, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal(110m, result.Sum());
        Assert.Equal([10m, 20m, 30m, 50m], result);
    }

    [Fact]
    public void DistributeByShare_Buffer_AbsorbsTheWholeRemainderIntoTheChosenCell()
    {
        decimal[] weights = [1m, 1m, 1m];
        var result = SuggestionMath.DistributeByShare(1_000m, weights, 1m, SuggestionRoundingMode.Down, SuggestionRemainderMethod.Buffer, bufferIndex: 2);

        Assert.Equal(1_000m, result.Sum());
        Assert.Equal(333m, result[0]);
        Assert.Equal(333m, result[1]);
        Assert.Equal(334m, result[2]); // 1,000 - 333 - 333
    }

    [Fact]
    public void DistributeByShare_Buffer_DefaultsToTheLastCellWhenNoIndexGiven()
    {
        decimal[] weights = [1m, 1m, 1m];
        var result = SuggestionMath.DistributeByShare(1_000m, weights, 1m, SuggestionRoundingMode.Down, SuggestionRemainderMethod.Buffer);

        Assert.Equal(1_000m, result.Sum());
        Assert.Equal(334m, result[^1]);
    }

    [Fact]
    public void DistributeByShare_UpMode_StillClosesANegativeRemainderByTakingStepsBack()
    {
        // UP rounds every ideal share (3.33, 3.33, 3.33) up to 4 first, overshooting the total of 10 by 2; the
        // overshoot (a negative diff, exactly 2 whole steps) must be removed by taking a step back from cells,
        // never by an equal split, and the result must still sum to the total.
        decimal[] weights = [1m, 1m, 1m];
        var result = SuggestionMath.DistributeByShare(10m, weights, 1m, SuggestionRoundingMode.Up, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal(10m, result.Sum());
        Assert.Equal([3m, 3m, 4m], result);
        Assert.All(result, v => Assert.True(v >= 0m));
    }

    [Fact]
    public void DistributeByShare_StepLessThanOrEqualToZero_ExactProportionalSplitStillSumsToTotal()
    {
        decimal[] weights = [1m, 2m, 7m];
        var result = SuggestionMath.DistributeByShare(1_000m, weights, 0m, SuggestionRoundingMode.Nearest, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal(1_000m, result.Sum());
    }

    [Fact]
    public void DistributeByShare_ZeroWeightSum_ThrowsInsteadOfFallingBackToAnEqualSplit()
    {
        // "No equal split anywhere" (docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md, D6): a unit set with no history
        // and no fallback weight must never be silently split evenly by this function; the caller is expected to
        // resolve that case upstream (typically a KPI-level disabledReason = "no_history").
        decimal[] weights = [0m, 0m, 0m];
        Assert.Throws<ArgumentException>(() => SuggestionMath.DistributeByShare(900m, weights, 10m, SuggestionRoundingMode.Nearest, SuggestionRemainderMethod.LargestRemainder));
    }

    [Fact]
    public void DistributeByShare_NegativeWeightSum_Throws()
    {
        decimal[] weights = [-5m, -5m];
        Assert.Throws<ArgumentException>(() => SuggestionMath.DistributeByShare(100m, weights, 10m, SuggestionRoundingMode.Nearest, SuggestionRemainderMethod.LargestRemainder));
    }

    [Fact]
    public void DistributeByShare_SingleWeight_ReturnsTheWholeTotal()
    {
        var result = SuggestionMath.DistributeByShare(500m, [1m], 10m, SuggestionRoundingMode.Nearest, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal([500m], result);
    }

    [Fact]
    public void DistributeByShare_EmptyWeights_ReturnsEmpty()
    {
        var result = SuggestionMath.DistributeByShare(500m, [], 10m, SuggestionRoundingMode.Nearest, SuggestionRemainderMethod.LargestRemainder);

        Assert.Empty(result);
    }

    [Fact]
    public void DistributeByShare_WeightsProportionalToIdealShares()
    {
        // A department with twice the previous-year weight of another should end up with roughly twice the share.
        decimal[] weights = [1m, 2m];
        var result = SuggestionMath.DistributeByShare(300m, weights, 1m, SuggestionRoundingMode.Nearest, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal(300m, result.Sum());
        Assert.Equal(100m, result[0]);
        Assert.Equal(200m, result[1]);
    }

    // =========================================================================
    // No-history fallback weights
    // =========================================================================

    [Fact]
    public void ProgressiveQuarterWeights_SplitTenTwentyThirtyForty_AndKeepTheExactSum()
    {
        var result = SuggestionMath.DistributeByShare(
            1_000_000_000m, SuggestionMath.ProgressiveQuarterWeights, 10_000_000m, SuggestionRoundingMode.Nearest, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal([100_000_000m, 200_000_000m, 300_000_000m, 400_000_000m], result);
    }

    [Fact]
    public void ProgressiveQuarterWeights_ClosesTheRemainderOnTheExactSum()
    {
        var result = SuggestionMath.DistributeByShare(
            1_000_000_001m, SuggestionMath.ProgressiveQuarterWeights, 1m, SuggestionRoundingMode.Nearest, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal(1_000_000_001m, result.Sum());
    }

    [Fact]
    public void QuarterShapeOrProgressive_KeepsAParentShapeAndFallsBackToProgressive()
    {
        decimal[] parent = [50m, 10m, 10m, 30m];

        Assert.Same(parent, SuggestionMath.QuarterShapeOrProgressive(parent));
        Assert.Same(SuggestionMath.ProgressiveQuarterWeights, SuggestionMath.QuarterShapeOrProgressive([0m, 0m, 0m, 0m]));
    }

    [Fact]
    public void HeadcountWeights_SplitTheYearByPeople()
    {
        // Tier 2 with no history: departments of 2, 3 and 5 people share 1000 as 200 / 300 / 500.
        var result = SuggestionMath.DistributeByShare(1000m, [2m, 3m, 5m], 1m, SuggestionRoundingMode.Nearest, SuggestionRemainderMethod.LargestRemainder);

        Assert.Equal([200m, 300m, 500m], result);
    }

    // =========================================================================
    // SplitIntoQuarters
    // =========================================================================

    [Fact]
    public void SplitIntoQuarters_RowsSumToYearAndColumnsToParentQuarters()
    {
        // Real case: department 7/8/9/10 = 34, employees 20 and 14.
        decimal[] rows = [20m, 14m];
        decimal[] parent = [7m, 8m, 9m, 10m];

        var result = SuggestionMath.SplitIntoQuarters(rows, [null, null], parent, 1m, SuggestionRoundingMode.Nearest);

        Assert.Equal(20m, result[0].Sum());
        Assert.Equal(14m, result[1].Sum());
        for (var q = 0; q < 4; q++)
        {
            Assert.Equal(parent[q], result[0][q] + result[1][q]);
        }
    }

    [Fact]
    public void SplitIntoQuarters_KeepsFixedRowAndFillsTheRestToTheColumns()
    {
        decimal[] fixedRow = [4m, 5m, 5m, 6m];
        var result = SuggestionMath.SplitIntoQuarters([20m, 14m], [fixedRow, null], [7m, 8m, 9m, 10m], 1m, SuggestionRoundingMode.Nearest);

        Assert.Equal(fixedRow, result[0]);
        Assert.Equal([3m, 3m, 4m, 4m], result[1]);
    }

    [Fact]
    public void SplitIntoQuarters_FallsBackToTheParentShapeWhenRowsDoNotAddUp()
    {
        var result = SuggestionMath.SplitIntoQuarters([10m], [null], [7m, 8m, 9m, 10m], 1m, SuggestionRoundingMode.Nearest);

        Assert.Equal(10m, result[0].Sum());
    }
}
