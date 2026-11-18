namespace G54.BLL.Common.Import.Reading;

/// <summary>One data row read from an Excel sheet, cells in file column order (0-based).</summary>
public sealed record RawRow(int RowNumber, IReadOnlyList<string?> Cells);

/// <summary>Header row plus data rows of the first worksheet, independent of any business template.</summary>
public sealed record ExcelSheetResult(IReadOnlyList<string> Headers, IReadOnlyList<RawRow> Rows);
