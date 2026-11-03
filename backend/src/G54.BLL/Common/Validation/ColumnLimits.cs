using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Common.Validation;

/// <summary>
/// Limits of a database column, read from the EF model so the validation message and the column share one source.
/// A property without the limit is a programming error: there is no fallback value.
/// </summary>
internal static class ColumnLimits
{
    public static int MaxLength<TEntity>(this DbContext db, string property) =>
        Find<TEntity>(db, property).GetMaxLength()
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name}.{property} has no maximum length.");

    /// <summary>Exclusive upper bound of a decimal column: 10 to the power of (precision - scale).</summary>
    public static decimal DecimalUpperBound<TEntity>(this DbContext db, string property)
    {
        var column = Find<TEntity>(db, property);
        var precision = column.GetPrecision()
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name}.{property} has no precision.");
        var scale = column.GetScale() ?? 0;
        return (decimal)Math.Pow(10, precision - scale);
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IProperty Find<TEntity>(DbContext db, string property) =>
        db.Model.FindEntityType(typeof(TEntity))?.FindProperty(property)
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name}.{property} is not mapped.");
}
