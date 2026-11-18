using System.Globalization;
using ClosedXML.Excel;
using G54.BLL.Dtos.Evaluation;

namespace G54.BLL.Services;

/// <summary>
/// Employee Evaluation Form Excel export (`GET /evaluations/form/export`).
/// </summary>
public sealed partial class EvaluationService
{
    public async Task<EvaluationFormExportDto> ExportEvaluationFormAsync(
        Guid employeeId,
        Guid periodId,
        Guid currentUserId,
        Guid? comparePeriodId = null,
        CancellationToken cancellationToken = default)
    {
        var form = await GetEvaluationFormAsync(employeeId, periodId, currentUserId, comparePeriodId, cancellationToken);
        var generatedAt = timeProvider.GetUtcNow();
        var subtitle = BuildFormExportSubtitle(form, generatedAt);

        using var workbook = new XLWorkbook();
        workbook.Style.Font.FontName = "Segoe UI";
        workbook.Style.Font.FontSize = 10;

        WriteFormSummarySheet(workbook.AddWorksheet("Tong quan"), form, generatedAt, subtitle);
        WriteFormCriteriaSheet(workbook.AddWorksheet("Tieu chi & KPI"), form, subtitle);
        WriteFormRiskSheet(workbook.AddWorksheet("Su kien rui ro"), form, subtitle);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return new EvaluationFormExportDto(
            stream.ToArray(),
            $"danh-gia-nhan-vien-{SanitizeFileNameSegment(form.Employee.EmployeeCode)}-{SanitizeFileNameSegment(form.Period.Code)}.xlsx");
    }

    private static void WriteFormSummarySheet(
        IXLWorksheet sheet,
        EvaluationFormDto form,
        DateTimeOffset generatedAt,
        string subtitle)
    {
        var qualitativeGroups = form.CriteriaGroups.Where(g => g.IsQualitative).ToList();
        decimal? qualitativeAverage = qualitativeGroups.Count == 0
            ? form.CurrentEvaluation.GeneralQualitativeScore
            : qualitativeGroups.Where(g => g.QualitativeScore.HasValue).Select(g => g.QualitativeScore!.Value).ToList() switch
            {
                { Count: > 0 } values => Math.Round(values.Average(), 2, MidpointRounding.AwayFromZero),
                _ => null,
            };

        var tierExplanation = form.Summary.ProposedTierExplanation is { Count: > 0 } rules
            ? string.Join(" | ", rules)
            : null;

        object?[][] rows =
        [
            ["Mã nhân viên", form.Employee.EmployeeCode],
            ["Họ và tên", form.Employee.FullName],
            ["Phòng ban", form.Employee.DepartmentName],
            ["Vị trí công việc", form.Employee.JobPositionTitle],
            ["Kỳ đánh giá", $"{form.Period.Code} · {form.Period.Name}"],
            ["Thời gian kỳ", $"{form.Period.StartDate:dd/MM/yyyy} - {form.Period.EndDate:dd/MM/yyyy}"],
            ["Trạng thái kỳ", form.Period.Status],
            ["Hồ sơ đánh giá", $"{form.Profile.Code} · {form.Profile.Name} (v{form.Profile.Version})"],
            ["Trạng thái phiếu", FormatEvaluationStatusLabel(form.CurrentEvaluation.Status)],
            ["Người đánh giá", form.CurrentEvaluation.ReviewerName],
            ["Tổng điểm", form.Summary.TotalScore],
            ["Điểm định lượng (trước phạt)", form.Summary.WeightedScore],
            ["Điểm trừ rủi ro", form.Summary.TotalRiskPenalty],
            ["Điểm định tính", qualitativeAverage],
            ["Xếp hạng đề xuất", form.Summary.ProposedTier],
            ["Giải thích xếp hạng", tierExplanation],
            ["Kỳ trước so sánh", form.PreviousPeriod?.PeriodCode],
            ["Điểm kỳ trước", form.PreviousPeriod?.TotalScore],
            ["Hạng kỳ trước", form.PreviousPeriod?.Tier],
            ["Chênh lệch với kỳ trước", form.PreviousPeriod?.ScoreDifference],
            ["Kết quả công việc chờ duyệt", form.PendingSubmissionCount],
            ["Khiếu nại đang mở", form.OpenAppealCount],
            ["Yêu cầu kèm cặp", form.CurrentEvaluation.CoachingRequired ? "Có" : "Không"],
            ["Kế hoạch kèm cặp", form.CurrentEvaluation.CoachingPlan],
            ["Nhận xét đánh giá", form.CurrentEvaluation.Comment],
            ["Thời điểm xuất", generatedAt.ToString("u", CultureInfo.InvariantCulture)],
        ];

        WriteExportTable(
            sheet,
            "BÁO CÁO ĐÁNH GIÁ HIỆU SUẤT NHÂN VIÊN",
            subtitle,
            ["Chỉ mục", "Giá trị"],
            rows,
            [null, "#,##0.##"],
            maxColumnWidth: 56);

        for (var i = 0; i < rows.Length; i++)
        {
            var label = rows[i][0] as string;
            var valueCell = sheet.Cell(ExportHeaderRow + 1 + i, 2);
            if (label is "Xếp hạng đề xuất" or "Hạng kỳ trước" && valueCell.GetString().Length > 0)
            {
                StyleBadge(valueCell, TierTone(valueCell.GetString()));
            }
            else if (label == "Trạng thái phiếu")
            {
                StyleBadge(valueCell, EvaluationStatusTone(form.CurrentEvaluation.Status));
            }
            else if (label is "Giải thích xếp hạng" or "Kế hoạch kèm cặp" or "Nhận xét đánh giá")
            {
                valueCell.Style.Alignment.WrapText = true;
            }
        }
    }

    private static void WriteFormCriteriaSheet(
        IXLWorksheet sheet,
        EvaluationFormDto form,
        string subtitle)
    {
        var rows = new List<object?[]>();

        foreach (var group in form.CriteriaGroups)
        {
            if (group.IsQualitative || group.KpiItems.Count == 0)
            {
                rows.Add([
                    group.GroupName,
                    group.WeightPercent,
                    group.GroupScore,
                    group.IsQualitative ? "Định tính" : "Định lượng",
                    null,
                    group.IsQualitative ? "Nhóm tiêu chí định tính" : null,
                    null,
                    null,
                    null,
                    group.QualitativeScore,
                    null,
                    group.GroupScore,
                    group.QualitativeNotes,
                ]);
                continue;
            }

            foreach (var kpi in group.KpiItems)
            {
                rows.Add([
                    group.GroupName,
                    group.WeightPercent,
                    group.GroupScore,
                    "Định lượng",
                    kpi.KpiCode,
                    kpi.KpiName,
                    kpi.Unit,
                    kpi.WeightPercent,
                    kpi.TargetValue,
                    kpi.ActualValue,
                    kpi.AchievementPercent,
                    kpi.ComponentScore,
                    group.QualitativeNotes,
                ]);
            }
        }

        WriteExportTable(
            sheet,
            "CHI TIẾT NHÓM TIÊU CHÍ & KPI",
            subtitle,
            [
                "Nhóm tiêu chí",
                "Trọng số nhóm (%)",
                "Điểm nhóm",
                "Loại nhóm",
                "Mã KPI",
                "Tên chỉ tiêu",
                "Đơn vị",
                "Tỷ trọng KPI (%)",
                "Mục tiêu",
                "Thực đạt / Điểm nhập",
                "% Đạt",
                "Điểm quy đổi",
                "Ghi chú",
            ],
            rows.ToArray(),
            [
                null,
                "0.##",
                "0.##",
                null,
                null,
                null,
                null,
                "0.##",
                "#,##0.##",
                "#,##0.##",
                "[>=100][Color10]0.##\"%\";[Red]0.##\"%\"",
                "0.##",
                null,
            ],
            maxColumnWidth: 52);
    }

    private static void WriteFormRiskSheet(
        IXLWorksheet sheet,
        EvaluationFormDto form,
        string subtitle)
    {
        var rows = form.RiskEvents
            .Select(risk => new object?[]
            {
                risk.Code,
                risk.EventType,
                risk.Description,
                risk.Severity,
                risk.PenaltyPoints,
                risk.OccurredAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            })
            .ToArray();

        WriteExportTable(
            sheet,
            "SỰ KIỆN RỦI RO & ĐIỂM TRỪ TRONG KỲ",
            subtitle,
            [
                "Mã sự kiện",
                "Loại sự kiện",
                "Mô tả",
                "Mức độ",
                "Điểm trừ",
                "Thời gian ghi nhận",
            ],
            rows,
            [
                null,
                null,
                null,
                null,
                "[Red]-0.##;0.##",
                null,
            ],
            maxColumnWidth: 52);

        for (var i = 0; i < rows.Length; i++)
        {
            var severityCell = sheet.Cell(ExportHeaderRow + 1 + i, 4);
            if (severityCell.GetString().Length > 0)
            {
                StyleBadge(severityCell, RiskSeverityTone(severityCell.GetString()));
            }
        }
    }

    private static string BuildFormExportSubtitle(EvaluationFormDto form, DateTimeOffset generatedAt) =>
        $"{form.Employee.EmployeeCode} · {form.Employee.FullName} · {form.Employee.DepartmentName} · Kỳ: {form.Period.Code} ({form.Period.Name}) · Xuất ngày {generatedAt:dd/MM/yyyy}";

    private static string SanitizeFileNameSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch));
    }

    private static string FormatEvaluationStatusLabel(string status) => status switch
    {
        "DRAFT" => "Nháp",
        "CALCULATED" => "Đã tính điểm",
        "IN_REVIEW" => "Đang hiệu chỉnh",
        "APPROVED" => "Đã duyệt",
        "DISPUTED" => "Đang khiếu nại",
        "FINAL" => "Đã chốt",
        _ => status,
    };

    private static (string Fill, string Font) EvaluationStatusTone(string status) => status switch
    {
        "FINAL" or "APPROVED" => ExportGoodTone,
        "CALCULATED" or "IN_REVIEW" => ExportInfoTone,
        "DISPUTED" => ExportBadTone,
        _ => ExportWarnTone,
    };

    private static (string Fill, string Font) RiskSeverityTone(string severity) => severity switch
    {
        "CRITICAL" or "HIGH" => ExportBadTone,
        "MEDIUM" => ExportWarnTone,
        _ => ExportInfoTone,
    };
}
