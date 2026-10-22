namespace G54.BLL.Common.Constants;

public static class ErrorCodes
{
    // Generic & HTTP Errors
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string Conflict = "CONFLICT";
    public const string InternalError = "INTERNAL_ERROR";

    // My Profile (docs/api/MY_PROFILE_CONTRACT.md)
    public const string InvalidCurrentPassword = "INVALID_CURRENT_PASSWORD";

    // Submission / Verification / KPI Result lifecycle (docs/api/SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md)
    public const string SubmissionInvalidState = "SUBMISSION_INVALID_STATE";
    public const string VerificationInvalidState = "VERIFICATION_INVALID_STATE";
    public const string KpiResultNotFound = "KPI_RESULT_NOT_FOUND";

    // Work lifecycle (docs/api/WORK_CONTRACT.md)
    public const string WorkInvalidState = "WORK_INVALID_STATE";

    // Performance evaluation (docs/api/PERFORMANCE_EVALUATION_CONTRACT.md)
    public const string PerformanceEvaluationInvalidState = "PERFORMANCE_EVALUATION_INVALID_STATE";

    // Evaluation & Calibration lifecycle (docs/api/CALIBRATION_APPROVAL_CONTRACT.md)
    public const string TierAQuotaExceeded = "TIER_A_QUOTA_EXCEEDED";

    // Application Message Codes (COMMON_REQUIREMENTS_AND_MESSAGES_SPECIFICATION.md)
    public const string MSG01 = "MSG01"; // Không tìm thấy kết quả phù hợp
    public const string MSG02 = "MSG02"; // Trường (*) là bắt buộc nhập
    public const string MSG03 = "MSG03"; // Cập nhật thành công
    public const string MSG04 = "MSG04"; // Tạo mới thành công
    public const string MSG05 = "MSG05"; // Gửi yêu cầu bàn giao thành công
    public const string MSG06 = "MSG06"; // Nộp kết quả công việc thành công
    public const string MSG07 = "MSG07"; // Xóa thành công
    public const string MSG08 = "MSG08"; // Độ dài vượt quá giới hạn
    public const string MSG09 = "MSG09"; // Sai thông tin đăng nhập
    public const string MSG10 = "MSG10"; // Maker tự chọn mình làm Approver (BR-02)
    public const string MSG11 = "MSG11"; // Giao KH VIP cho nhân sự chưa có chứng chỉ VIP (BR-03)
    public const string MSG12 = "MSG12"; // Cấu hình ủy quyền tạo vòng lặp vô tận (BR-04)
    public const string MSG13 = "MSG13"; // Ánh xạ kiểu chỉ số không tương thích (BR-09)
    public const string MSG14 = "MSG14"; // Tổng % chỉ tiêu chiến dịch != 100% (BR-24)
    public const string MSG17 = "MSG17"; // Nộp trễ hạn trong thời gian gia hạn 5 phút (BR-12)
    public const string MSG18 = "MSG18"; // Nộp quá thời gian gia hạn chiến dịch (BR-12)
    public const string MSG22 = "MSG22"; // Trùng lặp điểm KPI cho 1 Task ID (BR-08)
    public const string MSG23 = "MSG23"; // Sửa đổi minh chứng đã duyệt bị từ chối (BR-13)
    public const string MSG28 = "MSG28"; // Trừ điểm CLDV thiếu Timestamp (BR-19)
    public const string MSG29 = "MSG29"; // Chuyển phòng ban cho nhân sự còn việc tồn (BR-23)

    private static readonly Dictionary<string, (string Vi, string En)> DefaultMessages = new()
    {
        [ValidationError] = ("Dữ liệu đầu vào không hợp lệ.", "Input validation failed."),
        [NotFound] = ("Không tìm thấy tài nguyên yêu cầu.", "Requested resource was not found."),
        [Unauthorized] = ("Phiên làm việc không hợp lệ hoặc đã hết hạn.", "Unauthorized or session expired."),
        [Forbidden] = ("Bạn không có quyền thực hiện thao tác này.", "Access forbidden."),
        [Conflict] = ("Dữ liệu bị xung đột hoặc đã tồn tại.", "Resource conflict or already exists."),
        [InternalError] = ("Đã xảy ra lỗi hệ thống không mong muốn.", "An unexpected system error occurred."),
        [InvalidCurrentPassword] = ("Mật khẩu hiện tại không đúng.", "The current password is incorrect."),
        [SubmissionInvalidState] = ("Trạng thái hiện tại không cho phép thao tác với báo cáo kết quả.", "The current state does not allow this submission action."),
        [VerificationInvalidState] = ("Hồ sơ không ở trạng thái chờ thẩm định.", "The submission is not awaiting verification."),
        [KpiResultNotFound] = ("Chưa có kết quả KPI đã được thẩm định.", "No verified KPI result was found."),
        [WorkInvalidState] = ("Trạng thái hiện tại của công việc không cho phép chuyển sang trạng thái yêu cầu.", "The current work status does not allow the requested transition."),
        [PerformanceEvaluationInvalidState] = ("Trạng thái đánh giá hoặc kỳ đánh giá không cho phép tính điểm KPI.", "The evaluation or its period does not allow KPI score calculation."),
        [MSG01] = ("Không tìm thấy kết quả phù hợp.", "No search results."),
        [MSG02] = ("Trường (*) là bắt buộc nhập.", "The * field is required."),
        [MSG03] = ("Cập nhật thành công.", "Update successfully."),
        [MSG04] = ("Tạo mới thành công.", "Create successfully."),
        [MSG05] = ("Gửi yêu cầu bàn giao thành công.", "Handover request sent successfully."),
        [MSG06] = ("Nộp kết quả công việc thành công. Đang chờ thẩm định.", "Work result submitted successfully. Awaiting verification."),
        [MSG07] = ("Xóa thành công.", "Delete successfully."),
        [MSG08] = ("Độ dài giá trị nhập vượt quá quy định.", "Input length exceeded allowed limit."),
        [MSG09] = ("Tên đăng nhập hoặc mật khẩu không chính xác. Vui lòng kiểm tra lại.", "Incorrect user name or password. Please check again."),
        [MSG10] = ("Lỗi kiểm tra: Người thực hiện (Maker) không được phép tự phân công mình làm người phê duyệt (Checker).", "Validation Error: Maker cannot assign self as Checker/Approver for this task."),
        [MSG11] = ("Lỗi kiểm tra: Nhân sự được phân công chưa có chứng chỉ VIP_Trained phục vụ Khách hàng Ưu tiên.", "Validation Error: Assigned employee lacks VIP_Trained certification required for Priority Customers."),
        [MSG12] = ("Lỗi cấu hình: Lộ trình ủy quyền không hợp lệ. Nghiêm cấm tạo vòng lặp ủy quyền.", "Validation Error: Invalid delegation path. Circular delegation loops are not permitted."),
        [MSG13] = ("Lỗi ánh xạ: Kiểu chỉ số không đồng nhất (Định lượng vs Định tính).", "Mapping Error: Metric types do not match (Quantitative vs Qualitative)."),
        [MSG14] = ("Lỗi kiểm tra: Tổng tỷ lệ chỉ tiêu phân bổ bắt buộc phải bằng chính xác 100%.", "Validation Error: Total allocated target percentage must sum to exactly 100%."),
        [MSG17] = ("Nộp trễ hạn: Đã nhận trong thời gian gia hạn 5 phút. Chuyển sang luồng Thẩm định Nộp trễ.", "Late Submission: Received within 5-minute grace period. Routed for Late Submission Review."),
        [MSG18] = ("Từ chối nộp: Thời hạn chiến dịch đã kết thúc.", "Submission Rejected: Campaign deadline expired."),
        [MSG22] = ("Lỗi Xung đột: Công việc ID đã đóng góp KPI vào hệ thống trước đó.", "Conflict Error: Task ID has already contributed KPI to the system."),
        [MSG23] = ("Truy cập bị từ chối: Tệp tài liệu minh chứng đã bị khóa vĩnh viễn sau khi phê duyệt.", "Access Denied: Evidence attachments are permanently locked post-approval."),
        [MSG28] = ("Lỗi kiểm tra: Bắt buộc phải cung cấp Dấu thời gian (Timestamp) minh chứng.", "Validation Error: Media Timestamp proof is required."),
        [MSG29] = ("Lỗi kiểm tra: Chặn chuyển phòng ban. Nhân viên hiện còn công việc chưa hoàn tất.", "Validation Error: Department transfer blocked. Employee has active task(s)."),
        [TierAQuotaExceeded] = ("Tỷ lệ phân bổ xếp loại Tier A vượt quá hạn ngạch tối đa cho phép của đơn vị (BR-37).", "Tier A distribution exceeds department quota cap (BR-37)."),
    };

    public static string GetDefaultMessage(string code, bool english = false)
    {
        if (DefaultMessages.TryGetValue(code, out var messagePair))
        {
            return english ? messagePair.En : messagePair.Vi;
        }

        return english ? "An error occurred." : "Đã xảy ra lỗi.";
    }
}
