using ClosedXML.Excel;
using G54.BLL.Dtos.Department;

namespace G54.BLL.Services;

/// <summary>Excel export of SCR-PRF-02 / SCR-PRF-03 (docs/api/DEPARTMENT_CONTRACT.md, overview/export).</summary>
public sealed partial class DepartmentService
{
    // Fixed width: measuring text needs fonts the container image may not have.
    private const double ColumnWidth = 18;

    public async Task<DepartmentOverviewExportDto> ExportOverviewAsync(
        Guid accountId,
        Guid departmentId,
        Guid? periodId,
        string? compare = null,
        CancellationToken cancellationToken = default)
    {
        var overview = await GetOverviewAsync(accountId, departmentId, periodId, compare, cancellationToken);
        var selection = await LoadMemberRowsAsync(
            accountId,
            departmentId,
            new DepartmentMemberFilterDto { PeriodId = overview.Period?.Id, Compare = compare, Sort = "CHANGE_DESC" },
            cancellationToken);
        var members = await BuildMembersAsync(selection, 0, selection.Rows.Count, cancellationToken);

        using var workbook = new XLWorkbook();
        WriteSummarySheet(workbook.AddWorksheet("Tong quan"), overview);
        WriteKpiSheet(workbook.AddWorksheet("KPI"), overview);
        WriteMemberSheet(workbook.AddWorksheet("Can bo"), members);
        if (overview.ChildUnits.Count > 0)
        {
            WriteChildUnitSheet(workbook.AddWorksheet("Don vi"), overview.ChildUnits);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var periodCode = overview.Period?.Code ?? "na";
        return new DepartmentOverviewExportDto(
            stream.ToArray(),
            $"hieu-suat-{overview.Department.Code}-{periodCode}.xlsx");
    }

    private static void WriteSummarySheet(IXLWorksheet sheet, DepartmentOverviewDto overview)
    {
        object?[][] rows =
        [
            ["Đơn vị", $"{overview.Department.Code} · {overview.Department.Name}"],
            ["Kỳ đánh giá", overview.Period?.Name],
            ["So với", overview.ComparePeriod?.Name],
            ["Hiệu suất bình quân", overview.Performance.Average],
            ["Hiệu suất kỳ so sánh", overview.Performance.PreviousAverage],
            ["Thay đổi", overview.Performance.ChangeFromPrevious],
            ["Số cán bộ đã chấm", overview.Performance.EvaluatedEmployeeCount],
            ["Cán bộ giảm điểm", overview.Metrics.DeclinedEmployeeCount],
            ["Cán bộ có điểm cả hai kỳ", overview.Metrics.ComparedEmployeeCount],
            ["Chờ thẩm định", overview.Operational.PendingReview],
            ["Quá hạn", overview.Operational.Overdue],
            ["Thời điểm xuất", overview.GeneratedAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture)],
        ];
        WriteRows(sheet, ["Chỉ số", "Giá trị"], rows);
    }

    private static void WriteKpiSheet(IXLWorksheet sheet, DepartmentOverviewDto overview)
    {
        var rows = overview.Performance.KpiGroups
            .SelectMany(group => group.Kpis.Select(kpi => new object?[]
            {
                group.Name ?? "Khác",
                kpi.Code,
                kpi.Name,
                kpi.Unit,
                kpi.TargetValue,
                kpi.ActualValue,
                kpi.CampaignActualValue,
                kpi.AchievementPercent,
                kpi.PreviousAchievementPercent,
            }))
            .ToArray();
        WriteRows(
            sheet,
            ["Nhóm", "Mã KPI", "Chỉ tiêu", "Đơn vị", "Kế hoạch", "Thực hiện", "Từ campaign", "% hoàn thành", "% kỳ so sánh"],
            rows);
    }

    private static void WriteMemberSheet(IXLWorksheet sheet, List<DepartmentMemberDto> members)
    {
        var groupNames = members
            .SelectMany(m => m.Groups)
            .OrderBy(g => g.DisplayOrder)
            .Select(g => g.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        string[] header =
        [
            "Mã cán bộ", "Họ tên", "Vị trí", "Trạng thái", "Trạng thái đánh giá",
            .. groupNames,
            "Tổng", "Tổng kỳ so sánh", "Thay đổi", "Việc mở", "Quá hạn", "Chờ duyệt",
        ];
        var rows = members
            .Select(m => new object?[]
            {
                m.EmployeeCode, m.FullName, m.JobPositionTitle, m.Status, m.EvaluationStatus,
            }
            .Concat(groupNames.Select(name => (object?)m.Groups.FirstOrDefault(g => g.Name == name)?.Score))
            .Concat(new object?[]
            {
                m.PerformanceScore, m.PreviousPerformanceScore, m.ChangeFromPrevious,
                m.OpenWorkCount, m.OverdueWorkCount, m.PendingReviewCount,
            })
            .ToArray())
            .ToArray();
        WriteRows(sheet, header, rows);
    }

    private static void WriteChildUnitSheet(IXLWorksheet sheet, IReadOnlyList<DepartmentChildUnitDto> units)
    {
        var rows = units
            .Select(u => new object?[]
            {
                u.Code, u.Name, u.EmployeeCount, u.EvaluatedEmployeeCount, u.Average, u.PreviousAverage, u.ChangeFromPrevious,
            })
            .ToArray();
        WriteRows(sheet, ["Mã", "Đơn vị", "Cán bộ", "Đã chấm", "Hiệu suất", "Kỳ so sánh", "Thay đổi"], rows);
    }

    /// <summary>Header in bold, then the rows; a null value leaves the cell empty.</summary>
    private static void WriteRows(IXLWorksheet sheet, string[] header, object?[][] rows)
    {
        for (var c = 0; c < header.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = header[c];
        }

        sheet.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                sheet.Cell(r, c + 1).Value = row[c] switch
                {
                    null => Blank.Value,
                    decimal d => d,
                    int i => i,
                    string s => s,
                    var other => other.ToString(),
                };
            }

            r++;
        }

        sheet.Columns().Width = ColumnWidth;
    }
}
