using G54.BLL.Services;

namespace G54.Api.Common;

/// <summary>
/// Shared ".xlsx" upload shape check for import endpoints: non-empty, within the configured size cap, the
/// right extension and a matching magic-byte signature. Reused by <see cref="Controllers.DataImportsController"/>
/// and <see cref="Controllers.CampaignCustomersController"/> so both enforce the same rule.
/// </summary>
public static class XlsxUploadValidator
{
    private const string XlsxExtension = ".xlsx";

    public static async Task<bool> IsAcceptableAsync(IFormFile? file, int maxFileMb)
    {
        var maxBytes = maxFileMb * 1024L * 1024;
        return file is not null && file.Length > 0 && file.Length <= maxBytes && await IsValidXlsxAsync(file);
    }

    private static async Task<bool> IsValidXlsxAsync(IFormFile file)
    {
        if (!string.Equals(Path.GetExtension(file.FileName), XlsxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var header = new byte[EvidenceFileValidator.HeaderLength];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header);
        return EvidenceFileValidator.HasValidSignature(XlsxExtension, header.AsSpan(0, read));
    }
}
