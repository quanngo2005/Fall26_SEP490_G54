namespace G54.UnitTests;

/// <summary>
/// Real business sample workbooks (<c>02_Nhan_su_To_chuc.xlsx</c>, <c>06_Giao_dich.xlsx</c>) are not committed: copy them from
/// <c>Project/Documents/Template/excels/</c> into <c>backend/tests/G54.UnitTests/TestData/</c> to run these tests. Without them
/// the tests are skipped (locally and on CI).
/// </summary>
public static class SampleFiles
{
    public static string PathOf(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    /// <summary>The skip reason when any of <paramref name="fileNames"/> is missing, otherwise <c>null</c>.</summary>
    public static string? MissingReason(params string[] fileNames)
    {
        var missing = fileNames.Where(f => !File.Exists(PathOf(f))).ToList();
        return missing.Count == 0
            ? null
            : $"Sample file(s) {string.Join(", ", missing)} not found in TestData/ (not committed; copy them from Project/Documents/Template/excels/).";
    }
}

/// <summary>A fact that needs real sample workbooks in <c>TestData/</c>; skipped when they are absent.</summary>
public sealed class SampleFileFactAttribute : FactAttribute
{
    public SampleFileFactAttribute(params string[] fileNames)
    {
        if (SampleFiles.MissingReason(fileNames) is { } reason)
        {
            Skip = reason;
        }
    }
}
