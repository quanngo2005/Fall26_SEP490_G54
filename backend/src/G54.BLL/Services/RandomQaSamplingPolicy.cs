using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>
/// BR-32: the system samples 5% of approved results for QA re-verification. The sample is drawn on the manual verification
/// path (<c>VerificationService</c>), which per the 25/10 decisions is the approval of work outside the WorkType list; matched
/// transactions are auto-approved and never reach it. There is no system-parameter table yet, so the rate is a constant.
/// </summary>
public sealed class RandomQaSamplingPolicy : IQaSamplingPolicy
{
    public const double SampleRate = 0.05;

    public bool ShouldSample(Work work) => Random.Shared.NextDouble() < SampleRate;
}
