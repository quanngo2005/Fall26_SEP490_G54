using G54.BLL.Common.Constants;
using G54.BLL.Common.Import.Pipeline;
using G54.BLL.Common.Import.Rules;

namespace G54.BLL.Services.Import.EmployeeOrganization.Rules;

/// <summary>Rule 6: an email already used by a different employee in the DB is rejected.</summary>
public sealed class EmployeeOrgEmailInUseRule : RowRule<EmployeeOrgRow>
{
    public override int Order => -10;

    protected override void ValidateRow(ImportRow<EmployeeOrgRow> row, ImportValidationContext<EmployeeOrgRow> context)
    {
        var data = row.Data;
        if (data.Email is null)
        {
            return;
        }

        var ctx = (EmployeeOrgImportContext)context.Context!;
        if (ctx.EmployeesByEmail.TryGetValue(data.Email, out var existing) &&
            !string.Equals(existing.EmployeeCode, data.EmployeeCode, StringComparison.OrdinalIgnoreCase))
        {
            row.AddError("Email*", ErrorCodes.DataImportEmailInUse, $"Email '{data.Email}' đã thuộc nhân viên khác.");
            return;
        }

        // New accounts use the email as username; an account of another employee already holding it would
        // violate uq_account_username at commit time.
        if (ctx.AccountsByUsername.TryGetValue(data.Email, out var account) &&
            (!ctx.EmployeesByCode.TryGetValue(data.EmployeeCode ?? string.Empty, out var owner) || owner.Id != account.EmployeeId))
        {
            row.AddError("Email*", ErrorCodes.DataImportEmailInUse, $"Email '{data.Email}' đã được dùng làm tên đăng nhập của tài khoản khác.");
        }
    }
}
