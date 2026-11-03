using G54.BLL.Common.Constants;
using G54.BLL.Common.Import.Pipeline;

namespace G54.BLL.Common.Import.Rules;

/// <summary>Reports <see cref="ErrorCodes.RequiredField"/> when the selected field is blank.</summary>
public sealed class RequiredRule<TRow>(string column, Func<TRow, string?> getter) : RowRule<TRow>
{
    public override int Order => -100;

    protected override void ValidateRow(ImportRow<TRow> row, ImportValidationContext<TRow> context)
    {
        if (string.IsNullOrWhiteSpace(getter(row.Data)))
        {
            row.AddError(column, ErrorCodes.RequiredField, $"'{column}' là bắt buộc nhập.");
        }
    }
}
