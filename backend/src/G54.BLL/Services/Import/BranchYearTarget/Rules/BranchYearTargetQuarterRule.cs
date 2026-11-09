using G54.BLL.Common.Constants;
using G54.BLL.Common.Import.Reading;

namespace G54.BLL.Services.Import.BranchYearTarget.Rules;

/// <summary>
/// V10: cross-checks the year value against Q1-Q4. A ratio KPI (<see cref="BranchYearTargetRow.IsRatioYear"/>)
/// needs all four quarters equal - that becomes the year value, since the "Năm nay" cell was <c>-</c>. Any
/// other KPI's quarters should sum to the year value; a mismatch is a warning only (rounding in the source
/// file can leave it off by a cent). Runs after <see cref="BranchYearTargetFieldFormatRule"/> (-90).
/// </summary>
public static class BranchYearTargetQuarterRule
{
    public static void Validate(BranchYearTargetRow row)
    {
        var data = row;
        if (row.Errors.Count > 0 || data.Q1 is null || data.Q2 is null || data.Q3 is null || data.Q4 is null)
        {
            return;
        }

        var quarters = new[] { data.Q1.Value, data.Q2.Value, data.Q3.Value, data.Q4.Value };

        if (data.IsRatioYear)
        {
            if (quarters.Distinct().Count() > 1)
            {
                row.AddError(
                    KpiTargetSheetReader.YearColumn,
                    ErrorCodes.TargetImportInvalidValue,
                    "KPI tỷ lệ/bình quân (Năm nay = '-') phải có Q1 = Q2 = Q3 = Q4.");
                return;
            }

            data.YearValue = quarters[0];
            return;
        }

        if (data.YearValue is null)
        {
            return;
        }

        if (quarters.Sum() != data.YearValue)
        {
            row.AddWarning(
                KpiTargetSheetReader.YearColumn,
                ErrorCodes.TargetImportQuarterSumMismatch,
                ErrorCodes.GetDefaultMessage(ErrorCodes.TargetImportQuarterSumMismatch));
        }
    }
}
