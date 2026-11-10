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
    public const string KpiTargetNotAssigned = "KPI_TARGET_NOT_ASSIGNED";
    public const string EvidenceLocked = "EVIDENCE_LOCKED";                     // MSG23, BR-13
    public const string MediaTimestampRequired = "MEDIA_TIMESTAMP_REQUIRED";    // MSG28, BR-19

    // Work lifecycle (docs/api/WORK_CONTRACT.md)
    public const string WorkInvalidState = "WORK_INVALID_STATE";

    // Campaign (docs/api/CAMPAIGN_CONTRACT.md)
    public const string CampaignDeadlineExpired = "CAMPAIGN_DEADLINE_EXPIRED";            // MSG18, BR-12 (late submission)
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
    public const string AppealScoreWindowExpired = "APPEAL_SCORE_WINDOW_EXPIRED";

    // Handover (docs/api/HANDOVER_CONTRACT.md)
    public const string HandoverAmountExceedsThreshold = "HANDOVER_AMOUNT_EXCEEDS_THRESHOLD";
    public const string HandoverProgressBelowThreshold = "HANDOVER_PROGRESS_BELOW_THRESHOLD";
    public const string HandoverNoEligibleRecipient = "HANDOVER_NO_ELIGIBLE_RECIPIENT";
    public const string HandoverAlreadyPending = "HANDOVER_ALREADY_PENDING";
    public const string HandoverNotPending = "HANDOVER_NOT_PENDING";

    // Evaluation profile management (docs/api/EVALUATION_PROFILE_CONTRACT.md)
    public const string EvaluationProfileInUse = "EVALUATION_PROFILE_IN_USE";

    // Business objectives (docs/api/BUSINESS_OBJECTIVE_CONTRACT.md)
    public const string BusinessObjectiveInUse = "BUSINESS_OBJECTIVE_IN_USE";

    // Evaluation & Calibration lifecycle (docs/api/CALIBRATION_APPROVAL_CONTRACT.md)
    public const string TierAQuotaExceeded = "TIER_A_QUOTA_EXCEEDED";
    public const string EvaluationDisputePending = "EVALUATION_DISPUTE_PENDING";           // BR-31

    // Evaluation periods (docs/api/EVALUATION_PERIOD_CONTRACT.md)
    public const string EvaluationPeriodInvalidState = "EVALUATION_PERIOD_INVALID_STATE";

    // Locked evaluation periods (docs/api/PERIOD_LOCK_CONTRACT.md)
    public const string PeriodLocked = "PERIOD_LOCKED";

    // Risk events (docs/api/RISK_EVENT_CONTRACT.md)
    public const string RiskEventInvalidState = "RISK_EVENT_INVALID_STATE";
    public const string RiskEventPeriodUnavailable = "RISK_EVENT_PERIOD_UNAVAILABLE";

    // Target allocation (docs/api/TARGET_ALLOCATION_CONTRACT.md)
    public const string TargetDepartmentSumBelow = "TARGET_DEPARTMENT_SUM_BELOW";
    public const string TargetQuarterSumBelow = "TARGET_QUARTER_SUM_BELOW";
    public const string TargetPeriodNotOpen = "TARGET_PERIOD_NOT_OPEN";
    public const string TargetDepartmentNotInBranch = "TARGET_DEPARTMENT_NOT_IN_BRANCH";
    public const string TargetImportInvalid = "TARGET_IMPORT_INVALID";
    public const string TargetInvalidState = "TARGET_INVALID_STATE";

    // Year target import (docs/design/BRANCH_YEAR_KPI_IMPORT_DESIGN.md, replaces TargetImportInvalid above)
    public const string TargetImportKpiNotFound = "TARGET_IMPORT_KPI_NOT_FOUND";
    public const string TargetImportKpiInactive = "TARGET_IMPORT_KPI_INACTIVE";
    public const string TargetImportInvalidValue = "TARGET_IMPORT_INVALID_VALUE";
    public const string TargetImportUnitMismatch = "TARGET_IMPORT_UNIT_MISMATCH";
    public const string TargetImportYearColumnMissing = "TARGET_IMPORT_YEAR_COLUMN_MISSING";
    public const string TargetImportBranchMismatch = "TARGET_IMPORT_BRANCH_MISMATCH";
    public const string TargetImportFormulaNotCalculated = "TARGET_IMPORT_FORMULA_NOT_CALCULATED";
    public const string TargetImportPrevYearMismatch = "TARGET_IMPORT_PREV_YEAR_MISMATCH";
    public const string TargetImportNoValidRows = "TARGET_IMPORT_NO_VALID_ROWS";
    public const string TargetImportQuarterSumMismatch = "TARGET_IMPORT_QUARTER_SUM_MISMATCH";
    public const string TargetImportFieldMismatch = "TARGET_IMPORT_FIELD_MISMATCH";

    // Quarter split & allocation (Phase 2, docs/api/TARGET_ALLOCATION_CONTRACT.md)
    public const string TargetQuarterSumMismatch = "TARGET_QUARTER_SUM_MISMATCH";           // MSG14, BR-24: quarters must equal year
    public const string TargetAllocationSumMismatch = "TARGET_ALLOCATION_SUM_MISMATCH";     // MSG14, BR-24: departments must equal branch
    public const string TargetDeptQuarterSumMismatch = "TARGET_DEPT_QUARTER_SUM_MISMATCH";  // BR-24: each dept's quarters must equal its year
    public const string TargetQuartersNotReady = "TARGET_QUARTERS_NOT_READY";              // quarters not yet split for this KPI
    public const string TargetDepartmentNotEligible = "TARGET_DEPARTMENT_NOT_ELIGIBLE";    // department has no headcount for this KPI
    public const string TargetPublishBlocked = "TARGET_PUBLISH_BLOCKED";                   // publish validation failed

    // Data import (docs/api/DATA_IMPORT_CONTRACT.md)
    public const string ImportTemplateInvalid = "IMPORT_TEMPLATE_INVALID";
    public const string DataImportHasErrors = "DATA_IMPORT_HAS_ERRORS";
    public const string DataImportInvalidValue = "DATA_IMPORT_INVALID_VALUE";
    public const string DataImportDuplicate = "DATA_IMPORT_DUPLICATE";
    public const string DataImportReferenceNotFound = "DATA_IMPORT_REFERENCE_NOT_FOUND";
    public const string DataImportConflict = "DATA_IMPORT_CONFLICT";

    // Transaction import (docs/api/TRANSACTION_IMPORT_CONTRACT.md)
    public const string ImportInvalidFile = "IMPORT_INVALID_FILE";

    // Work transaction reconcile (docs/api/WORK_TRANSACTION_CONTRACT.md)
    public const string KpiAggregationNotSupported = "KPI_AGGREGATION_NOT_SUPPORTED";
    public const string WorkTransactionLimit = "WORK_TRANSACTION_LIMIT";
    public const string WorkTransactionDuplicate = "WORK_TRANSACTION_DUPLICATE";
    public const string MetricTypeMismatch = "METRIC_TYPE_MISMATCH";

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
        [KpiTargetNotAssigned] = ("Nhân sự nhận điểm chưa có chỉ tiêu (target) đang hiệu lực cho KPI của công việc này nên chưa thể phê duyệt kết quả.", "The recipient has no active target for this work's KPI, so the result cannot be approved."),
        [EvidenceLocked] = ("Truy cập bị từ chối: Tệp tài liệu minh chứng đã bị khóa vĩnh viễn sau khi phê duyệt.", "Access Denied: Evidence attachments are permanently locked post-approval."),
        [MediaTimestampRequired] = ("Lỗi kiểm tra: Bắt buộc phải cung cấp Dấu thời gian (Timestamp) minh chứng.", "Validation Error: Media Timestamp proof is required."),
        [WorkInvalidState] = ("Trạng thái hiện tại của công việc không cho phép chuyển sang trạng thái yêu cầu.", "The current work status does not allow the requested transition."),
        [CampaignDeadlineExpired] = ("Từ chối nộp: Thời hạn chiến dịch đã kết thúc.", "Submission Rejected: Campaign deadline expired."),
        [TargetAllocationNot100Percent] = ("Lỗi kiểm tra: Tổng tỷ lệ chỉ tiêu phân bổ bắt buộc phải bằng chính xác 100%.", "Validation Error: Total allocated target percentage must sum to exactly 100%."),
        [VipCertificationRequired] = ("Lỗi kiểm tra: Nhân sự được phân công chưa có chứng chỉ VIP_Trained phục vụ Khách hàng Ưu tiên.", "Validation Error: Assigned employee lacks VIP_Trained certification required for Priority Customers."),
        [DelegationLoop] = ("Lỗi cấu hình: Lộ trình ủy quyền không hợp lệ. Nghiêm cấm tạo vòng lặp ủy quyền.", "Validation Error: Invalid delegation path. Circular delegation loops are not permitted."),
        [KpiTypeMismatch] = ("Lỗi ánh xạ: Kiểu chỉ số không đồng nhất (Định lượng vs Định tính).", "Mapping Error: Metric types do not match (Quantitative vs Qualitative)."),
        [DepartmentTransferBlocked] = ("Lỗi kiểm tra: Chặn chuyển phòng ban. Nhân viên hiện còn công việc chưa hoàn tất.", "Validation Error: Department transfer blocked. Employee has active task(s)."),
        [PerformanceEvaluationInvalidState] = ("Trạng thái đánh giá hoặc kỳ đánh giá không cho phép tính điểm KPI.", "The evaluation or its period does not allow KPI score calculation."),
        [AppealInvalidState] = ("Trạng thái hiện tại không cho phép thao tác với khiếu nại.", "The current state does not allow this appeal action."),
        [AppealAlreadyOpen] = ("Kết quả này đã có khiếu nại đang được xử lý.", "This result already has an appeal in progress."),
        [AppealWindowExpired] = ("Đã quá thời hạn 48 giờ để khiếu nại kết quả bị từ chối.", "The 48-hour window to appeal a rejected result has expired."),
        [AppealScoreWindowExpired] = ("Đã quá thời hạn khiếu nại điểm đánh giá.", "The window to appeal an evaluation score has expired."),
        [HandoverAmountExceedsThreshold] = ("Giá trị giao dịch vượt ngưỡng cho phép bàn giao (BR-01).", "The amount exceeds the allowed handover threshold (BR-01)."),
        [HandoverProgressBelowThreshold] = ("Tiến độ công việc chưa đạt ngưỡng tối thiểu để chuyển việc.", "Work progress has not reached the minimum threshold required for transfer."),
        [HandoverNoEligibleRecipient] = ("Không tìm thấy nhân viên hợp lệ để nhận chuyển việc.", "No eligible employee found to receive the transferred work."),
        [HandoverAlreadyPending] = ("Công việc này đã có yêu cầu bàn giao đang chờ xử lý.", "This work already has a pending handover request."),
        [HandoverNotPending] = ("Yêu cầu bàn giao không còn ở trạng thái chờ xử lý.", "The handover request is no longer pending."),
        [EvaluationProfileInUse] = ("Hồ sơ đánh giá đã được dùng để tính điểm nên không thể đổi cấu trúc. Vui lòng tạo phiên bản mới.", "The evaluation profile is already used by performance evaluations and its structure cannot change. Create a new version instead."),
        [BusinessObjectiveInUse] = ("Mục tiêu kinh doanh đã được dùng bởi chiến dịch, chỉ tiêu, KPI hoặc mục tiêu con nên không thể xóa.", "The business objective is used by campaigns, targets, KPIs or child objectives and cannot be deleted."),
        [TargetDepartmentSumBelow] = ("Tổng chỉ tiêu năm của các phòng ban phải lớn hơn hoặc bằng chỉ tiêu năm của chi nhánh.", "Department year targets must add up to at least the branch year target."),
        [TargetQuarterSumBelow] = ("Tổng chỉ tiêu các quý phải lớn hơn hoặc bằng chỉ tiêu năm của phòng ban.", "Quarter targets must add up to at least the department year target."),
        [TargetPeriodNotOpen] = ("Kỳ đánh giá không ở trạng thái mở nên không thể giao chỉ tiêu.", "The evaluation period is not open for target allocation."),
        [TargetDepartmentNotInBranch] = ("Phòng ban phải là đơn vị con trực tiếp của chi nhánh.", "Each department must be a direct child of the branch."),
        [TargetImportInvalid] = ("Tệp nhập chứa dòng dữ liệu không hợp lệ nên không có chỉ tiêu nào được ghi nhận.", "The import file contains invalid rows, so no target was applied."),
        [TargetInvalidState] = ("Trạng thái chỉ tiêu hiện tại không cho phép thao tác này.", "The current target state does not allow this action."),
        [TargetImportKpiNotFound] = ("Mã KPI không tồn tại trong danh mục.", "The KPI code does not exist in the catalog."),
        [TargetImportKpiInactive] = ("KPI đã ngừng sử dụng nên không thể giao chỉ tiêu.", "The KPI is inactive and cannot be assigned a target."),
        [TargetImportInvalidValue] = ("Giá trị chỉ tiêu không hợp lệ.", "The target value is invalid."),
        [TargetImportUnitMismatch] = ("Đơn vị trong file không khớp với danh mục KPI.", "The file's unit does not match the KPI catalog."),
        [TargetImportYearColumnMissing] = ("File không có cột 'Năm {year}' của năm cần nhập.", "The file has no 'Năm {year}' column for the requested year."),
        [TargetImportBranchMismatch] = ("Mã chi nhánh trong file không khớp với chi nhánh đang chọn.", "The file's branch code does not match the selected branch."),
        [TargetImportFormulaNotCalculated] = ("Ô công thức chưa có giá trị tính sẵn; hãy mở và lưu lại file bằng Excel.", "A formula cell has no cached value; open and save the file in Excel."),
        [TargetImportPrevYearMismatch] = ("Giá trị năm trước trong file khác với dữ liệu đã lưu.", "The previous year's value in the file differs from the stored value."),
        [TargetImportNoValidRows] = ("Không có dòng hợp lệ nào để nhập.", "There are no valid rows to import."),
        [TargetImportQuarterSumMismatch] = ("Tổng 4 quý trong file khác với chỉ tiêu cả năm.", "The file's four quarters do not add up to the year value."),
        [TargetImportFieldMismatch] = ("Giá trị trong file khác với danh mục KPI.", "The file's value does not match the KPI catalog."),
        [ImportTemplateInvalid] = ("File không đúng mẫu quy định.", "The file does not match the required template."),
        [DataImportHasErrors] = ("File còn dòng lỗi, không thể xác nhận import.", "The file still has invalid rows and cannot be committed."),
        [DataImportInvalidValue] = ("Giá trị không hợp lệ.", "Invalid value."),
        [DataImportDuplicate] = ("Mã nhân viên trùng lặp trong file.", "Duplicate employee code in the file."),
        [DataImportReferenceNotFound] = ("Mã tham chiếu không tồn tại.", "Referenced code not found."),
        [DataImportConflict] = ("Mã nhân viên hoặc email đã tồn tại, không có dữ liệu nào được ghi.", "An employee code or email already exists, so nothing was written."),
        [ImportInvalidFile] = ("Tệp nhập không đúng định dạng .xlsx hoặc thiếu cột bắt buộc.", "The import file is not a valid .xlsx or is missing a required column."),
        [KpiAggregationNotSupported] = ("KPI kh\u00f4ng h\u1ed7 tr\u1ee3 c\u00e1ch t\u1ed5ng h\u1ee3p n\u00e0y.", "The KPI aggregation type is not supported for transaction reconciliation."),
        [WorkTransactionLimit] = ("M\u1ed7i c\u00f4ng vi\u1ec7c ch\u1ec9 c\u00f3 t\u1ed1i \u0111a 10 m\u00e3 giao d\u1ecbch.", "A work can hold at most 10 transaction codes."),
        [WorkTransactionDuplicate] = ("M\u00e3 giao d\u1ecbch \u0111\u00e3 \u0111\u01b0\u1ee3c d\u00f9ng cho m\u1ed9t c\u00f4ng vi\u1ec7c.", "The transaction code is already used by a work."),
        [MetricTypeMismatch] = ("Lỗi ánh xạ: loại chỉ số không khớp (Định lượng và Định tính). Cần ánh xạ thủ công.", "Mapping Error: Metric types do not match (Quantitative vs Qualitative). Manual mapping required."),
        [EvaluationDisputePending] = ("Còn điểm đánh giá đang tranh chấp (Disputed_Pending) nên chưa thể khóa kỳ đánh giá (BR-31).", "Some scores are disputed (Disputed_Pending), so the evaluation period cannot be locked yet (BR-31)."),
        [EvaluationPeriodInvalidState] = ("Trạng thái hiện tại của kỳ đánh giá không cho phép thao tác này.", "The current state of the evaluation period does not allow this action."),
        [PeriodLocked] = ("Kỳ đánh giá đã bị khóa nên không thể thay đổi hoặc nhập dữ liệu KPI.", "The evaluation period is locked, so KPI data cannot be changed or imported."),
        [RiskEventInvalidState] = ("Trạng thái hiện tại của sự kiện rủi ro không cho phép thao tác này.", "The current state of the risk event does not allow this action."),
        [RiskEventPeriodUnavailable] = ("Không có kỳ đánh giá đang mở chứa ngày xảy ra sự kiện rủi ro nên không thể ghi điểm.", "No open evaluation period contains the risk event date, so the penalty cannot be recorded."),
        [TierAQuotaExceeded] = ("Tỷ lệ phân bổ xếp loại Tier A vượt quá hạn ngạch tối đa cho phép của đơn vị (BR-37).", "Tier A distribution exceeds department quota cap (BR-37)."),
        [TargetQuarterSumMismatch] = ("Tổng 4 quý phải bằng chính xác chỉ tiêu năm.", "Quarter totals must equal year target exactly."),
        [TargetAllocationSumMismatch] = ("Tổng chỉ tiêu của các phòng ban phải bằng chính xác chỉ tiêu của chi nhánh.", "Department allocations must equal branch target exactly."),
        [TargetDeptQuarterSumMismatch] = ("Tổng chỉ tiêu các quý của từng phòng ban phải bằng chính xác chỉ tiêu năm của phòng đó.", "Each department's quarter totals must equal its year target exactly."),
        [TargetQuartersNotReady] = ("Chỉ tiêu theo quý chưa được chia cho chỉ tiêu này; hãy hoàn thành bước chia quý trước.", "Quarters have not yet been split for this KPI; complete step 2 first."),
        [TargetDepartmentNotEligible] = ("Phòng ban này không có nhân viên đủ điều kiện cho chỉ tiêu này nên bị ẩn.", "This department has no eligible employees for this KPI and is hidden."),
        [TargetPublishBlocked] = ("Phát hành bị chặn: một hoặc nhiều chỉ tiêu chưa khớp. Xem chi tiết để hoàn thành.", "Publish is blocked: one or more KPI targets are incomplete. See details to resolve."),
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
