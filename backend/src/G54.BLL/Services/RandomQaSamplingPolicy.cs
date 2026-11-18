using G54.BLL.Common;
using G54.DAL.Entities;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// BR-32: samples a configurable percentage (<c>qaSampleRatePercent</c>, default 5%) of manually approved results for QA re-verification.
/// </summary>
public sealed class RandomQaSamplingPolicy(IOptions<SystemParametersOptions>? parameters = null) : IQaSamplingPolicy
{
    public const double SampleRate = 0.05;

    public bool ShouldSample(Work work)
    {
        var rate = parameters is not null
            ? Math.Clamp((double)parameters.Value.QaSampleRatePercent / 100.0, 0.0, 1.0)
            : SampleRate;
        return rate > 0.0 && Random.Shared.NextDouble() < rate;
    }
}
