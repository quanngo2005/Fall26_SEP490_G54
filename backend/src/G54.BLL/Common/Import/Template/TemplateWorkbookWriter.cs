using ClosedXML.Excel;

namespace G54.BLL.Common.Import.Template;

/// <summary>Generates a downloadable .xlsx template from the same declaration used to map uploads.</summary>
public static class TemplateWorkbookWriter
{
    public static byte[] Write<TRow>(ImportTemplate<TRow> template)
        where TRow : new() => Write(template, null, null);

    public static byte[] Write<TRow>(
        ImportTemplate<TRow> template,
        Action<IXLWorksheet>? writeExampleRow,
        IReadOnlyList<(string Column, string Required, string Type, string Description)>? notes)
        where TRow : new()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Data");

        for (var i = 0; i < template.Columns.Count; i++)
        {
            var column = template.Columns[i];
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = column.Header;
            cell.Style.Font.Bold = true;

            if (column.DropdownValues is { Count: > 0 })
            {
                var validation = worksheet.Range(2, i + 1, 1001, i + 1).CreateDataValidation();
                validation.List(string.Join(",", column.DropdownValues), true);
            }
        }

        writeExampleRow?.Invoke(worksheet);

        worksheet.Columns(1, template.Columns.Count).AdjustToContents();

        if (notes is { Count: > 0 })
        {
            var notesSheet = workbook.Worksheets.Add("Notes");
            notesSheet.Cell(1, 1).Value = "Cột";
            notesSheet.Cell(1, 2).Value = "Bắt buộc";
            notesSheet.Cell(1, 3).Value = "Kiểu dữ liệu";
            notesSheet.Cell(1, 4).Value = "Ghi chú";
            notesSheet.Row(1).Style.Font.Bold = true;

            for (var r = 0; r < notes.Count; r++)
            {
                var item = notes[r];
                notesSheet.Cell(r + 2, 1).Value = item.Column;
                notesSheet.Cell(r + 2, 2).Value = item.Required;
                notesSheet.Cell(r + 2, 3).Value = item.Type;
                notesSheet.Cell(r + 2, 4).Value = item.Description;
            }

            notesSheet.Columns(1, 4).AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
