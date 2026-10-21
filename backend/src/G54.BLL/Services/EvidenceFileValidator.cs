namespace G54.BLL.Services;

/// <summary>
/// Confirms that an evidence file really is the type its extension claims, and maps the extension to the content type
/// that is stored and served. The client-supplied <c>Content-Type</c> is never trusted.
/// </summary>
public static class EvidenceFileValidator
{
    private static readonly byte[] Pdf = "%PDF"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly Dictionary<string, (string ContentType, byte[] Signature, bool Inline)> Types =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ("application/pdf", Pdf, true),
            [".png"] = ("image/png", Png, true),
            [".jpg"] = ("image/jpeg", Jpeg, true),
            [".jpeg"] = ("image/jpeg", Jpeg, true),
            [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Zip, false),
            [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", Zip, false),
            [".xls"] = ("application/vnd.ms-excel", Ole, false),
            [".doc"] = ("application/msword", Ole, false),
        };

    /// <summary>Number of leading bytes <see cref="HasValidSignature"/> needs.</summary>
    public static int HeaderLength => Ole.Length;

    public static bool IsKnownExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) && Types.ContainsKey(extension);

    /// <summary>Content type for a known extension; <c>application/octet-stream</c> otherwise.</summary>
    public static string ContentTypeFor(string? extension) =>
        extension is not null && Types.TryGetValue(extension, out var type) ? type.ContentType : "application/octet-stream";

    /// <summary>True when the browser may render the file inline (PDF and images); Office files are always downloads.</summary>
    public static bool CanPreview(string? extension) =>
        extension is not null && Types.TryGetValue(extension, out var type) && type.Inline;

    /// <summary>True when <paramref name="header"/> starts with the signature expected for <paramref name="extension"/>.</summary>
    public static bool HasValidSignature(string? extension, ReadOnlySpan<byte> header) =>
        extension is not null
        && Types.TryGetValue(extension, out var type)
        && header.StartsWith(type.Signature);
}
