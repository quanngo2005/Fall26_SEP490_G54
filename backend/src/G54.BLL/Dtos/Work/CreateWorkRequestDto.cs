using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Work;

/// <summary>
/// DTO yêu cầu khởi tạo và phân công công việc mới (POST /works).
/// </summary>
/// <param name="Title">Tiêu đề công việc, bắt buộc, từ 5 đến 255 ký tự.</param>
/// <param name="WorkTypeId">Id loại công việc, bắt buộc, phải tồn tại và đang Active. Nếu không hợp lệ sẽ trả về lỗi 400 (VALIDATION_ERROR).</param>
/// <param name="AssigneeEmployeeId">Id nhân viên được phân công, bắt buộc, phải tồn tại và đang Active. Nếu gắn chiến dịch thì nhân viên phải là Participant của chiến dịch đó. Nếu không hợp lệ sẽ trả về lỗi 400 (VALIDATION_ERROR).</param>
/// <param name="Priority">Mức độ ưu tiên, mặc định là 'Medium'. Chấp nhận: Low, Medium, High, Critical và các bí danh P1 (Critical), P2 (High) (không phân biệt hoa/thường). Mọi giá trị khác được âm thầm chuyển về Medium mà không báo lỗi.</param>
/// <param name="PlannedStartDate">Ngày bắt đầu dự kiến, định dạng yyyy-MM-dd. Nếu bỏ trống thì mặc định lấy ngày hiện tại theo giờ UTC.</param>
/// <param name="PlannedEndDate">Hạn hoàn thành dự kiến, định dạng yyyy-MM-dd. Không được trước PlannedStartDate. Nếu có BusinessCampaignId thì không được vượt quá ngày kết thúc (EndDate) của chiến dịch.</param>
/// <param name="EstimatedHours">Số giờ ước tính thực hiện công việc, giá trị từ 0 đến 9999.99.</param>
/// <param name="BusinessCampaignId">Id chiến dịch kinh doanh liên kết (tùy chọn). Nếu truyền thì chiến dịch phải tồn tại (nếu không sẽ trả về lỗi 404 NOT_FOUND) và phải ở trạng thái Running hoặc Planned.</param>
/// <param name="Description">Mô tả chi tiết nội dung công việc, tối đa 2000 ký tự.</param>
/// <param name="ChecklistItems">Danh sách các đầu việc con cần thực hiện. Lưu ý: Hiện tại server chưa xử lý và bỏ qua trường này.</param>
public sealed record CreateWorkRequestDto(
    [Required]
    [StringLength(255, MinimumLength = 5)]
    string Title,

    [Required]
    Guid WorkTypeId,

    [Required]
    Guid AssigneeEmployeeId,

    string? Priority = "Medium",

    DateOnly? PlannedStartDate = null,

    DateOnly? PlannedEndDate = null,

    [Range(0, 9999.99)]
    decimal? EstimatedHours = null,

    Guid? BusinessCampaignId = null,

    [StringLength(2000)]
    string? Description = null,

    IReadOnlyList<string>? ChecklistItems = null);
