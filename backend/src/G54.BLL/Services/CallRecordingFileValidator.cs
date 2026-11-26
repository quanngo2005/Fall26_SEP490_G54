namespace G54.BLL.Services;

/// <summary>
/// Confirms that a call recording really is the audio type its extension claims, and maps the extension to the content
/// type that is stored and served. The client-supplied <c>Content-Type</c> is never trusted.
/// </summary>
public static class CallRecordingFileValidator
{
    private delegate bool SignatureCheck(ReadOnlySpan<byte> header);

    private static readonly Dictionary<string, (string ContentType, SignatureCheck Matches)> Types =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".mp3"] = ("audio/mpeg", IsMp3),
            [".m4a"] = ("audio/mp4", IsIsoMedia),
            [".mp4"] = ("video/mp4", IsIsoMedia),
            [".wav"] = ("audio/wav", IsWave),
        };

    /// <summary>Number of leading bytes <see cref="HasValidSignature"/> needs.</summary>
    public const int HeaderLength = 12;

    public static bool IsKnownExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) && Types.ContainsKey(extension);

    /// <summary>Content type for a known extension.</summary>
    public static string ContentTypeFor(string extension) => Types[extension].ContentType;

    /// <summary>True when <paramref name="header"/> starts with the signature expected for <paramref name="extension"/>.</summary>
    public static bool HasValidSignature(string? extension, ReadOnlySpan<byte> header) =>
        extension is not null && Types.TryGetValue(extension, out var type) && type.Matches(header);

    /// <summary>An ID3 tag, or an MPEG audio frame sync (11 set bits).</summary>
    private static bool IsMp3(ReadOnlySpan<byte> header) =>
        header.StartsWith("ID3"u8) || (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0);

    /// <summary>ISO base media (MP4 / M4A): a box size followed by <c>ftyp</c>.</summary>
    private static bool IsIsoMedia(ReadOnlySpan<byte> header) =>
        header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8);

    private static bool IsWave(ReadOnlySpan<byte> header) =>
        header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WAVE"u8);
}
