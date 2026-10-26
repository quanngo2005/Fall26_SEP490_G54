using Testcontainers.Redis;

namespace G54.UnitTests;

/// <summary>Redis for the session-store tests: <c>G54_TEST_REDIS</c> when set, otherwise one <c>redis:7-alpine</c> container per run.</summary>
public static class TestRedis
{
    public const string ConnectionVariable = "G54_TEST_REDIS";

    private static readonly Lazy<string?> Connection = new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string? ConnectionString => Connection.Value;

    public static string SkipReason =>
        $"Set {ConnectionVariable} to a Redis connection string, or start Docker so Testcontainers can provide one.";

    private static string? Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var continuousIntegration = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        if (Environment.GetEnvironmentVariable(TestPostgres.DisableContainerVariable) != "0")
        {
            try
            {
                var container = new RedisBuilder("redis:7-alpine").Build();
                container.StartAsync().GetAwaiter().GetResult();
                return container.GetConnectionString();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (continuousIntegration)
                {
                    throw new InvalidOperationException($"CI requires Redis for the session-store tests, but Testcontainers could not start one. {SkipReason}", exception);
                }
            }
        }

        return continuousIntegration
            ? throw new InvalidOperationException($"CI requires Redis for the session-store tests. {SkipReason}")
            : null;
    }
}
