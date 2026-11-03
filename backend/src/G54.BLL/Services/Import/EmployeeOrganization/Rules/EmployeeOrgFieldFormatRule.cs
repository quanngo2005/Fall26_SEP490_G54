using G54.BLL.Common.Constants;
using G54.BLL.Common.Import.Parsing;
using G54.BLL.Common.Import.Pipeline;
using G54.BLL.Common.Import.Rules;
using G54.DAL.Entities;

namespace G54.BLL.Services.Import.EmployeeOrganization.Rules;

/// <summary>
/// Parses and normalizes every cell on its own row: codes (UPPER, regex), email (lower-case), position and
/// status enums, hire date, and max lengths. Fills the row's Norm/typed properties on success so later rules
/// never re-parse raw text. Runs before every other rule (Order -90, right after RequiredRule at -100).
/// </summary>
public sealed class EmployeeOrgFieldFormatRule : RowRule<EmployeeOrgRow>
{
    public override int Order => -90;

    protected override void ValidateRow(ImportRow<EmployeeOrgRow> row, ImportValidationContext<EmployeeOrgRow> context)
    {
        var data = row.Data;

        ParseCode(row, "Mã chi nhánh*", data.BranchCodeRaw, value => data.BranchCode = value);
        ParseCode(row, "Mã phòng ban*", data.DeptCodeRaw, value => data.DeptCode = value);
        ParseCode(row, "Mã NV*", data.EmployeeCodeRaw, value => data.EmployeeCode = value);

        if (!string.IsNullOrWhiteSpace(data.ManagerCodeRaw))
        {
            ParseCode(row, "Mã quản lý trực tiếp", data.ManagerCodeRaw, value => data.ManagerCode = value);
        }

        // "Đơn vị cha" blank defaults to the row's own branch code (checked once BranchCode above is known valid).
        var deptParentRaw = string.IsNullOrWhiteSpace(data.DeptParentCodeRaw) ? data.BranchCodeRaw : data.DeptParentCodeRaw;
        if (!string.IsNullOrWhiteSpace(deptParentRaw))
        {
            ParseCode(row, "Đơn vị cha", deptParentRaw, value => data.DeptParentCode = value);
        }

        if (!string.IsNullOrWhiteSpace(data.BranchParentCodeRaw))
        {
            row.AddError("Mã CN cha", ErrorCodes.DataImportBranchHasParent, "Chi nhánh không được có chi nhánh cha.");
        }

        if (!CellParsers.TryParseEmail(data.EmailRaw, out var email))
        {
            if (!string.IsNullOrWhiteSpace(data.EmailRaw))
            {
                row.AddError("Email*", ErrorCodes.DataImportInvalidEmail, $"Email '{data.EmailRaw}' không đúng định dạng.");
            }
        }
        else
        {
            data.Email = email;
        }

        if (!string.IsNullOrWhiteSpace(data.PositionRaw))
        {
            if (PositionMapping.IsKnown(data.PositionRaw))
            {
                data.Position = data.PositionRaw.ToUpperInvariant();
            }
            else
            {
                row.AddError("Vị trí*", ErrorCodes.DataImportInvalidPosition, $"Vị trí '{data.PositionRaw}' không hợp lệ.");
            }
        }

        if (!string.IsNullOrWhiteSpace(data.StatusRaw))
        {
            if (CellParsers.TryParseEnum<EmployeeStatus>(data.StatusRaw, out var status) && status != EmployeeStatus.Inactive)
            {
                data.Status = status;
            }
            else
            {
                row.AddError("Trạng thái*", ErrorCodes.DataImportInvalidStatus, $"Trạng thái '{data.StatusRaw}' không hợp lệ.");
            }
        }

        if (!string.IsNullOrWhiteSpace(data.HireDateRaw))
        {
            if (CellParsers.TryParseDate(data.HireDateRaw, out var hireDate) && hireDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            {
                data.HireDate = hireDate;
            }
            else
            {
                row.AddError("Ngày vào làm*", ErrorCodes.DataImportInvalidDate, $"Ngày vào làm '{data.HireDateRaw}' không hợp lệ.");
            }
        }

        CheckMaxLength(row, "Tên chi nhánh*", data.BranchNameRaw, 200);
        CheckMaxLength(row, "Tên phòng ban*", data.DeptNameRaw, 200);
        CheckMaxLength(row, "Họ tên*", data.FullNameRaw, 200);
    }

    private static void ParseCode(ImportRow<EmployeeOrgRow> row, string column, string? raw, Action<string> onValid)
    {
        if (CellParsers.TryParseCode(raw, out var normalized))
        {
            onValid(normalized);
        }
        else if (!string.IsNullOrWhiteSpace(raw))
        {
            row.AddError(column, ErrorCodes.DataImportInvalidCode, $"'{column}' có giá trị '{raw}' không hợp lệ.");
        }
    }

    private static void CheckMaxLength(ImportRow<EmployeeOrgRow> row, string column, string? value, int max)
    {
        if (!string.IsNullOrEmpty(value) && value.Length > max)
        {
            row.AddError(column, ErrorCodes.DataImportMaxLengthExceeded, $"'{column}' vượt quá {max} ký tự.");
        }
    }
}
