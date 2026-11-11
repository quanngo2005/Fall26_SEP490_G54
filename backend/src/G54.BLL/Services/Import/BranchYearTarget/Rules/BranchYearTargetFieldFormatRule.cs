using System.Globalization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Import.Reading;

namespace G54.BLL.Services.Import.BranchYearTarget.Rules;

/// <summary>
/// Parses every cell on its own row: the KPI code shape, and every numeric column (V4, V12). A formula cell
/// with no cached value (<see cref="KpiTargetSheetReader.FormulaNotCalculatedMarker"/>) is reported as
/// <c>TARGET_IMPORT_FORMULA_NOT_CALCULATED</c> rather than a parse failure (V13). The literal text <c>-</c> in
/// "Năm nay" marks a ratio KPI, whose year value comes from the (equal) quarters instead (V10, filled in by
/// <see cref="BranchYearTargetQuarterRule"/>, which runs after this rule).
/// </summary>
public static class BranchYearTargetFieldFormatRule
{
    public static void Validate(BranchYearTargetRow row)
    {
        var data = row;

        var kpiCode = (data.KpiCodeRaw ?? string.Empty).Trim().ToUpperInvariant();
        data.KpiCode = kpiCode.Length > 0 ? kpiCode : null;

        ParseDecimal(row, KpiTargetSheetReader.PreviousYearColumn, data.PreviousYearRaw, allowDash: true, value => data.PreviousYearValue = value);

        if (string.Equals(data.YearRaw?.Trim(), "-", StringComparison.Ordinal))
        {
            data.IsRatioYear = true;
        }
        else
        {
            ParseDecimal(row, KpiTargetSheetReader.YearColumn, data.YearRaw, allowDash: false, value => data.YearValue = value);
        }

        ParseDecimal(row, KpiTargetSheetReader.Q1Column, data.Q1Raw, allowDash: false, value => data.Q1 = value);
        ParseDecimal(row, KpiTargetSheetReader.Q2Column, data.Q2Raw, allowDash: false, value => data.Q2 = value);
        ParseDecimal(row, KpiTargetSheetReader.Q3Column, data.Q3Raw, allowDash: false, value => data.Q3 = value);
        ParseDecimal(row, KpiTargetSheetReader.Q4Column, data.Q4Raw, allowDash: false, value => data.Q4 = value);

        if (!string.IsNullOrWhiteSpace(data.WeightRaw) &&
            decimal.TryParse(data.WeightRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
        {
            data.WeightValue = Math.Round(weight, 4, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>Rounds to 4 decimals (V12) to absorb the cached-value floating-point noise described in the
    /// design (e.g. <c>1.7399999999999998</c>).</summary>
    private static void ParseDecimal(
        BranchYearTargetRow row, string column, string? raw, bool allowDash, Action<decimal> onValid)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (raw == KpiTargetSheetReader.FormulaNotCalculatedMarker)
        {
            row.AddError(column, ErrorCodes.TargetImportFormulaNotCalculated,
                ErrorCodes.GetDefaultMessage(ErrorCodes.TargetImportFormulaNotCalculated));
            return;
        }

        if (allowDash && raw.Trim() == "-")
        {
            return;
        }

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            onValid(Math.Round(value, 4, MidpointRounding.AwayFromZero));
        }
        else
        {
            row.AddError(column, ErrorCodes.TargetImportInvalidValue, $"'{column}' có giá trị '{raw}' không hợp lệ.");
        }
    }
}
