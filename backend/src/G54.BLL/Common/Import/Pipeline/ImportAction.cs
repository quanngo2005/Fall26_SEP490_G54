namespace G54.BLL.Common.Import.Pipeline;

/// <summary>Classification of an import row once rules have run.</summary>
public enum ImportAction
{
    Create,
    Update,
    Unchanged,
    Invalid,
}
