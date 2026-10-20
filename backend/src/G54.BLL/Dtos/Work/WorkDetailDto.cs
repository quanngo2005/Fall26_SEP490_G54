namespace G54.BLL.Dtos.Work;

/// <summary>
/// DTO thông tin chi tiết công việc (Work).
/// </summary>
/// <param name="Id">Id định danh duy nhất của công việc (UUID).</param>
/// <param name="Code">Mã công việc do hệ thống tự động sinh theo định dạng 'WRK-yyyyMMdd-####' (hoặc hậu tố 6 ký tự hex nếu xảy ra trùng lặp).</param>
/// <param name="Title">Tiêu đề công việc.</param>
/// <param name="Description">Mô tả chi tiết nội dung công việc (nullable).</param>
/// <param name="WorkTypeId">Id loại công việc (nullable khi không gắn loại công việc).</param>
/// <param name="WorkTypeName">Tên loại công việc (nullable khi không gắn loại công việc).</param>
/// <param name="BusinessCampaignId">Id chiến dịch kinh doanh liên kết (nullable khi không gắn chiến dịch).</param>
/// <param name="BusinessCampaignName">Tên chiến dịch kinh doanh liên kết (nullable khi không gắn chiến dịch).</param>
/// <param name="AssigneeEmployeeId">Id nhân viên được phân công thực hiện công việc.</param>
/// <param name="AssigneeEmployeeName">Họ và tên nhân viên được phân công (nullable).</param>
/// <param name="AssigneeEmployeeCode">Mã nhân viên được phân công (nullable).</param>
/// <param name="Priority">Mức độ ưu tiên của công việc, trả về dạng PascalCase ('Low', 'Medium', 'High', 'Critical').</param>
/// <param name="Status">Trạng thái công việc, trả về dạng PascalCase ('New', 'InProgress', 'HandedOver', 'Submitted', 'Verified', 'Rejected', 'Done', 'Cancelled'). Khi khởi tạo luôn là 'New'.</param>
/// <param name="PlannedStartDate">Ngày dự kiến bắt đầu công việc theo định dạng yyyy-MM-dd (nullable).</param>
/// <param name="PlannedEndDate">Hạn hoàn thành dự kiến của công việc theo định dạng yyyy-MM-dd (nullable).</param>
/// <param name="EstimatedHours">Số giờ ước tính hoàn thành công việc (nullable).</param>
/// <param name="UpdatedAt">Thời điểm cập nhật cuối cùng theo chuẩn ISO 8601 có múi giờ (UTC offset).</param>
public sealed record WorkDetailDto(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    Guid? WorkTypeId,
    string? WorkTypeName,
    Guid? BusinessCampaignId,
    string? BusinessCampaignName,
    Guid AssigneeEmployeeId,
    string? AssigneeEmployeeName,
    string? AssigneeEmployeeCode,
    string Priority,
    string Status,
    DateOnly? PlannedStartDate,
    DateOnly? PlannedEndDate,
    decimal? EstimatedHours,
    DateTimeOffset UpdatedAt);
