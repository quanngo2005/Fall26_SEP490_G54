namespace G54.BLL.Common.Constants;

/// <summary>
/// Stable error codes returned in <c>errorCode</c> of the API error envelope. Every constant must have an entry in
/// <see cref="DefaultMessages"/> (enforced by tests). Non-error outcomes live in <see cref="SuccessMessages"/>.
/// The <c>MSGxx</c> / <c>BR-xx</c> references in comments point to COMMON_REQUIREMENTS_AND_MESSAGES_SPECIFICATION.md.
/// </summary>
public static class ErrorCodes
{
    // Generic & HTTP Errors
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string Conflict = "CONFLICT";
    public const string InternalError = "INTERNAL_ERROR";

    // Input validation & authentication
    public const string RequiredField = "REQUIRED_FIELD";                       // MSG02
    public const string MaxLengthExceeded = "MAX_LENGTH_EXCEEDED";              // MSG08
    public const string InvalidCredentials = "INVALID_CREDENTIALS";             // MSG09

    // My Profile (docs/api/MY_PROFILE_CONTRACT.md)
    public const string InvalidCurrentPassword = "INVALID_CURRENT_PASSWORD";

    // Submission / Verification / KPI Result lifecycle (docs/api/SUBMISSION_VERIFICATION_KPI_RESULT_CONTRACT.md)
    public const string SubmissionInvalidState = "SUBMISSION_INVALID_STATE";
    public const string VerificationInvalidState = "VERIFICATION_INVALID_STATE";
    public const string KpiResultNotFound = "KPI_RESULT_NOT_FOUND";
    public const string MakerCannotBeChecker = "MAKER_CANNOT_BE_CHECKER";       // MSG10, BR-02
    public const string KpiAlreadyContributed = "KPI_ALREADY_CONTRIBUTED";      // MSG22, BR-08
    public const string EvidenceLocked = "EVIDENCE_LOCKED";                     // MSG23, BR-13
    public const string MediaTimestampRequired = "MEDIA_TIMESTAMP_REQUIRED";    // MSG28, BR-19

    // Work lifecycle (docs/api/WORK_CONTRACT.md)
    public const string WorkInvalidState = "WORK_INVALID_STATE";
    public const string WorkDeadlineExceedsCampaign = "WORK_DEADLINE_EXCEEDS_CAMPAIGN";   // formerly MSG18 (BR-12)
    public const string CampaignNotActive = "CAMPAIGN_NOT_ACTIVE";                        // formerly MSG18 (BR-12)

    // Campaign (docs/api/CAMPAIGN_CONTRACT.md)
    public const string CampaignDeadlineExpired = "CAMPAIGN_DEADLINE_EXPIRED";            // MSG18, BR-12 (late submission)
    public const string CampaignEndDateBeforeWorkDeadline = "CAMPAIGN_END_DATE_BEFORE_WORK_DEADLINE"; // formerly MSG18 (BR-12)
    public const string TargetAllocationNot100Percent = "TARGET_ALLOCATION_NOT_100_PERCENT";          // MSG14, BR-24

    // Assignment & configuration rules
    public const string VipCertificationRequired = "VIP_CERTIFICATION_REQUIRED";  // MSG11, BR-03
    public const string DelegationLoop = "DELEGATION_LOOP";                       // MSG12, BR-04
    public const string KpiTypeMismatch = "KPI_TYPE_MISMATCH";                    // MSG13, BR-09
    public const string DepartmentTransferBlocked = "DEPARTMENT_TRANSFER_BLOCKED"; // MSG29, BR-23

    // Performance evaluation (docs/api/PERFORMANCE_EVALUATION_CONTRACT.md)
    public const string PerformanceEvaluationInvalidState = "PERFORMANCE_EVALUATION_INVALID_STATE";

    // Appeal (docs/api/APPEAL_CONTRACT.md)
    public const string AppealInvalidState = "APPEAL_INVALID_STATE";
    public const string AppealAlreadyOpen = "APPEAL_ALREADY_OPEN";
    public const string AppealWindowExpired = "APPEAL_WINDOW_EXPIRED";

    // Evaluation profile management (docs/api/EVALUATION_PROFILE_CONTRACT.md)
    public const string EvaluationProfileInUse = "EVALUATION_PROFILE_IN_USE";

    // Evaluation & Calibration lifecycle (docs/api/CALIBRATION_APPROVAL_CONTRACT.md)
    public const string TierAQuotaExceeded = "TIER_A_QUOTA_EXCEEDED";

    // Target allocation (docs/api/TARGET_ALLOCATION_CONTRACT.md)
    public const string TargetDepartmentSumBelow = "TARGET_DEPARTMENT_SUM_BELOW";
    public const string TargetQuarterSumBelow = "TARGET_QUARTER_SUM_BELOW";
    public const string TargetPeriodNotOpen = "TARGET_PERIOD_NOT_OPEN";
    public const string TargetDepartmentNotInBranch = "TARGET_DEPARTMENT_NOT_IN_BRANCH";
    public const string TargetImportInvalid = "TARGET_IMPORT_INVALID";
    public const string TargetInvalidState = "TARGET_INVALID_STATE";

    private static readonly Dictionary<string, (string Vi, string En)> DefaultMessages = new()
    {
        [ValidationError] = ("Dữ liệu đầu vào không hợp lệ.", "Input validation failed."),
        [NotFound] = ("Không tìm thấy tài nguyên yêu cầu.", "Requested resource was not found."),
        [Unauthorized] = ("Phiên làm việc không hợp lệ hoặc đã hết hạn.", "Unauthorized or session expired."),
        [Forbidden] = ("Bạn không có quyền thực hiện thao tác này.", "Access forbidden."),
        [Conflict] = ("Dữ liệu bị xung đột hoặc đã tồn tại.", "Resource conflict or already exists."),
        [InternalError] = ("Đã xảy ra lỗi hệ thống không mong muốn.", "An unexpected system error occurred."),
        [RequiredField] = ("Trường (*) là bắt buộc nhập.", "The * field is required."),
        [MaxLengthExceeded] = ("Độ dài giá trị nhập vượt quá quy định.", "Input length exceeded allowed limit."),
        [InvalidCredentials] = ("Tên đăng nhập hoặc mật khẩu không chính xác. Vui lòng kiểm tra lại.", "Incorrect user name or password. Please check again."),
        [InvalidCurrentPassword] = ("Mật khẩu hiện tại không đúng.", "The current password is incorrect."),
        [SubmissionInvalidState] = ("Trạng thái hiện tại không cho phép thao tác với báo cáo kết quả.", "The current state does not allow this submission action."),
        [VerificationInvalidState] = ("Hồ sơ không ở trạng thái chờ thẩm định.", "The submission is not awaiting verification."),
        [KpiResultNotFound] = ("Chưa có kết quả KPI đã được thẩm định.", "No verified KPI result was found."),
        [MakerCannotBeChecker] = ("Lỗi kiểm tra: Người thực hiện (Maker) không được phép tự phân công mình làm người phê duyệt (Checker).", "Validation Error: Maker cannot assign self as Checker/Approver for this task."),
        [KpiAlreadyContributed] = ("Lỗi Xung đột: Công việc ID đã đóng góp KPI vào hệ thống trước đó.", "Conflict Error: Task ID has already contributed KPI to the system."),
        [EvidenceLocked] = ("Truy cập bị từ chối: Tệp tài liệu minh chứng đã bị khóa vĩnh viễn sau khi phê duyệt.", "Access Denied: Evidence attachments are permanently locked post-approval."),
        [MediaTimestampRequired] = ("Lỗi kiểm tra: Bắt buộc phải cung cấp Dấu thời gian (Timestamp) minh chứng.", "Validation Error: Media Timestamp proof is required."),
        [WorkInvalidState] = ("Trạng thái hiện tại của công việc không cho phép chuyển sang trạng thái yêu cầu.", "The current work status does not allow the requested transition."),
        [WorkDeadlineExceedsCampaign] = ("Hạn hoàn thành công việc vượt quá ngày kết thúc chiến dịch.", "The work deadline exceeds the campaign end date."),
        [CampaignNotActive] = ("Chiến dịch được gắn không trong trạng thái hoạt động.", "The linked campaign is not in an active status."),
        [CampaignDeadlineExpired] = ("Từ chối nộp: Thời hạn chiến dịch đã kết thúc.", "Submission Rejected: Campaign deadline expired."),
        [CampaignEndDateBeforeWorkDeadline] = ("Ngày kết thúc chiến dịch không được nhỏ hơn hạn của các công việc đã sinh.", "The campaign end date cannot be earlier than the deadline of generated works."),
        [TargetAllocationNot100Percent] = ("Lỗi kiểm tra: Tổng tỷ lệ chỉ tiêu phân bổ bắt buộc phải bằng chính xác 100%.", "Validation Error: Total allocated target percentage must sum to exactly 100%."),
        [VipCertificationRequired] = ("Lỗi kiểm tra: Nhân sự được phân công chưa có chứng chỉ VIP_Trained phục vụ Khách hàng Ưu tiên.", "Validation Error: Assigned employee lacks VIP_Trained certification required for Priority Customers."),
        [DelegationLoop] = ("Lỗi cấu hình: Lộ trình ủy quyền không hợp lệ. Nghiêm cấm tạo vòng lặp ủy quyền.", "Validation Error: Invalid delegation path. Circular delegation loops are not permitted."),
        [KpiTypeMismatch] = ("Lỗi ánh xạ: Kiểu chỉ số không đồng nhất (Định lượng vs Định tính).", "Mapping Error: Metric types do not match (Quantitative vs Qualitative)."),
        [DepartmentTransferBlocked] = ("Lỗi kiểm tra: Chặn chuyển phòng ban. Nhân viên hiện còn công việc chưa hoàn tất.", "Validation Error: Department transfer blocked. Employee has active task(s)."),
        [PerformanceEvaluationInvalidState] = ("Trạng thái đánh giá hoặc kỳ đánh giá không cho phép tính điểm KPI.", "The evaluation or its period does not allow KPI score calculation."),
        [AppealInvalidState] = ("Trạng thái hiện tại không cho phép thao tác với khiếu nại.", "The current state does not allow this appeal action."),
        [AppealAlreadyOpen] = ("Kết quả này đã có khiếu nại đang được xử lý.", "This result already has an appeal in progress."),
        [AppealWindowExpired] = ("Đã quá thời hạn 48 giờ để khiếu nại kết quả bị từ chối.", "The 48-hour window to appeal a rejected result has expired."),
        [EvaluationProfileInUse] = ("Hồ sơ đánh giá đã được dùng để tính điểm nên không thể đổi cấu trúc. Vui lòng tạo phiên bản mới.", "The evaluation profile is already used by performance evaluations and its structure cannot change. Create a new version instead."),
        [TargetDepartmentSumBelow] = ("Tổng chỉ tiêu năm của các phòng ban phải lớn hơn hoặc bằng chỉ tiêu năm của chi nhánh.", "Department year targets must add up to at least the branch year target."),
        [TargetQuarterSumBelow] = ("Tổng chỉ tiêu các quý phải lớn hơn hoặc bằng chỉ tiêu năm của phòng ban.", "Quarter targets must add up to at least the department year target."),
        [TargetPeriodNotOpen] = ("Kỳ đánh giá không ở trạng thái mở nên không thể giao chỉ tiêu.", "The evaluation period is not open for target allocation."),
        [TargetDepartmentNotInBranch] = ("Phòng ban phải là đơn vị con trực tiếp của chi nhánh.", "Each department must be a direct child of the branch."),
        [TargetImportInvalid] = ("Tệp nhập chứa dòng dữ liệu không hợp lệ nên không có chỉ tiêu nào được ghi nhận.", "The import file contains invalid rows, so no target was applied."),
        [TargetInvalidState] = ("Trạng thái chỉ tiêu hiện tại không cho phép thao tác này.", "The current target state does not allow this action."),
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
