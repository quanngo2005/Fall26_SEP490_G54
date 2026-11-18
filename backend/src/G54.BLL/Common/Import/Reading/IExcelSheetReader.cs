namespace G54.BLL.Common.Import.Reading;

/// <summary>
/// Adapter that isolates the import engine from the Excel library used to read uploaded workbooks.
/// </summary>
public interface IExcelSheetReader
{
    /// <summary>
    /// Reads the first worksheet: row 1 is the header, data starts at row 2. Rows that are completely
    /// blank are skipped. Throws <see cref="G54.BLL.Common.Exceptions.ValidationException"/> when the
    /// file has no worksheet or no header row, and when the row count exceeds <paramref name="maxRows"/>.
    /// </summary>
    ExcelSheetResult Read(Stream excel, int maxRows);
}
