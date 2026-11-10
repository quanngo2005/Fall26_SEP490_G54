using System.IO.Compression;
using G54.BLL.Common.Exceptions;

namespace G54.BLL.Common.Import.Reading;

/// <summary>
/// Zip-bomb guard shared by every Excel reader: an .xlsx is a zip, and a small upload can inflate to gigabytes
/// once opened (CVE-style denial of service) unless the uncompressed size is checked first.
/// </summary>
internal static class ExcelUploadGuard
{
    private const long MaxUncompressedBytes = 100L * 1024 * 1024;

    public static void EnsureUncompressedSizeWithinLimit(Stream excel)
    {
        if (!excel.CanSeek)
        {
            throw new ValidationException("File Excel không đọc được.");
        }

        var start = excel.Position;
        try
        {
            using var zip = new ZipArchive(excel, ZipArchiveMode.Read, leaveOpen: true);
            var total = zip.Entries.Sum(e => e.Length);
            if (total > MaxUncompressedBytes)
            {
                throw new ValidationException("File Excel quá lớn sau khi giải nén.");
            }
        }
        catch (InvalidDataException)
        {
            throw new ValidationException("File không phải định dạng .xlsx hợp lệ.");
        }
        finally
        {
            excel.Position = start;
        }
    }
}
