using System.Globalization;
using ClosedXML.Excel;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;

namespace G54.BLL.Common.Import.Reading;

/// <summary>
/// Reads the "KPI Năm-Quý" sheet of the year-target workbook (docs/design/BRANCH_YEAR_KPI_IMPORT_DESIGN.md,
/// section 3.1). Unlike <see cref="ClosedXmlSheetReader"/>, the header is not on row 1, columns are matched by
/// header text rather than a fixed letter (the real file shifted Q1-Q4 when <c>Năm 2026</c> was added), and
/// formula cells must use the value Excel last cached - re-evaluating them is unreliable across sheets.
/// </summary>
public static class KpiTargetSheetReader
{
    /// <summary>A formula cell with no cached value is reported as this row's raw text, never as 0 (section 1.3).</summary>
    public const string FormulaNotCalculatedMarker = "#FORMULA_NOT_CALCULATED#";

    private const string KpiCodeHeader = "Mã KPI";
    private const string BranchCodeLabel = "Mã chi nhánh";
    private const int MaxHeaderScanRows = 10;

    // Canonical headers of the ExcelSheetResult this reader produces; BranchYearTargetImportDefinition's
    // template matches these by exact text, independent of where each concept sits in the source file.
    public const string KpiCodeColumn = "Mã KPI";
    public const string KpiNameColumn = "Tên KPI";
    public const string UnitColumn = "Đơn vị";
    public const string DirectionColumn = "Chiều";
    public const string WeightColumn = "Trọng số";
    public const string PreviousYearColumn = "Năm trước";
    public const string YearColumn = "Năm nay";
    public const string Q1Column = "Q1";
    public const string Q2Column = "Q2";
    public const string Q3Column = "Q3";
    public const string Q4Column = "Q4";
    public const string AggregationColumn = "Tổng hợp";

    private static readonly string[] CanonicalHeaders =
    [
        KpiCodeColumn, KpiNameColumn, UnitColumn, DirectionColumn, WeightColumn,
        PreviousYearColumn, YearColumn, Q1Column, Q2Column, Q3Column, Q4Column, AggregationColumn,
    ];

    /// <summary>A data row's <c>Mã KPI</c> must match this shape; group/total/note rows are skipped (section 1).</summary>
    private static readonly System.Text.RegularExpressions.Regex KpiCodePattern = new("^KPI_[A-Z0-9_]+$");

    public static KpiTargetSheetResult Read(Stream excel, int year, int maxRows)
    {
        ExcelUploadGuard.EnsureUncompressedSizeWithinLimit(excel);
        using var workbook = new XLWorkbook(excel);
        var worksheet = workbook.Worksheets.FirstOrDefault(w => w.Name.StartsWith("KPI Năm-Quý", StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.FirstOrDefault()
            ?? throw new ValidationException("File Excel không có worksheet nào.");

        var headerRowNumber = FindHeaderRow(worksheet);
        var sourceColumns = MapSourceColumns(worksheet, headerRowNumber, year);

        var ratioRowNumber = headerRowNumber - 2;
        var quarterRatios = ReadQuarterRatios(worksheet, ratioRowNumber, sourceColumns);
        var branchCodeRaw = ReadBranchCode(worksheet, headerRowNumber);

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRowNumber;
        var rows = new List<RawRow>();
        for (var rowNumber = headerRowNumber + 1; rowNumber <= lastRow; rowNumber++)
        {
            var kpiCode = GetCellText(worksheet.Cell(rowNumber, sourceColumns[KpiCodeColumn]));
            if (!KpiCodePattern.IsMatch((kpiCode ?? string.Empty).Trim()))
            {
                continue; // group header, total row or note - not a KPI data row
            }

            if (rows.Count >= maxRows)
            {
                throw new ValidationException($"File vượt quá số dòng tối đa cho phép ({maxRows}).");
            }

            var cells = CanonicalHeaders
                .Select(header => sourceColumns.TryGetValue(header, out var col) ? GetCellText(worksheet.Cell(rowNumber, col)) : null)
                .ToList();
            rows.Add(new RawRow(rowNumber, cells));
        }

        return new KpiTargetSheetResult(CanonicalHeaders, rows, quarterRatios, branchCodeRaw);
    }

    /// <summary>
    /// The real header row has both <c>Mã KPI</c> and <c>Q1</c>. The sample file also has a merged sub-header
    /// row above it ("Mã KPI" over the code/name columns, section 1 item 1) with no <c>Q1</c>, which this must
    /// not be mistaken for.
    /// </summary>
    private static int FindHeaderRow(IXLWorksheet worksheet)
    {
        var lastColumn = Math.Max(worksheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 1, 20);
        for (var row = 1; row <= MaxHeaderScanRows; row++)
        {
            var hasKpiCode = false;
            var hasQ1 = false;
            for (var col = 1; col <= lastColumn; col++)
            {
                var text = worksheet.Cell(row, col).GetString().Trim();
                hasKpiCode |= string.Equals(text, KpiCodeHeader, StringComparison.OrdinalIgnoreCase);
                hasQ1 |= string.Equals(text, "Q1", StringComparison.OrdinalIgnoreCase);
            }

            if (hasKpiCode && hasQ1)
            {
                return row;
            }
        }

        throw new BusinessRuleException(ErrorCodes.ImportTemplateInvalid, "File không đúng mẫu quy định (không tìm thấy cột 'Mã KPI').", 400);
    }

    private static Dictionary<string, int> MapSourceColumns(IXLWorksheet worksheet, int headerRow, int year)
    {
        var lastColumn = worksheet.Row(headerRow).LastCellUsed()?.Address.ColumnNumber ?? 1;
        var byText = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var col = 1; col <= lastColumn; col++)
        {
            var text = worksheet.Cell(headerRow, col).GetString().Trim();
            if (text.Length > 0 && !byText.ContainsKey(text))
            {
                byText[text] = col;
            }
        }

        var columns = new Dictionary<string, int>();
        if (byText.TryGetValue(KpiCodeHeader, out var kpiCodeCol))
        {
            columns[KpiCodeColumn] = kpiCodeCol;
        }

        if (byText.TryGetValue($"Năm {year}", out var yearCol))
        {
            columns[YearColumn] = yearCol;
        }
        else
        {
            throw new BusinessRuleException(
                ErrorCodes.TargetImportYearColumnMissing,
                $"File không có cột 'Năm {year}' của năm cần nhập.",
                400);
        }

        if (byText.TryGetValue($"Năm {year - 1}", out var previousYearCol))
        {
            columns[PreviousYearColumn] = previousYearCol;
        }

        void MapIfPresent(string sourceHeader, string canonical)
        {
            if (byText.TryGetValue(sourceHeader, out var col))
            {
                columns[canonical] = col;
            }
        }

        MapIfPresent("Tên KPI", KpiNameColumn);
        MapIfPresent("Đơn vị", UnitColumn);
        MapIfPresent("Chiều", DirectionColumn);
        MapIfPresent("Trọng số", WeightColumn);
        MapIfPresent("Q1", Q1Column);
        MapIfPresent("Q2", Q2Column);
        MapIfPresent("Q3", Q3Column);
        MapIfPresent("Q4", Q4Column);
        MapIfPresent("Tổng hợp", AggregationColumn);

        return columns;
    }

    private static Dictionary<string, decimal> ReadQuarterRatios(
        IXLWorksheet worksheet, int ratioRowNumber, Dictionary<string, int> sourceColumns)
    {
        var ratios = new Dictionary<string, decimal>();
        if (ratioRowNumber < 1)
        {
            return ratios;
        }

        foreach (var quarter in new[] { Q1Column, Q2Column, Q3Column, Q4Column })
        {
            if (sourceColumns.TryGetValue(quarter, out var col))
            {
                var text = GetCellText(worksheet.Cell(ratioRowNumber, col));
                if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
                {
                    ratios[quarter] = ratio;
                }
            }
        }

        return ratios;
    }

    /// <summary>Optional <c>Mã chi nhánh:</c> label anywhere in the title rows, branch code in the next cell (G2).</summary>
    private static string? ReadBranchCode(IXLWorksheet worksheet, int headerRow)
    {
        var lastColumn = worksheet.Row(headerRow).LastCellUsed()?.Address.ColumnNumber ?? 1;
        for (var row = 1; row < headerRow; row++)
        {
            for (var col = 1; col <= lastColumn; col++)
            {
                var text = worksheet.Cell(row, col).GetString().Trim();
                if (text.StartsWith(BranchCodeLabel, StringComparison.OrdinalIgnoreCase))
                {
                    var next = GetCellText(worksheet.Cell(row, col + 1))?.Trim();
                    return string.IsNullOrWhiteSpace(next) ? null : next;
                }
            }
        }

        return null;
    }

    /// <summary>Literal cells use <see cref="IXLCell.GetString"/>; formula cells use the cached value only
    /// (never ClosedXML's evaluator), per section 1.3. A formula with no cache yields <see cref="FormulaNotCalculatedMarker"/>.</summary>
    private static string? GetCellText(IXLCell cell)
    {
        if (!cell.HasFormula)
        {
            return cell.GetString();
        }

        var cached = cell.CachedValue;
        if (cached.IsBlank)
        {
            return FormulaNotCalculatedMarker;
        }

        if (cached.IsText)
        {
            return cached.GetText();
        }

        if (cached.IsNumber)
        {
            return cached.GetNumber().ToString(CultureInfo.InvariantCulture);
        }

        return cached.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Header/data rows in the engine's generic shape (canonical headers, mapped by header text rather than file
/// column letters), plus the two things no other import needs: the file's quarter split ratios (row 2) and an
/// optional branch code cell, both read once per upload (D10, G2).
/// </summary>
public sealed record KpiTargetSheetResult(
    IReadOnlyList<string> Headers,
    IReadOnlyList<RawRow> Rows,
    IReadOnlyDictionary<string, decimal> QuarterRatios,
    string? BranchCodeRaw);
