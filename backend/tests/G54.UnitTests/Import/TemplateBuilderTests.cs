using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Template;

namespace G54.UnitTests.Import;

public sealed class TemplateBuilderTests
{
    private sealed class SampleRow
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? Note { get; set; }
    }

    private static ImportTemplate<SampleRow> BuildTemplate() => new TemplateBuilder<SampleRow>()
        .Column("Code*", (r, v) => r.Code = v)
        .Column("Name*", (r, v) => r.Name = v)
        .Column("Note", (r, v) => r.Note = v, required: false)
        .Build();

    [Fact]
    public void BuildHeaderIndex_MissingRequiredColumn_ThrowsImportTemplateInvalid()
    {
        var template = BuildTemplate();

        var exception = Assert.Throws<BusinessRuleException>(() => template.BuildHeaderIndex(["Code*"]));
        Assert.Equal("IMPORT_TEMPLATE_INVALID", exception.ErrorCode);
    }

    [Fact]
    public void MapRow_ReorderedColumns_MapsByHeaderName()
    {
        var template = BuildTemplate();
        // File has the columns in a different order than the template declared them.
        var headerIndex = template.BuildHeaderIndex(["Name*", "Code*", "Note"]);
        var raw = new RawRow(2, ["Nguyen Van A", "NV001", "demo"]);

        var row = template.MapRow(raw, headerIndex);

        Assert.Equal("NV001", row.Code);
        Assert.Equal("Nguyen Van A", row.Name);
        Assert.Equal("demo", row.Note);
    }

    [Fact]
    public void MapRow_OptionalColumnAbsentFromFile_LeavesNull()
    {
        var template = BuildTemplate();
        var headerIndex = template.BuildHeaderIndex(["Code*", "Name*"]);
        var raw = new RawRow(2, ["NV001", "Nguyen Van A"]);

        var row = template.MapRow(raw, headerIndex);

        Assert.Equal("NV001", row.Code);
        Assert.Null(row.Note);
    }

    [Fact]
    public void MapRow_HeaderMatchIsCaseInsensitiveAndIgnoresAsterisk()
    {
        var template = BuildTemplate();
        var headerIndex = template.BuildHeaderIndex([" code ", " name "]);
        var raw = new RawRow(2, ["NV001", "Nguyen Van A"]);

        var row = template.MapRow(raw, headerIndex);

        Assert.Equal("NV001", row.Code);
        Assert.Equal("Nguyen Van A", row.Name);
    }
}
