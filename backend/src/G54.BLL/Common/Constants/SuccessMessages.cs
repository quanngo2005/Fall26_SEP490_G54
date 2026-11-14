namespace G54.BLL.Common.Constants;

/// <summary>
/// Message keys for non-error outcomes (success / informational), used in the <c>message</c> field of
/// <c>ApiResponse.Ok</c>. They are not error codes and must not be placed in <c>errorCode</c>.
/// The <c>MSGxx</c> references in comments point to COMMON_REQUIREMENTS_AND_MESSAGES_SPECIFICATION.md.
/// </summary>
public static class SuccessMessages
{
    public const string NoResultsFound = "NO_RESULTS_FOUND";                 // MSG01
    public const string Updated = "UPDATED";                                 // MSG03
    public const string Created = "CREATED";                                 // MSG04
    public const string HandoverRequested = "HANDOVER_REQUESTED";            // MSG05
    public const string HandoverTransferred = "HANDOVER_TRANSFERRED";
    public const string HandoverAccepted = "HANDOVER_ACCEPTED";
    public const string HandoverRejected = "HANDOVER_REJECTED";
    public const string WorkResultSubmitted = "WORK_RESULT_SUBMITTED";       // MSG06
    public const string Deleted = "DELETED";                                 // MSG07
    public const string LateSubmissionAccepted = "LATE_SUBMISSION_ACCEPTED"; // MSG17, BR-12

    private static readonly Dictionary<string, (string Vi, string En)> Messages = new()
    {
        [NoResultsFound] = ("Không tìm thấy kết quả phù hợp.", "No search results."),
        [Updated] = ("Cập nhật thành công.", "Update successfully."),
        [Created] = ("Tạo mới thành công.", "Create successfully."),
        [HandoverRequested] = ("Gửi yêu cầu bàn giao thành công.", "Handover request sent successfully."),
        [HandoverTransferred] = ("Đã chuyển công việc cho người nhận.", "Work transferred successfully."),
        [HandoverAccepted] = ("Đã nhận bàn giao công việc.", "Handover accepted successfully."),
        [HandoverRejected] = ("Đã từ chối yêu cầu bàn giao.", "Handover rejected successfully."),
        [WorkResultSubmitted] = ("Nộp kết quả công việc thành công. Đang chờ thẩm định.", "Work result submitted successfully. Awaiting verification."),
        [Deleted] = ("Xóa thành công.", "Delete successfully."),
        [LateSubmissionAccepted] = ("Nộp trễ hạn: Đã nhận trong thời gian gia hạn 5 phút. Chuyển sang luồng Thẩm định Nộp trễ.", "Late Submission: Received within 5-minute grace period. Routed for Late Submission Review."),
    };

    public static string GetMessage(string key, bool english = false)
    {
        if (Messages.TryGetValue(key, out var messagePair))
        {
            return english ? messagePair.En : messagePair.Vi;
        }

        return english ? "Operation completed." : "Thao tác thành công.";
    }
}
