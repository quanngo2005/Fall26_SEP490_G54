using Npgsql;

namespace G54.BLL.Common.Exceptions;

internal static class PostgresConflict
{
    /// <summary>Npgsql's execution strategy may wrap a serialization failure in InvalidOperationException.</summary>
    public static bool IsSerializationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUniqueViolation(Exception exception) => HasSqlState(exception, PostgresErrorCodes.UniqueViolation);

    public static bool IsForeignKeyViolation(Exception exception) => HasSqlState(exception, PostgresErrorCodes.ForeignKeyViolation);

    public static bool IsCheckViolation(Exception exception) => HasSqlState(exception, PostgresErrorCodes.CheckViolation);

    /// <summary>A trigger's <c>RAISE EXCEPTION</c> without an explicit error code (SQLSTATE P0001).</summary>
    public static bool IsRaisedException(Exception exception) => HasSqlState(exception, PostgresErrorCodes.RaiseException);

    /// <summary>The message the database raised, or null when the exception chain holds no PostgreSQL error.</summary>
    public static string? DatabaseMessage(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres.MessageText;
            }
        }

        return null;
    }

    private static bool HasSqlState(Exception exception, string sqlState)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == sqlState)
            {
                return true;
            }
        }

        return false;
    }
}
