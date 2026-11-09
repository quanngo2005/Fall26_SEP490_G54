using ClosedXML.Excel;

namespace G54.BLL.Common.Import.Template;

/// <summary>Generates a downloadable .xlsx template from the same declaration used to map uploads.</summary>
public static class TemplateWorkbookWriter
{
    public static byte[] Write<TRow>(ImportTemplate<TRow> template)
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

        worksheet.Columns(1, template.Columns.Count).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
