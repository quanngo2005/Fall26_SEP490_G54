namespace G54.BLL.Common.Import.Pipeline;

/// <summary>One validation error attached to an import row. <c>Code</c> is a stable value from <c>ErrorCodes</c>.</summary>
public sealed record ImportError(string Column, string Code, string Message);
