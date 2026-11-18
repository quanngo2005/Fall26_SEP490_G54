using G54.UnitTests.Import;

namespace G54.UnitTests;

/// <summary>
/// Sample workbooks for the import tests. The business samples (<c>02_Nhan_su_To_chuc.xlsx</c>, <c>06_Giao_dich.xlsx</c>)
/// are confidential: they are never committed and their content is never copied into code. Tests use synthetic
/// workbooks with the same layout and the same kinds of invalid rows (<see cref="EmployeeOrgSampleWorkbook"/>,
/// <see cref="TransactionSampleWorkbook"/>), so they always run, locally and on CI.
/// When a real sample is copied into <c>backend/tests/G54.UnitTests/TestData/</c>, <see cref="Sources"/> adds a
/// <see cref="Real"/> case that checks only its shape (row counts, invalid rows, error codes); without the file that
/// case does not exist, so nothing is skipped.
/// </summary>
public static class SampleFiles
{
    public const string EmployeeOrg = "02_Nhan_su_To_chuc.xlsx";
    public const string Transactions = "06_Giao_dich.xlsx";
    public const string Synthetic = "synthetic";
    public const string Real = "real";

    public static string PathOf(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    /// <summary>Theory data: always <see cref="Synthetic"/>, plus <see cref="Real"/> when the real file is in TestData/.</summary>
    public static TheoryData<string> Sources(string fileName)
    {
        var data = new TheoryData<string> { Synthetic };
        if (File.Exists(PathOf(fileName)))
        {
            data.Add(Real);
        }

        return data;
    }

    public static byte[] Read(string fileName, string source = Synthetic) => source switch
    {
        Real => File.ReadAllBytes(PathOf(fileName)),
        _ => fileName switch
        {
            EmployeeOrg => EmployeeOrgSampleWorkbook.Build().ToArray(),
            Transactions => TransactionSampleWorkbook.Build(),
            _ => throw new ArgumentException($"No synthetic workbook for '{fileName}'.", nameof(fileName)),
        },
    };

    public static Stream Open(string fileName, string source = Synthetic) => new MemoryStream(Read(fileName, source));
}
