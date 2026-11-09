using System.IO.Compression;
using ClosedXML.Excel;
using G54.BLL.Common.Exceptions;

namespace G54.BLL.Common.Import.Reading;

/// <summary>Reads the first worksheet of an .xlsx workbook with ClosedXML into generic <see cref="RawRow"/>s.</summary>
public sealed class ClosedXmlSheetReader : IExcelSheetReader
{
    // .xlsx is a zip; a small upload can inflate to gigabytes once ClosedXML loads it (zip bomb).
    private const long MaxUncompressedBytes = 100L * 1024 * 1024;

    public ExcelSheetResult Read(Stream excel, int maxRows)
    {
        EnsureUncompressedSizeWithinLimit(excel);
        using var workbook = new XLWorkbook(excel);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new ValidationException("File Excel không có worksheet nào.");

        var lastColumn = worksheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
        if (lastColumn == 0)
        {
            throw new ValidationException("File Excel không có dòng tiêu đề (header).");
        }

        var headers = new List<string>(lastColumn);
        for (var col = 1; col <= lastColumn; col++)
        {
            headers.Add(worksheet.Cell(1, col).GetString());
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var rows = new List<RawRow>();
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var cells = new List<string?>(lastColumn);
            var isBlank = true;
            for (var col = 1; col <= lastColumn; col++)
            {
                var cell = worksheet.Cell(rowNumber, col);
                var text = cell.DataType == XLDataType.DateTime
                    ? cell.GetDateTime().ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
                    : cell.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    isBlank = false;
                }

                cells.Add(text);
            }

            if (isBlank)
            {
                continue;
            }

            if (rows.Count >= maxRows)
            {
                throw new ValidationException($"File vượt quá số dòng tối đa cho phép ({maxRows}).");
            }

            rows.Add(new RawRow(rowNumber, cells));
        }

        return new ExcelSheetResult(headers, rows);
    }

    private static void EnsureUncompressedSizeWithinLimit(Stream excel)
    {
        if (!excel.CanSeek)
        {
            throw new ValidationException("File Excel không đọc được.");
        }

        var start = excel.Position;
        try
        {
            using var zip = new ZipArchive(excel, ZipArchiveMode.Read, leaveOpen: true);
            var total = zip.Entries.Sum(e => e.Length);
            if (total > MaxUncompressedBytes)
            {
                throw new ValidationException("File Excel quá lớn sau khi giải nén.");
            }
        }
        catch (InvalidDataException)
        {
            throw new ValidationException("File không phải định dạng .xlsx hợp lệ.");
        }
        finally
        {
            excel.Position = start;
        }
    }
}
