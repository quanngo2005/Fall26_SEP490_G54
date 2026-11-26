namespace G54.BLL.Common;

/// <summary>Call recording upload settings (docs/api/CALL_RECORDING_AI_TRIAGE_CONTRACT.md §3).</summary>
public sealed class CallRecordingOptions
{
    public const string SectionName = "CallRecording";

    /// <summary>Hard cap of the upload endpoint; <see cref="MaxBytes"/> may only lower it.</summary>
    public const long MaxUploadBytes = 50 * 1024 * 1024;

    /// <summary>When true, an upload whose product code is neither given nor known from the work type is rejected.</summary>
    public bool RequireProductCode { get; set; }

    public long MaxBytes { get; set; } = MaxUploadBytes;
}
