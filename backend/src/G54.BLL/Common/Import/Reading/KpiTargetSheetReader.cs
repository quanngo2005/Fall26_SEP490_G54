using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;

namespace G54.BLL.Common.Import.Reading;

/// <summary>
/// Reads the year-target sheet of the workbook (docs/design/BRANCH_YEAR_KPI_IMPORT_DESIGN.md, section 3.1).
/// The layout is located, not fixed: the sheet is the first one with a recognisable header row, columns are
/// matched by header aliases (ignoring case, accents, extra spaces, "(...)" notes, "*" and ":"), the quarter
/// ratio row is the nearest row above the header with numeric Q1-Q4 cells, and any row with a value in the KPI
/// code column is a data row. Formula cells use the value Excel last cached - re-evaluating them is unreliable
/// across sheets.
/// </summary>
public static partial class KpiTargetSheetReader
{
    /// <summary>A formula cell with no cached value is reported as this row's raw text, never as 0 (section 1.3).</summary>
    public const string FormulaNotCalculatedMarker = "#FORMULA_NOT_CALCULATED#";

    private const int MaxHeaderScanRows = 20;
    private const string BranchCodeLabel = "ma chi nhanh";

    // Canonical headers of the ExcelSheetResult this reader produces; BranchYearTargetImportDefinition's
    // template matches these by exact text, independent of how each concept is titled in the source file.
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

    /// <summary>Accepted header texts per canonical column, already normalized (see <see cref="Normalize"/>).
    /// The two year columns are matched separately because their header depends on the import year.</summary>
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        [KpiCodeColumn] = ["ma kpi", "ma chi tieu", "kpi code"],
        [KpiNameColumn] = ["ten kpi", "ten chi tieu", "kpi name"],
        [UnitColumn] = ["don vi", "don vi tinh", "dvt", "unit"],
        [DirectionColumn] = ["chieu", "chieu huong", "direction"],
        [WeightColumn] = ["trong so", "weight"],
        [Q1Column] = ["q1", "quy 1", "quy i"],
        [Q2Column] = ["q2", "quy 2", "quy ii"],
        [Q3Column] = ["q3", "quy 3", "quy iii"],
        [Q4Column] = ["q4", "quy 4", "quy iv"],
        [AggregationColumn] = ["tong hop", "aggregation"],
    };

    private static readonly string[] CanonicalHeaders =
    [
        KpiCodeColumn, KpiNameColumn, UnitColumn, DirectionColumn, WeightColumn,
        PreviousYearColumn, YearColumn, Q1Column, Q2Column, Q3Column, Q4Column, AggregationColumn,
    ];

    private static readonly string[] QuarterColumns = [Q1Column, Q2Column, Q3Column, Q4Column];

    public static KpiTargetSheetResult Read(Stream excel, int year, int maxRows)
    {
        ExcelUploadGuard.EnsureUncompressedSizeWithinLimit(excel);
        using var workbook = new XLWorkbook(excel);
        if (workbook.Worksheets.Count == 0)
        {
            throw new ValidationException("File Excel không có worksheet nào.");
        }

        var (worksheet, headerRow) = workbook.Worksheets
            .Select(sheet => (Sheet: sheet, Row: FindHeaderRow(sheet)))
            .FirstOrDefault(found => found.Row is not null);
        if (worksheet is null || headerRow is not { } headerRowNumber)
        {
            throw new BusinessRuleException(ErrorCodes.ImportTemplateInvalid, "File không đúng mẫu quy định (không tìm thấy cột 'Mã KPI').", 400);
        }

        var sourceColumns = MapSourceColumns(worksheet, headerRowNumber, year);
        var quarterRatios = ReadQuarterRatios(worksheet, headerRowNumber, sourceColumns);
        var branchCodeRaw = ReadBranchCode(worksheet, headerRowNumber);

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRowNumber;
        var rows = new List<RawRow>();
        for (var rowNumber = headerRowNumber + 1; rowNumber <= lastRow; rowNumber++)
        {
            if (string.IsNullOrWhiteSpace(GetCellText(worksheet.Cell(rowNumber, sourceColumns[KpiCodeColumn]))))
            {
                continue; // group header, total row or note - no KPI code, so not a data row
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
    /// The header row has both a KPI-code and a Q1 header. The sample file also has a merged sub-header row
    /// above it ("Mã KPI" over the code/name columns, section 1 item 1) with no Q1, which this must skip.
    /// </summary>
    private static int? FindHeaderRow(IXLWorksheet worksheet)
    {
        var lastColumn = LastColumn(worksheet);
        for (var row = 1; row <= MaxHeaderScanRows; row++)
        {
            var texts = Enumerable.Range(1, lastColumn).Select(col => Normalize(worksheet.Cell(row, col).GetString())).ToList();
            if (texts.Any(Aliases[KpiCodeColumn].Contains) && texts.Any(Aliases[Q1Column].Contains))
            {
                return row;
            }
        }

        return null;
    }

    private static Dictionary<string, int> MapSourceColumns(IXLWorksheet worksheet, int headerRow, int year)
    {
        var byText = new Dictionary<string, int>();
        for (var col = 1; col <= LastColumn(worksheet); col++)
        {
            var text = Normalize(worksheet.Cell(headerRow, col).GetString());
            if (text.Length > 0)
            {
                byText.TryAdd(text, col);
            }
        }

        int? Find(IEnumerable<string> aliases) =>
            aliases.Select(alias => byText.TryGetValue(alias, out var col) ? col : (int?)null).FirstOrDefault(col => col is not null);

        var columns = new Dictionary<string, int>();
        foreach (var (canonical, aliases) in Aliases)
        {
            if (Find(aliases) is { } col)
            {
                columns[canonical] = col;
            }
        }

        columns[YearColumn] = Find(YearAliases(year)) ?? throw new BusinessRuleException(
            ErrorCodes.TargetImportYearColumnMissing,
            $"File không có cột 'Năm {year}' của năm cần nhập.",
            400);

        if (Find(YearAliases(year - 1)) is { } previousYearCol)
        {
            columns[PreviousYearColumn] = previousYearCol;
        }

        return columns;
    }

    private static string[] YearAliases(int year) => [$"nam {year}", $"{year}"];

    /// <summary>Nearest row above the header whose Q1-Q4 cells are all numeric (D10); none means no file ratios.</summary>
    private static Dictionary<string, decimal> ReadQuarterRatios(
        IXLWorksheet worksheet, int headerRow, Dictionary<string, int> sourceColumns)
    {
        if (!QuarterColumns.All(sourceColumns.ContainsKey))
        {
            return [];
        }

        for (var row = headerRow - 1; row >= 1; row--)
        {
            var ratios = new Dictionary<string, decimal>();
            foreach (var quarter in QuarterColumns)
            {
                var text = GetCellText(worksheet.Cell(row, sourceColumns[quarter]));
                if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
                {
                    ratios[quarter] = ratio;
                }
            }

            if (ratios.Count == QuarterColumns.Length)
            {
                return ratios;
            }
        }

        return [];
    }

    /// <summary>Optional <c>Mã chi nhánh:</c> label anywhere above the header, branch code in the next cell (G2).</summary>
    private static string? ReadBranchCode(IXLWorksheet worksheet, int headerRow)
    {
        var lastColumn = LastColumn(worksheet);
        for (var row = 1; row < headerRow; row++)
        {
            for (var col = 1; col <= lastColumn; col++)
            {
                if (Normalize(worksheet.Cell(row, col).GetString()).StartsWith(BranchCodeLabel, StringComparison.Ordinal))
                {
                    var next = GetCellText(worksheet.Cell(row, col + 1))?.Trim();
                    return string.IsNullOrWhiteSpace(next) ? null : next;
                }
            }
        }

        return null;
    }

    private static int LastColumn(IXLWorksheet worksheet) => worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;

    /// <summary>Lower-case, accent-free, single-spaced text without "(...)" notes, "*" or ":", so
    /// "Mã KPI (*)", "MA  KPI" and "Mã KPI:" all compare equal.</summary>
    private static string Normalize(string text)
    {
        var stripped = NoteOrMarker().Replace(text, " ").Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(stripped.Length);
        foreach (var ch in stripped)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return Whitespace().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\([^)]*\)|[*:]")]
    private static partial Regex NoteOrMarker();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

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
/// column letters), plus the two things no other import needs: the file's quarter split ratios and an
/// optional branch code cell, both read once per upload (D10, G2).
/// </summary>
public sealed record KpiTargetSheetResult(
    IReadOnlyList<string> Headers,
    IReadOnlyList<RawRow> Rows,
    IReadOnlyDictionary<string, decimal> QuarterRatios,
    string? BranchCodeRaw);
