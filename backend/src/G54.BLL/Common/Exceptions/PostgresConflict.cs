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

    private static bool HasSqlState(Exception exception, string sqlState)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return true;
            }
        }

        return false;
    }
}
