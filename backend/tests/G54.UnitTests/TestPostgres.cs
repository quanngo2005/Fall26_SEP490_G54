using Testcontainers.PostgreSql;

namespace G54.UnitTests;

/// <summary>
/// Postgres admin connection shared by every database-backed test. Uses <c>G54_TEST_POSTGRES</c> when set; otherwise starts one
/// <c>postgres:16-alpine</c> container for the whole test run (Testcontainers removes it afterwards). When neither is possible the
/// tests skip locally, but fail when <c>CI=true</c> so a pipeline cannot pass without running them.
/// </summary>
public static class TestPostgres
{
    public const string ConnectionVariable = "G54_TEST_POSTGRES";
    public const string DisableContainerVariable = "G54_TEST_TESTCONTAINERS";

    private static readonly Lazy<string?> Admin = new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Connection string able to create databases, or <c>null</c> when no Postgres is available.</summary>
    public static string? AdminConnectionString => Admin.Value;

    public static string SkipReason =>
        $"Set {ConnectionVariable} to a Postgres admin connection string, or start Docker so Testcontainers can provide one.";

    private static string? Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (Environment.GetEnvironmentVariable(DisableContainerVariable) != "0")
        {
            try
            {
                var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
                container.StartAsync().GetAwaiter().GetResult();
                return container.GetConnectionString();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (IsContinuousIntegration)
                {
                    throw new InvalidOperationException(
                        $"CI requires Postgres for the database tests, but Testcontainers could not start one. {SkipReason}",
                        exception);
                }
            }
        }

        return IsContinuousIntegration
            ? throw new InvalidOperationException($"CI requires Postgres for the database tests. {SkipReason}")
            : null;
    }

    private static bool IsContinuousIntegration =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
}
