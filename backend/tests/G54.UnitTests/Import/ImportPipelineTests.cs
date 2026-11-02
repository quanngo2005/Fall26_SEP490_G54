using G54.BLL.Common.Constants;
using G54.BLL.Common.Import.Pipeline;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Rules;
using G54.BLL.Common.Import.Template;

namespace G54.UnitTests.Import;

/// <summary>
/// Exercises the generic Template Method pipeline (read -> map -> context -> rules -> classify) with a tiny
/// fake definition, independent of any business import.
/// </summary>
public sealed class ImportPipelineTests
{
    private sealed class FakeRow
    {
        public string? Code { get; set; }
    }

    private sealed class FakeContext
    {
        public required HashSet<string> ExistingCodes { get; init; }
    }

    private sealed class FakeDefinition : ImportDefinition<FakeRow, FakeContext>
    {
        public override string Type => "fake";

        protected override void ConfigureTemplate(TemplateBuilder<FakeRow> builder) =>
            builder.Column("Code*", (r, v) => r.Code = v?.Trim().ToUpperInvariant());

        protected override IReadOnlyList<IImportRule<FakeRow>> CreateRules() =>
        [
            new RequiredRule<FakeRow>("Code*", r => r.Code),
            new DuplicateInFileRule<FakeRow, string>("Code*", r => r.Code, ErrorCodes.DataImportDuplicateCode, "dup {0}"),
        ];

        protected override Task<FakeContext> LoadContextAsync(IReadOnlyList<FakeRow> rows, CancellationToken cancellationToken) =>
            Task.FromResult(new FakeContext { ExistingCodes = ["EXIST1"] });

        protected override void Classify(ImportRow<FakeRow> row, FakeContext context) =>
            row.Action = context.ExistingCodes.Contains(row.Data.Code!) ? ImportAction.Update : ImportAction.Create;

        protected override Task<ImportCommitResult> CommitRowsAsync(
            IReadOnlyList<ImportRow<FakeRow>> rows, FakeContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ImportCommitResult(rows.Count, 0, 0, []));

        protected override Dictionary<string, string?> ToDictionary(FakeRow row) => new() { ["Code*"] = row.Code };
    }

    private static ExcelSheetResult Sheet(params string?[] codes) => new(
        ["Code*"],
        codes.Select((c, i) => new RawRow(i + 2, new List<string?> { c })).ToList());

    [Fact]
    public async Task PreviewAsync_MissingRequiredField_IsInvalid()
    {
        var result = await new FakeDefinition().PreviewAsync(Sheet((string?)null), CancellationToken.None);

        Assert.Equal("INVALID", result.Rows[0].Action);
        Assert.Contains(result.Rows[0].Errors, e => e.Code == ErrorCodes.RequiredField);
    }

    [Fact]
    public async Task PreviewAsync_DuplicateCode_FlagsBothRows()
    {
        var result = await new FakeDefinition().PreviewAsync(Sheet("NEW1", "NEW1"), CancellationToken.None);

        Assert.All(result.Rows, r => Assert.Equal("INVALID", r.Action));
        Assert.All(result.Rows, r => Assert.Contains(r.Errors, e => e.Code == ErrorCodes.DataImportDuplicateCode));
    }

    [Fact]
    public async Task PreviewAsync_ClassifiesCreateAndUpdate()
    {
        var result = await new FakeDefinition().PreviewAsync(Sheet("NEW1", "EXIST1"), CancellationToken.None);

        Assert.Equal("CREATE", result.Rows[0].Action);
        Assert.Equal("UPDATE", result.Rows[1].Action);
        Assert.Equal(2, result.Summary.Valid);
        Assert.Equal(0, result.Summary.Invalid);
    }

    [Fact]
    public async Task CommitAsync_AnyInvalidRow_ThrowsDataImportHasErrors()
    {
        var definition = new FakeDefinition();

        var exception = await Assert.ThrowsAsync<G54.BLL.Common.Exceptions.BusinessRuleException>(
            () => definition.CommitAsync(Sheet("NEW1", "NEW1"), CancellationToken.None));

        Assert.Equal(ErrorCodes.DataImportHasErrors, exception.ErrorCode);
        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task CommitAsync_AllValid_DelegatesToCommitHook()
    {
        var definition = new FakeDefinition();

        var result = await definition.CommitAsync(Sheet("NEW1", "EXIST1"), CancellationToken.None);

        Assert.Equal(2, result.Created);
    }
}
