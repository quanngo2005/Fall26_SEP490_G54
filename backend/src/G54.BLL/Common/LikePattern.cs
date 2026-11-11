namespace G54.BLL.Common;

/// <summary>Builds the pattern for a "contains" search with <c>EF.Functions.ILike(column, pattern, LikePattern.Escape)</c>.</summary>
public static class LikePattern
{
    /// <summary>The escape character that <see cref="Contains"/> puts before <c>%</c>, <c>_</c> and itself.</summary>
    public const string Escape = "\\";

    /// <summary>Wraps the text in <c>%</c> and escapes its own wildcards so they match literally.</summary>
    public static string Contains(string text) =>
        "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
