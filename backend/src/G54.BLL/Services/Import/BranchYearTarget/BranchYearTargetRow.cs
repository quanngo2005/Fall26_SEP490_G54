using G54.BLL.Common.Import;

namespace G54.BLL.Services.Import.BranchYearTarget;

/// <summary>
/// One data row of the "KPI Năm-Quý" sheet (<see cref="G54.BLL.Common.Import.Reading.KpiTargetSheetReader"/>).
/// <c>*Raw</c> fields are the cell text; the rest are filled in by <see cref="Rules.BranchYearTargetFieldFormatRule"/>.
/// </summary>
public sealed class BranchYearTargetRow
{
    public int RowNumber { get; set; }

    public string? KpiCodeRaw { get; set; }

    public string? KpiNameRaw { get; set; }

    public string? UnitRaw { get; set; }

    public string? DirectionRaw { get; set; }

    public string? WeightRaw { get; set; }

    public string? PreviousYearRaw { get; set; }

    public string? YearRaw { get; set; }

    public string? Q1Raw { get; set; }

    public string? Q2Raw { get; set; }

    public string? Q3Raw { get; set; }

    public string? Q4Raw { get; set; }

    public string? AggregationRaw { get; set; }

    /// <summary>Normalized (upper, trimmed) <see cref="KpiCodeRaw"/>.</summary>
    public string? KpiCode { get; set; }

    /// <summary>The KPI catalog unit the values were converted to (G9); null until the catalog rule has matched the KPI.</summary>
    public string? StoredUnit { get; set; }

    public decimal? PreviousYearValue { get; set; }

    /// <summary>True when <see cref="YearRaw"/> is the literal <c>-</c>: a ratio KPI whose year value is the
    /// (equal) quarter value instead of a cell of its own (V10).</summary>
    public bool IsRatioYear { get; set; }

    /// <summary>The plan value for the requested year. For a ratio KPI this is filled in from the quarters,
    /// once they are known to be equal (V10), rather than parsed directly from <see cref="YearRaw"/>.</summary>
    public decimal? YearValue { get; set; }

    public decimal? Q1 { get; set; }

    public decimal? Q2 { get; set; }

    public decimal? Q3 { get; set; }

    public decimal? Q4 { get; set; }

    public decimal? WeightValue { get; set; }

    /// <summary><c>CREATE</c>, <c>UPDATE</c>, <c>UNCHANGED</c> or <c>INVALID</c>.</summary>
    public string Action { get; set; } = "INVALID";

    public List<ImportError> Errors { get; } = [];

    public List<ImportError> Warnings { get; } = [];

    public void AddError(string column, string code, string message) => Errors.Add(new ImportError(column, code, message));

    public void AddWarning(string column, string code, string message) => Warnings.Add(new ImportError(column, code, message));
}
