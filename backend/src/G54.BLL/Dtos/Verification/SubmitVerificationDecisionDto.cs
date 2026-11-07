using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Verification;

public sealed record AuditChecklistResultDto(
    string StandardId,
    bool IsPassed);

public sealed record SubmitVerificationDecisionDto
{
    [Required]
    [RegularExpression("^(APPROVED|REVISION_REQUIRED|REJECTED)$", ErrorMessage = "Quyết định thẩm định chỉ có thể là APPROVED, REVISION_REQUIRED hoặc REJECTED.")]
    public string Decision { get; init; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Ý kiến chỉ đạo không được vượt quá 2000 ký tự.")]
    public string? Feedback { get; init; }

    public DateTimeOffset? ResubmitDeadline { get; init; }

    public List<AuditChecklistResultDto>? ChecklistResults { get; init; }
}
