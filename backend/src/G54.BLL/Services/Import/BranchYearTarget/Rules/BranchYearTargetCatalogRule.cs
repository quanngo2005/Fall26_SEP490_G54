using G54.BLL.Common.Constants;
using G54.BLL.Common.Import.Reading;
using G54.DAL.Entities;

namespace G54.BLL.Services.Import.BranchYearTarget.Rules;

/// <summary>
/// Looks the KPI up by code (V3), converts the file's monetary unit to the catalog unit (V5/G9), checks the
/// year value's sign against the KPI's direction (V4), and warns on a direction/aggregation mismatch (V6) or a
/// previous-year value that differs from what is already stored (V14). Runs after the quarter rule (-80),
/// since the unit conversion must also scale Q1-Q4 before the committer writes them.
/// </summary>
public static class BranchYearTargetCatalogRule
{
    private const decimal BillionVnd = 1_000_000_000m;
    private const decimal MillionVnd = 1_000_000m;
    public static void Validate(BranchYearTargetRow row, BranchYearTargetContext ctx)
    {
        if (row.Errors.Count > 0)
        {
            return;
        }

        var data = row;

        if (data.KpiCode is null || !ctx.KpiByCode.TryGetValue(data.KpiCode, out var kpi))
        {
            row.AddError(KpiTargetSheetReader.KpiCodeColumn, ErrorCodes.TargetImportKpiNotFound,
                ErrorCodes.GetDefaultMessage(ErrorCodes.TargetImportKpiNotFound));
            return;
        }

        if (kpi.Status != KpiStatus.Active)
        {
            row.AddError(KpiTargetSheetReader.KpiCodeColumn, ErrorCodes.TargetImportKpiInactive,
                ErrorCodes.GetDefaultMessage(ErrorCodes.TargetImportKpiInactive));
            return;
        }

        if (!TryConvertUnit(data.UnitRaw, kpi.Unit, out var factor))
        {
            row.AddError(KpiTargetSheetReader.UnitColumn, ErrorCodes.TargetImportUnitMismatch,
                $"Đơn vị '{data.UnitRaw}' không khớp với danh mục KPI ('{kpi.Unit}').");
            return;
        }

        data.StoredUnit = kpi.Unit;
        if (factor != 1)
        {
            data.PreviousYearValue *= factor;
            data.YearValue *= factor;
            data.Q1 *= factor;
            data.Q2 *= factor;
            data.Q3 *= factor;
            data.Q4 *= factor;
        }

        if (data.YearValue is decimal year)
        {
            var invalid = kpi.Direction switch
            {
                KpiDirection.LowerBetter => year < 0,
                _ => year <= 0, // HigherBetter/OnTarget: KPI_RISK_SEVERE = 0 only valid for LowerBetter
            };
            if (invalid)
            {
                row.AddError(KpiTargetSheetReader.YearColumn, ErrorCodes.TargetImportInvalidValue,
                    ErrorCodes.GetDefaultMessage(ErrorCodes.TargetImportInvalidValue));
            }
        }

        if (string.Equals(kpi.Unit.Trim(), "%", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (column, value) in RatioFields(data))
            {
                if (value is decimal v && (v < 0 || v > 1))
                {
                    row.AddError(column, ErrorCodes.TargetImportInvalidValue, $"'{column}' phải trong khoảng 0-1 (đơn vị %).");
                }
            }
        }

        WarnIfDirectionMismatch(row, data, kpi);
        WarnIfAggregationMismatch(row, data, kpi);
        WarnIfPreviousYearMismatch(row, data, kpi, ctx);
    }

    /// <summary>G9: <c>tỷ VND</c> and <c>triệu VND</c> convert to the catalog's <c>VND</c>; any other unit must
    /// match the catalog exactly (trimmed, case-insensitive).</summary>
    private static bool TryConvertUnit(string? fileUnit, string catalogUnit, out decimal factor)
    {
        var file = (fileUnit ?? string.Empty).Trim();
        var catalog = catalogUnit.Trim();

        if (string.Equals(file, catalog, StringComparison.OrdinalIgnoreCase))
        {
            factor = 1;
            return true;
        }

        if (string.Equals(catalog, "VND", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(file, "tỷ VND", StringComparison.OrdinalIgnoreCase))
            {
                factor = BillionVnd;
                return true;
            }

            if (string.Equals(file, "triệu VND", StringComparison.OrdinalIgnoreCase))
            {
                factor = MillionVnd;
                return true;
            }
        }

        factor = 1;
        return false;
    }

    private static IEnumerable<(string Column, decimal? Value)> RatioFields(BranchYearTargetRow data)
    {
        yield return (KpiTargetSheetReader.YearColumn, data.YearValue);
        yield return (KpiTargetSheetReader.Q1Column, data.Q1);
        yield return (KpiTargetSheetReader.Q2Column, data.Q2);
        yield return (KpiTargetSheetReader.Q3Column, data.Q3);
        yield return (KpiTargetSheetReader.Q4Column, data.Q4);
    }

    private static void WarnIfDirectionMismatch(BranchYearTargetRow row, BranchYearTargetRow data, KpiDefinition kpi)
    {
        if (!string.IsNullOrWhiteSpace(data.DirectionRaw) &&
            !string.Equals(data.DirectionRaw.Trim(), kpi.Direction.ToString(), StringComparison.OrdinalIgnoreCase) &&
            !DirectionMatches(data.DirectionRaw, kpi.Direction))
        {
            row.AddWarning(KpiTargetSheetReader.DirectionColumn, ErrorCodes.TargetImportFieldMismatch,
                $"Chiều '{data.DirectionRaw}' trong file khác với danh mục KPI.");
        }
    }

    private static bool DirectionMatches(string raw, KpiDirection direction) => direction switch
    {
        KpiDirection.HigherBetter => string.Equals(raw.Trim(), "HIGHER_BETTER", StringComparison.OrdinalIgnoreCase),
        KpiDirection.LowerBetter => string.Equals(raw.Trim(), "LOWER_BETTER", StringComparison.OrdinalIgnoreCase),
        _ => string.Equals(raw.Trim(), "ON_TARGET", StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>V6: the file's "Tổng hợp" column still uses COUNT/LAST, which <see cref="KpiAggregationType"/> does
    /// not have (D3: COUNT -&gt; SUM, LAST -&gt; NO_AGGREGATION).</summary>
    private static void WarnIfAggregationMismatch(BranchYearTargetRow row, BranchYearTargetRow data, KpiDefinition kpi)
    {
        if (string.IsNullOrWhiteSpace(data.AggregationRaw))
        {
            return;
        }

        var expected = data.AggregationRaw.Trim().ToUpperInvariant() switch
        {
            "COUNT" => KpiAggregationType.Sum,
            "LAST" => KpiAggregationType.NoAggregation,
            "SUM" => KpiAggregationType.Sum,
            "AVG" => KpiAggregationType.Avg,
            "RATIO" => KpiAggregationType.Ratio,
            _ => (KpiAggregationType?)null,
        };

        if (expected is KpiAggregationType value && value != kpi.AggregationType)
        {
            row.AddWarning(KpiTargetSheetReader.AggregationColumn, ErrorCodes.TargetImportFieldMismatch,
                $"Tổng hợp '{data.AggregationRaw}' trong file khác với danh mục KPI.");
        }
    }

    /// <summary>V14: the comparison on screen reads the database, not the file, so a file value that disagrees
    /// with what is already stored for <c>year - 1</c> is flagged (warning only - the column is read-only).</summary>
    private static void WarnIfPreviousYearMismatch(
        BranchYearTargetRow row, BranchYearTargetRow data, KpiDefinition kpi, BranchYearTargetContext ctx)
    {
        if (data.PreviousYearValue is not decimal fileValue ||
            !ctx.PreviousYearByKpiId.TryGetValue(kpi.Id, out var stored))
        {
            return;
        }

        if (Math.Round(stored.TargetValue, 4, MidpointRounding.AwayFromZero) != fileValue)
        {
            row.AddWarning(KpiTargetSheetReader.PreviousYearColumn, ErrorCodes.TargetImportPrevYearMismatch,
                ErrorCodes.GetDefaultMessage(ErrorCodes.TargetImportPrevYearMismatch));
        }
    }
}
