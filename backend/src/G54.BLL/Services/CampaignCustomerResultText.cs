using System.Globalization;
using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>
/// Derives the human-readable <c>resultText</c> for a <see cref="CampaignCustomer"/> row
/// (docs/api/CAMPAIGN_CONTRACT.md §5.2). Never stored verbatim; computed on every read so Phase 4 (the employee
/// "my customers" / contact update screen) can reuse the exact same text as the manager list.
/// </summary>
public static class CampaignCustomerResultText
{
    public static string Compute(
        CampaignContactStatus status,
        bool? callAnswered,
        int missedCallCount,
        DateTimeOffset? appointmentAt,
        DateTimeOffset? metAt,
        string? closedWorkTypeName)
    {
        if (status == CampaignContactStatus.Closed)
        {
            return $"Đã chốt được sản phẩm {closedWorkTypeName}";
        }

        if (status == CampaignContactStatus.Met)
        {
            return $"Đã gặp vào {FormatDateTime(metAt)}";
        }

        if (status == CampaignContactStatus.AppointmentSet)
        {
            return $"Đã hẹn gặp vào {FormatDateTime(appointmentAt)}";
        }

        if (status == CampaignContactStatus.Called && callAnswered == true)
        {
            return "Gọi thành công";
        }

        if (missedCallCount > 0)
        {
            return $"Không nghe máy {missedCallCount} lần";
        }

        return "Chưa liên hệ";
    }

    /// <summary>Vietnam has no daylight saving, so a fixed UTC+7 offset is exact. Times are stored in UTC.</summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private static string FormatDateTime(DateTimeOffset? value) =>
        value?.ToOffset(VietnamOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
}
