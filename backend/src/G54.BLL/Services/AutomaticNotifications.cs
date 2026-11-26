using System.Globalization;
using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>The notifications the system raises by itself, with the Vietnamese text each one shows.</summary>
internal static class AutomaticNotifications
{
    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 2000;

    /// <summary>An employee asked to hand a work over: their direct manager is told and settles it. The recipient hears only once it is decided.</summary>
    public static List<NotificationDraft> HandoverRequested(TaskHandover handover, Work work, Employee from, Employee to)
    {
        var drafts = new List<NotificationDraft>();
        if (from.ManagerId is { } managerId)
        {
            drafts.Add(ForHandover(
                managerId,
                NotificationType.HandoverRequested,
                "Yêu cầu bàn giao công việc",
                $"{from.FullName} muốn bàn giao công việc {Describe(work)} cho {to.FullName}. Hãy duyệt hoặc từ chối.",
                NotificationSeverity.Important,
                handover));
        }

        return drafts;
    }
    /// <summary>A handover was settled: whoever did not make the decision hears the outcome.</summary>
    public static List<NotificationDraft> HandoverDecided(TaskHandover handover, Work work, Employee from, Employee to, Guid deciderId)
    {
        var drafts = new List<NotificationDraft>();
        var accepted = handover.Status == HandoverStatus.Accepted;
        var details = $"{Describe(work)} từ {from.FullName} sang {to.FullName}";

        if (from.Id != deciderId)
        {
            drafts.Add(accepted
                ? ForHandover(from.Id, NotificationType.HandoverAccepted, "Yêu cầu bàn giao đã được chấp nhận", $"Công việc {details} đã được chuyển.", NotificationSeverity.Normal, handover)
                : ForHandover(from.Id, NotificationType.HandoverRejected, "Yêu cầu bàn giao bị từ chối", $"Yêu cầu bàn giao công việc {details} đã bị từ chối.", NotificationSeverity.Normal, handover));
        }

        // The recipient never saw the request, so they only hear about it once the work is theirs.
        if (accepted && to.Id != deciderId)
        {
            drafts.Add(ForHandover(to.Id, NotificationType.HandoverTransferred, "Bạn được giao công việc mới", $"Công việc {details} đã được chuyển cho bạn.", NotificationSeverity.Important, handover));
        }

        return drafts;
    }

    /// <summary>A work passed its deadline: the assignee is told, and so is their direct manager.</summary>
    public static List<NotificationDraft> WorkOverdue(Work work, Employee assignee)
    {
        var due = work.PlannedEndDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var drafts = new List<NotificationDraft>
        {
            ForWork(assignee.Id, "Công việc quá hạn", $"Công việc {Describe(work)} đã quá hạn (hạn {due}).", NotificationSeverity.Important, work),
        };

        if (assignee.ManagerId is { } managerId)
        {
            drafts.Add(ForWork(managerId, "Nhân viên có công việc quá hạn", $"{assignee.FullName} có công việc {Describe(work)} đã quá hạn (hạn {due}).", NotificationSeverity.Normal, work));
        }

        return drafts;
    }

    /// <summary>A manager changed the plan of a work: the assignee is told what changed and any note.</summary>
    public static NotificationDraft WorkPlanUpdated(Work work, Employee assignee, Employee manager, string summary, string? note)
    {
        var body = $"{manager.FullName} đã sửa kế hoạch công việc {Describe(work)}: {summary}.";
        if (!string.IsNullOrWhiteSpace(note))
        {
            body += $" Ghi chú: {note}";
        }

        return new NotificationDraft(
            assignee.Id,
            NotificationType.WorkPlanUpdated,
            "Kế hoạch công việc được cập nhật",
            ClampBody(body),
            NotificationSeverity.Important,
            NotificationRelatedType.Work,
            work.Id);
    }

    /// <summary>
    /// A period was locked while a submitter's result was still SUBMITTED/UNDER_REVIEW: the result was carried over
    /// to <paramref name="nextPeriod"/>, so each distinct submitter is told where their pending result now lives.
    /// No clean way exists yet to resolve "the department manager" of an arbitrary department from BLL (the only
    /// such resolution lives as a private heuristic inside DepartmentService), so only submitters are notified.
    /// </summary>
    public static List<NotificationDraft> ResultSubmissionsCarriedOver(IReadOnlyCollection<Guid> submitterEmployeeIds, EvaluationPeriod nextPeriod) =>
        submitterEmployeeIds
            .Select(employeeId => new NotificationDraft(
                employeeId,
                NotificationType.ResultCarriedOver,
                "Kết quả của bạn được chuyển sang kỳ mới",
                $"Kỳ đánh giá đã được khóa khi kết quả của bạn còn đang chờ xử lý; kết quả đã được chuyển sang kỳ {nextPeriod.Code} - {nextPeriod.Name}.",
                NotificationSeverity.Important,
                NotificationRelatedType.EvaluationPeriod,
                nextPeriod.Id))
            .ToList();

    /// <summary>A manager assigned a new work item to an employee.</summary>
    public static NotificationDraft? WorkAssigned(Work work, Employee assignee, Employee creator)
    {
        if (assignee.Id == creator.Id)
        {
            return null;
        }

        var due = work.PlannedEndDate.HasValue
            ? $" (hạn {work.PlannedEndDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)})"
            : string.Empty;
        return new NotificationDraft(
            assignee.Id,
            NotificationType.WorkAssigned,
            "Bạn được giao công việc mới",
            ClampBody($"{creator.FullName} đã giao cho bạn công việc {Describe(work)}{due}."),
            NotificationSeverity.Normal,
            NotificationRelatedType.Work,
            work.Id,
            creator.Id);
    }

    /// <summary>Batch-generated campaign works: one summary notification per participant.</summary>
    public static List<NotificationDraft> BatchWorksAssigned(
        BusinessCampaign campaign,
        IEnumerable<(Guid AssigneeId, int Count)> countsByAssignee,
        Employee creator)
    {
        return countsByAssignee
            .Where(item => item.Count > 0 && item.AssigneeId != creator.Id)
            .Select(item => new NotificationDraft(
                item.AssigneeId,
                NotificationType.WorkAssigned,
                "Bạn được phân bổ công việc chiến dịch",
                ClampBody($"{creator.FullName} đã phân bổ {item.Count} đầu việc từ chiến dịch {campaign.Code} - {campaign.Name} cho bạn."),
                NotificationSeverity.Normal,
                NotificationRelatedType.Campaign,
                campaign.Id,
                creator.Id))
            .ToList();
    }

    /// <summary>An employee submitted a work result: their direct manager is notified to review it.</summary>
    public static NotificationDraft? SubmissionSubmitted(Guid submissionId, string workCode, string workTitle, int submissionNo, Employee submitter)
    {
        if (submitter.ManagerId is not { } managerId || managerId == submitter.Id)
        {
            return null;
        }

        return new NotificationDraft(
            managerId,
            NotificationType.SubmissionSubmitted,
            "Hồ sơ kết quả chờ thẩm định",
            ClampBody($"{submitter.FullName} đã nộp kết quả phiên bản #{submissionNo} cho công việc {workCode} - {workTitle}."),
            NotificationSeverity.Normal,
            NotificationRelatedType.Submission,
            submissionId,
            submitter.Id);
    }

    /// <summary>A verifier decided on a submission (APPROVED / REVISION_REQUIRED / REJECTED).</summary>
    public static NotificationDraft SubmissionDecided(
        ResultSubmission submission,
        Work work,
        Employee verifier,
        string normalizedDecision,
        string? feedback)
    {
        var (title, outcome, severity) = normalizedDecision switch
        {
            "APPROVED" => ("Kết quả công việc được phê duyệt", "đã được phê duyệt", NotificationSeverity.Normal),
            "REVISION_REQUIRED" => ("Yêu cầu bổ sung hồ sơ kết quả", "cần bổ sung / chỉnh sửa", NotificationSeverity.Important),
            _ => ("Kết quả công việc bị từ chối", "đã bị từ chối", NotificationSeverity.Important),
        };

        var body = $"Hồ sơ nộp #{submission.SubmissionNo} của công việc {Describe(work)} {outcome} bởi {verifier.FullName}.";
        if (!string.IsNullOrWhiteSpace(feedback))
        {
            body += $" Ý kiến: {feedback.Trim()}";
        }

        return new NotificationDraft(
            submission.SubmitterEmployeeId,
            NotificationType.SubmissionDecided,
            title,
            ClampBody(body),
            severity,
            NotificationRelatedType.Submission,
            submission.Id,
            verifier.Id);
    }

    /// <summary>Core Banking transaction codes on a work item failed reconciliation (NOT_MATCHED).</summary>
    public static List<NotificationDraft> TransactionMismatched(
        Work work,
        Employee assignee,
        IReadOnlyCollection<(string Code, string? Reason)> mismatched)
    {
        if (mismatched.Count == 0)
        {
            return [];
        }

        var summary = string.Join(", ", mismatched.Take(5).Select(m => $"{m.Code} ({DescribeMismatchReason(m.Reason)})"));
        var extra = mismatched.Count > 5 ? $" (+{mismatched.Count - 5} mã khác)" : string.Empty;
        var drafts = new List<NotificationDraft>
        {
            new(
                assignee.Id,
                NotificationType.TransactionMismatch,
                "Mã giao dịch đối soát không khớp",
                ClampBody($"Công việc {Describe(work)} có {mismatched.Count} mã giao dịch không khớp Core Banking: {summary}{extra}."),
                NotificationSeverity.Important,
                NotificationRelatedType.Work,
                work.Id),
        };

        if (assignee.ManagerId is { } managerId && managerId != assignee.Id)
        {
            drafts.Add(new NotificationDraft(
                managerId,
                NotificationType.TransactionMismatch,
                "Nhân viên có mã giao dịch lệch đối soát",
                ClampBody($"{assignee.FullName} có {mismatched.Count} mã giao dịch không khớp trên công việc {Describe(work)}: {summary}{extra}."),
                NotificationSeverity.Normal,
                NotificationRelatedType.Work,
                work.Id));
        }

        return drafts;
    }

    /// <summary>An Excel import finished with row errors or skipped records.</summary>
    public static NotificationDraft ImportCompletedWithErrors(
        Guid recipientEmployeeId,
        string importLabel,
        string fileName,
        int validCount,
        int errorCount,
        Guid? batchId = null)
    {
        return new NotificationDraft(
            recipientEmployeeId,
            NotificationType.ImportCompletedWithErrors,
            ClampTitle($"Import {importLabel} có {errorCount} dòng lỗi"),
            ClampBody($"Tệp '{fileName}' đã xử lý xong: {validCount} dòng hợp lệ, {errorCount} dòng lỗi/bỏ qua cần kiểm tra."),
            NotificationSeverity.Important,
            batchId.HasValue ? NotificationRelatedType.ImportBatch : null,
            batchId);
    }

    /// <summary>BR-17: a customer gave an employee 1 star; each department manager hears it, once per rating.</summary>
    public static List<NotificationDraft> ServiceRatedOneStar(
        ServiceRating rating,
        Employee employee,
        IEnumerable<Guid> deptManagerEmployeeIds)
    {
        var comment = string.IsNullOrEmpty(rating.CustomerComment) ? string.Empty : $" Nhận xét: {rating.CustomerComment}";
        return deptManagerEmployeeIds
            .Where(id => id != employee.Id)
            .Distinct()
            .Select(id => new NotificationDraft(
                id,
                NotificationType.RatingOneStar,
                ClampTitle($"Khách hàng đánh giá 1 sao: {employee.FullName}"),
                ClampBody($"{employee.FullName} ({employee.EmployeeCode}) nhận đánh giá 1 sao cho giao dịch {rating.TransactionCode} ngày {rating.RatingDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.{comment}"),
                NotificationSeverity.Important,
                NotificationRelatedType.ImportBatch,
                rating.ImportBatchId))
            .ToList();
    }

    /// <summary>A risk event was recorded/confirmed against an employee.</summary>
    public static List<NotificationDraft> RiskEventRecorded(
        RiskEvent riskEvent,
        Employee subject,
        Employee actor,
        decimal? penaltyPoints)
    {
        var penaltyText = penaltyPoints is > 0m
            ? $" (-{penaltyPoints.Value.ToString("0.##", CultureInfo.InvariantCulture)} điểm)"
            : string.Empty;
        var drafts = new List<NotificationDraft>();
        if (subject.Id != actor.Id)
        {
            drafts.Add(new NotificationDraft(
                subject.Id,
                NotificationType.RiskEventRecorded,
                "Sự kiện rủi ro được ghi nhận",
                ClampBody($"Sự kiện rủi ro {riskEvent.Code} ({riskEvent.Severity}){penaltyText} đã được ghi nhận: {riskEvent.Description}"),
                NotificationSeverity.Important,
                NotificationRelatedType.RiskEvent,
                riskEvent.Id,
                actor.Id));
        }

        if (subject.ManagerId is { } managerId && managerId != actor.Id && managerId != subject.Id)
        {
            drafts.Add(new NotificationDraft(
                managerId,
                NotificationType.RiskEventRecorded,
                "Nhân sự phát sinh sự kiện rủi ro",
                ClampBody($"{subject.FullName} vừa bị ghi nhận sự kiện rủi ro {riskEvent.Code} ({riskEvent.Severity}){penaltyText}."),
                NotificationSeverity.Important,
                NotificationRelatedType.RiskEvent,
                riskEvent.Id,
                actor.Id));
        }

        return drafts;
    }

    /// <summary>A confirmed risk event was reversed and its penalty removed.</summary>
    public static List<NotificationDraft> RiskEventReversed(RiskEvent riskEvent, Employee subject, Employee actor)
    {
        var drafts = new List<NotificationDraft>();
        if (subject.Id != actor.Id)
        {
            drafts.Add(new NotificationDraft(
                subject.Id,
                NotificationType.RiskEventReversed,
                "Sự kiện rủi ro đã được đảo",
                ClampBody($"Sự kiện rủi ro {riskEvent.Code} đã được {actor.FullName} gỡ điểm phạt. Lý do: {riskEvent.ReversalReason}"),
                NotificationSeverity.Normal,
                NotificationRelatedType.RiskEvent,
                riskEvent.Id,
                actor.Id));
        }

        if (subject.ManagerId is { } managerId && managerId != actor.Id && managerId != subject.Id)
        {
            drafts.Add(new NotificationDraft(
                managerId,
                NotificationType.RiskEventReversed,
                "Đã gỡ điểm phạt sự kiện rủi ro",
                ClampBody($"Sự kiện rủi ro {riskEvent.Code} của {subject.FullName} đã được đảo bởi {actor.FullName}."),
                NotificationSeverity.Normal,
                NotificationRelatedType.RiskEvent,
                riskEvent.Id,
                actor.Id));
        }

        return drafts;
    }

    /// <summary>QA raised a quality flag (directly or via a failed 5% sample without immediate rework).</summary>
    public static List<NotificationDraft> QualityFlagRaised(QualityFlag flag, Work work, Employee subject, Employee qaActor)
    {
        var drafts = new List<NotificationDraft>();
        if (subject.Id != qaActor.Id)
        {
            drafts.Add(new NotificationDraft(
                subject.Id,
                NotificationType.QualityFlagRaised,
                "Kết quả bị gắn cờ chất lượng QA",
                ClampBody($"QA ({qaActor.FullName}) đã gắn cờ chất lượng ({flag.Severity}) trên công việc {Describe(work)}: {flag.Reason}"),
                NotificationSeverity.Important,
                NotificationRelatedType.QualityFlag,
                flag.Id,
                qaActor.Id));
        }

        if (subject.ManagerId is { } managerId && managerId != qaActor.Id && managerId != subject.Id)
        {
            drafts.Add(new NotificationDraft(
                managerId,
                NotificationType.QualityFlagRaised,
                "Nhân sự có cờ chất lượng QA",
                ClampBody($"Công việc {Describe(work)} của {subject.FullName} bị QA gắn cờ chất lượng ({flag.Severity}): {flag.Reason}"),
                NotificationSeverity.Normal,
                NotificationRelatedType.QualityFlag,
                flag.Id,
                qaActor.Id));
        }

        return drafts;
    }

    /// <summary>An employee filed an appeal on a rejected result submission.</summary>
    public static NotificationDraft? AppealFiled(Appeal appeal, Work work, Employee filer)
    {
        if (filer.ManagerId is not { } managerId || managerId == filer.Id)
        {
            return null;
        }

        return new NotificationDraft(
            managerId,
            NotificationType.AppealFiled,
            "Khiếu nại kết quả mới cần xử lý",
            ClampBody($"{filer.FullName} đã gửi khiếu nại kết quả cho công việc {Describe(work)}: {appeal.Reason}"),
            NotificationSeverity.Important,
            NotificationRelatedType.Appeal,
            appeal.Id,
            filer.Id);
    }

    /// <summary>A manager/branch manager decided an appeal (ACCEPT / ADJUST / REJECT).</summary>
    public static NotificationDraft AppealDecided(Appeal appeal, Work work, Employee resolver, string decision, string? note)
    {
        var (title, outcome) = decision switch
        {
            "ACCEPT" => ("Khiếu nại kết quả được chấp nhận", "đã được chấp nhận"),
            "ADJUST" => ("Khiếu nại yêu cầu bổ sung chứng từ", "được yêu cầu bổ sung hồ sơ"),
            _ => ("Khiếu nại kết quả bị từ chối", "đã bị từ chối"),
        };

        var body = $"Khiếu nại trên công việc {Describe(work)} {outcome} bởi {resolver.FullName}.";
        if (!string.IsNullOrWhiteSpace(note))
        {
            body += $" Ghi chú: {note}";
        }

        return new NotificationDraft(
            appeal.EmployeeId,
            NotificationType.AppealDecided,
            title,
            ClampBody(body),
            NotificationSeverity.Important,
            NotificationRelatedType.Appeal,
            appeal.Id,
            resolver.Id);
    }

    /// <summary>Department calibration submitted to Branch Manager for executive approval.</summary>
    public static List<NotificationDraft> CalibrationSubmitted(
        Department department,
        EvaluationPeriod period,
        Employee submitter,
        IEnumerable<Guid> branchManagerEmployeeIds)
    {
        return branchManagerEmployeeIds
            .Where(id => id != submitter.Id)
            .Distinct()
            .Select(id => new NotificationDraft(
                id,
                NotificationType.CalibrationSubmitted,
                "Bảng hiệu chuẩn phòng ban chờ phê duyệt",
                ClampBody($"{submitter.FullName} đã trình duyệt bảng hiệu chuẩn xếp hạng của {department.Name} cho kỳ {period.Code} - {period.Name}."),
                NotificationSeverity.Important,
                NotificationRelatedType.EvaluationPeriod,
                period.Id,
                submitter.Id))
            .ToList();
    }

    /// <summary>Branch Manager approved or rejected a department's calibration.</summary>
    public static List<NotificationDraft> CalibrationDecided(
        Department department,
        EvaluationPeriod period,
        Employee reviewer,
        IEnumerable<Guid> deptManagerEmployeeIds,
        bool approved,
        string? noteOrReason)
    {
        var title = approved
            ? "Xếp hạng phòng ban đã được phê duyệt"
            : "Yêu cầu hiệu chuẩn lại xếp hạng phòng ban";
        var body = approved
            ? $"{reviewer.FullName} đã phê duyệt kết quả xếp hạng của {department.Name} trong kỳ {period.Code}."
            : $"{reviewer.FullName} yêu cầu hiệu chuẩn lại bảng xếp hạng {department.Name} trong kỳ {period.Code}.";
        if (!string.IsNullOrWhiteSpace(noteOrReason))
        {
            body += $" Ý kiến: {noteOrReason.Trim()}";
        }

        return deptManagerEmployeeIds
            .Where(id => id != reviewer.Id)
            .Distinct()
            .Select(id => new NotificationDraft(
                id,
                NotificationType.CalibrationDecided,
                title,
                ClampBody(body),
                NotificationSeverity.Important,
                NotificationRelatedType.EvaluationPeriod,
                period.Id,
                reviewer.Id))
            .ToList();
    }

    /// <summary>Evaluation period finalized &amp; locked: evaluated employees receive RESULT_PUBLISHED.</summary>
    public static List<NotificationDraft> PeriodResultsPublished(
        EvaluationPeriod period,
        Employee reviewer,
        IEnumerable<Guid> evaluatedEmployeeIds)
    {
        return evaluatedEmployeeIds
            .Where(id => id != reviewer.Id)
            .Distinct()
            .Select(id => new NotificationDraft(
                id,
                NotificationType.ResultPublished,
                "Kết quả đánh giá kỳ đã được công bố",
                ClampBody($"Kết quả đánh giá hiệu suất kỳ {period.Code} - {period.Name} đã được phê chuẩn và công bố chính thức."),
                NotificationSeverity.Important,
                NotificationRelatedType.EvaluationPeriod,
                period.Id,
                reviewer.Id))
            .ToList();
    }

    /// <summary>Department manager completed an employee's evaluation (DRAFT -> CALCULATED) -> the employee is told to review it.</summary>
    public static List<NotificationDraft> EvaluationCalculated(
        Employee employee,
        EvaluationPeriod period,
        Employee reviewer)
    {
        if (employee.Id == reviewer.Id)
        {
            return [];
        }

        return
        [
            new NotificationDraft(
                employee.Id,
                NotificationType.EvaluationCalculated,
                "Kết quả đánh giá đã sẵn sàng",
                ClampBody($"Trưởng phòng đã hoàn tất đánh giá kỳ {period.Code} của bạn. Bạn có thể xem tại Đánh giá của tôi."),
                NotificationSeverity.Important,
                NotificationRelatedType.EvaluationPeriod,
                period.Id,
                reviewer.Id),
        ];
    }

    /// <summary>Branch Manager returned an employee's evaluation to CALCULATED -> department managers hear why.</summary>
    public static List<NotificationDraft> EvaluationReturned(
        Employee employee,
        EvaluationPeriod period,
        Employee reviewer,
        IEnumerable<Guid> deptManagerEmployeeIds,
        string reason)
    {
        return deptManagerEmployeeIds
            .Where(id => id != reviewer.Id)
            .Distinct()
            .Select(id => new NotificationDraft(
                id,
                NotificationType.EvaluationReturned,
                "Đánh giá nhân viên bị trả lại",
                ClampBody($"{reviewer.FullName} đã trả lại đánh giá của {employee.FullName} trong kỳ {period.Code}. Lý do: {reason}"),
                NotificationSeverity.Important,
                NotificationRelatedType.EvaluationPeriod,
                period.Id,
                reviewer.Id))
            .ToList();
    }

    /// <summary>Branch Manager finalized an employee's evaluation -> department managers are told.</summary>
    public static List<NotificationDraft> EvaluationFinalized(
        Employee employee,
        EvaluationPeriod period,
        Employee reviewer,
        IEnumerable<Guid> deptManagerEmployeeIds)
    {
        return deptManagerEmployeeIds
            .Where(id => id != reviewer.Id)
            .Distinct()
            .Select(id => new NotificationDraft(
                id,
                NotificationType.EvaluationFinalized,
                "Đánh giá nhân viên đã hoàn tất",
                ClampBody($"{reviewer.FullName} đã hoàn tất đánh giá của {employee.FullName} trong kỳ {period.Code}."),
                NotificationSeverity.Normal,
                NotificationRelatedType.EvaluationPeriod,
                period.Id,
                reviewer.Id))
            .ToList();
    }

    /// <summary>Campaign plan approved or rejected.</summary>
    public static List<NotificationDraft> CampaignDecided(
        BusinessCampaign campaign,
        Employee reviewer,
        IEnumerable<Guid> participantIds,
        bool approved,
        string? note)
    {
        var title = approved ? "Chiến dịch kinh doanh đã được phê duyệt" : "Chiến dịch kinh doanh bị từ chối";
        var body = approved
            ? $"Chiến dịch {campaign.Code} - {campaign.Name} đã được {reviewer.FullName} phê duyệt và chuyển sang triển khai."
            : $"Chiến dịch {campaign.Code} - {campaign.Name} đã bị {reviewer.FullName} từ chối.";
        if (!string.IsNullOrWhiteSpace(note))
        {
            body += $" Ghi chú: {note.Trim()}";
        }

        return participantIds
            .Where(id => id != reviewer.Id)
            .Distinct()
            .Select(id => new NotificationDraft(
                id,
                NotificationType.CampaignDecided,
                title,
                ClampBody(body),
                approved ? NotificationSeverity.Normal : NotificationSeverity.Important,
                NotificationRelatedType.Campaign,
                campaign.Id,
                reviewer.Id))
            .ToList();
    }

    /// <summary>Evaluation profile approved or rejected by Branch Manager -> System Admins hear the decision.</summary>
    public static List<NotificationDraft> EvaluationProfileDecided(
        EvaluationProfile profile,
        Guid? reviewerEmployeeId,
        IEnumerable<Guid> adminEmployeeIds,
        bool approved,
        string? reason = null)
    {
        var title = approved ? "Hồ sơ đánh giá đã được phê duyệt" : "Hồ sơ đánh giá bị từ chối";
        var body = approved
            ? $"Hồ sơ đánh giá {profile.Code} - {profile.Name} (v{profile.Version}) đã được phê duyệt và kích hoạt."
            : $"Hồ sơ đánh giá {profile.Code} - {profile.Name} (v{profile.Version}) bị từ chối: {reason}";

        return adminEmployeeIds
            .Where(id => id != reviewerEmployeeId)
            .Distinct()
            .Select(id => new NotificationDraft(
                id,
                NotificationType.EvaluationProfileDecided,
                title,
                ClampBody(body),
                NotificationSeverity.Important,
                NotificationRelatedType.EvaluationProfile,
                profile.Id,
                reviewerEmployeeId))
            .ToList();
    }

    /// <summary>Security or unhandled runtime exception alert routed to active System Administrators.</summary>
    public static List<NotificationDraft> SystemAlertForAdmins(
        IEnumerable<Guid> adminEmployeeIds,
        string title,
        string body,
        Guid? excludeEmployeeId = null)
    {
        return adminEmployeeIds
            .Where(id => id != excludeEmployeeId)
            .Distinct()
            .Select(id => new NotificationDraft(
                id,
                NotificationType.SystemAlert,
                ClampTitle(title),
                ClampBody(body),
                NotificationSeverity.Important))
            .ToList();
    }

    private static string DescribeMismatchReason(string? reason) => reason switch
    {
        "EMPLOYEE_MISMATCH" => "sai mã nhân viên",
        "PRODUCT_MISMATCH" => "sai mã sản phẩm",
        "CUSTOMER_MISMATCH" => "sai mã CIF khách hàng",
        "TRANSACTION_BEFORE_HANDOVER" => "giao dịch trước thời điểm bàn giao",
        "REFERRER_MISMATCH" => "sai người giới thiệu",
        "METRIC_TYPE_MISMATCH" => "lệch loại chỉ số",
        _ => reason ?? "không khớp",
    };

    private static string ClampTitle(string value) => value.Length > MaxTitleLength ? value[..MaxTitleLength] : value;

    private static string ClampBody(string value) => value.Length > MaxBodyLength ? value[..MaxBodyLength] : value;

    private static string Describe(Work work) => $"{work.Code} - {work.Title}";

    private static NotificationDraft ForHandover(Guid recipientId, NotificationType type, string title, string body, NotificationSeverity severity, TaskHandover handover)
        => new(recipientId, type, title, body, severity, NotificationRelatedType.Handover, handover.Id);

    private static NotificationDraft ForWork(Guid recipientId, string title, string body, NotificationSeverity severity, Work work)
        => new(recipientId, NotificationType.WorkOverdue, title, body, severity, NotificationRelatedType.Work, work.Id);
}
