using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Validation;
using G54.BLL.Dtos.Profile;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class ProfileService(AppDbContext db, ILogger<ProfileService> logger) : IProfileService
{
    private static readonly Action<ILogger, Guid, Exception?> LogPhoneChanged =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(1, "ProfilePhoneChanged"),
            "Profile audit: account {AccountId} changed their phone number");

    public async Task<MyPhoneDto> UpdatePhoneAsync(
        Guid accountId,
        UpdateMyProfileDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var phone = PhoneRules.Normalize(request.Phone);
        if (phone is not null && !PhoneRules.IsValid(phone))
        {
            throw new ValidationException("Số điện thoại phải gồm đúng 10 số và bắt đầu bằng 0.");
        }

        var account = await LoadAsync(accountId, cancellationToken);
        if (!string.Equals(account.Employee.Phone, phone, StringComparison.Ordinal))
        {
            // LastSyncAt belongs to the HR sync and is deliberately left alone.
            account.Employee.Phone = phone;
            account.Employee.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            LogPhoneChanged(logger, accountId, null);
        }

        return new MyPhoneDto(account.Employee.Phone);
    }

    private async Task<Account> LoadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts
            .Include(a => a.Employee)
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken)
            ?? throw new NotFoundException("Tài khoản", accountId);

        if (account.IsLocked || account.Employee.Status != EmployeeStatus.Active)
        {
            throw new ForbiddenException();
        }

        return account;
    }
}
