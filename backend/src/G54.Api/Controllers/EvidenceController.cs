using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace G54.Api.Controllers;

/// <summary>
/// Evidence file download and preview. Access is decided by <see cref="IEvidenceService"/>: the submitter, a verifier whose
/// scope covers the submitter's department, and administrators. Errors are raised as AppException and mapped by
/// GlobalExceptionHandler.
/// </summary>
[ApiController]
[Authorize]
[Route("evidence")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class EvidenceController(IEvidenceService evidenceService) : ControllerBase
{
    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var file = await evidenceService.OpenAsync(User.GetRequiredUserId(), id, cancellationToken);
        ApplyPrivateHeaders();
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    /// <summary>Inline for PDF and images; Office files fall back to a download because browsers cannot render them.</summary>
    [HttpGet("{id:guid}/preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview(Guid id, CancellationToken cancellationToken)
    {
        var file = await evidenceService.OpenAsync(User.GetRequiredUserId(), id, cancellationToken);
        ApplyPrivateHeaders();
        if (!file.CanPreview)
        {
            return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
        }

        // Sandbox so an inline document can never run script in the API origin.
        Response.Headers.ContentSecurityPolicy = "sandbox";
        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(file.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        return File(file.Content, file.ContentType, enableRangeProcessing: true);
    }

    private void ApplyPrivateHeaders() => Response.Headers.CacheControl = "private, no-store";
}
