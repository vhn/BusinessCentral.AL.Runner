// MediaPatches — the portable part of BC's Media/MediaSet image-import contract.
//
// WHY
//   BC decides what a Media field holds by trying to decode it as an image:
//       NavMediaFactory.ProcessMediaObject(stream, saveStream, mimeType)
//         try  { NavMediaImage.GetImageWithContentHeaderValidation(stream) }
//         catch (NavImageLoadErrorException ex) when (ex.InnerException is ArgumentException)
//              { mimeType = "application/octet-stream"; }
//   That catch is how NON-image media (a report layout template, a PDF, any blob) gets
//   stored at all. On Windows it fires because System.Drawing's Image.FromStream throws
//   ArgumentException for content it does not recognise.
//
//   On Linux there is no libgdiplus and System.Drawing.Common is unsupported, so
//   Image.FromStream throws PlatformNotSupportedException instead. BC's exception mapper
//   turns that into a NavImageLoadErrorException whose InnerException is NOT an
//   ArgumentException — so the `when` filter does not match, the fallback never runs, and
//   EVERY media write failed with "The media object could not be loaded because it is not a
//   valid image type, such as JPEG, GIF, or PNG", image or not. Publishing a report layout
//   (bytes that were never meant to be an image) hit exactly that.
//
// WHAT THIS DOES
//   Replaces the unavailable decoder with a bounded PNG path, and answers in the shape BC's
//   own control flow expects:
//     * content that is NOT a recognised image  → NavImageLoadErrorException wrapping an
//       ArgumentException, i.e. precisely what Windows produces, so BC's own
//       octet-stream fallback runs and the media stores;
//     * a structurally valid PNG                  → the same fallback, with a Cecil rewrite
//       preserving the validated source stream and image/png. The NST also preserves the
//       source stream when ProcessMediaObject's saveStream argument is true;
//     * any other image-looking payload          → a named refusal, because this platform
//       cannot decode it and answering with a fake would let callers assert against image
//       dimensions or thumbnails that were never read.
using System.Buffers.Binary;
using System.Reflection;

namespace AlRunner.Patches;

public static class MediaPatches
{
    private const string BinaryMimeType = "application/octet-stream";
    private const string PngMimeType = "image/png";
    private const long MaximumPngBytes = 64L * 1024 * 1024;
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly uint[] PngCrcTable = BuildPngCrcTable();

    /// <summary>
    /// Replacement for <c>NavMediaImage.GetImageWithContentHeaderValidation(Stream)</c>.
    /// Never returns: either it routes content through BC's binary-media fallback, or it
    /// refuses by name because image semantics cannot be established here.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static object? NavMediaImage_GetImageWithContentHeaderValidation(object? contentStream)
    {
        if (contentStream is not Stream stream)
            throw NotAnImage("the media content is not a readable stream");

        if (!stream.CanSeek)
            throw new AlRunner.Infrastructure.RunnerOutOfScopeException(
                "NavMediaImage.GetImageWithContentHeaderValidation",
                "media-content-sniff — the media content stream is not seekable, so its header "
                + "cannot be inspected without consuming the content BC is about to store. "
                + "See docs/scope.md");

        if (TryReadValidatedPng(stream))
            throw NotAnImage("image/png content will be stored from its validated source stream");

        var header = ReadHeader(stream);

        if (!LooksLikeImage(header))
            throw NotAnImage("the media content header matches no known image signature");

        throw new AlRunner.Infrastructure.RunnerOutOfScopeException(
            "NavMediaImage.GetImageWithContentHeaderValidation",
            "media-image-decode — the content IS an image, but decoding one needs "
            + "System.Drawing, which has no support on this platform (no libgdiplus). Non-image "
            + "media stores normally. See docs/scope.md");
    }

    /// <summary>
    /// Used by the Cecil-owned catch path in <c>NavMediaFactory.ProcessMediaObject</c>.
    /// A supported PNG keeps its content type while all ordinary non-image media retains
    /// BC's <c>application/octet-stream</c> fallback.
    /// </summary>
    public static string NavMedia_GetFallbackMimeType(object? contentStream)
        => contentStream is Stream stream && stream.CanSeek
            ? (TryReadValidatedPng(stream) ? PngMimeType : BinaryMimeType)
            : BinaryMimeType;

    /// <summary>Image signatures BC's own supported set covers (JPEG, PNG, GIF, BMP, TIFF, ICO).</summary>
    private static bool LooksLikeImage(ReadOnlySpan<byte> h)
    {
        static bool Starts(ReadOnlySpan<byte> h, params byte[] sig)
            => h.Length >= sig.Length && h[..sig.Length].SequenceEqual(sig);

        return Starts(h, 0xFF, 0xD8, 0xFF)                                     // JPEG
            || Starts(h, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)       // PNG
            || Starts(h, (byte)'G', (byte)'I', (byte)'F', (byte)'8')           // GIF87a / GIF89a
            || Starts(h, (byte)'B', (byte)'M')                                 // BMP
            || Starts(h, 0x49, 0x49, 0x2A, 0x00)                               // TIFF little-endian
            || Starts(h, 0x4D, 0x4D, 0x00, 0x2A)                               // TIFF big-endian
            || Starts(h, 0x00, 0x00, 0x01, 0x00);                              // ICO
    }

    private static byte[] ReadHeader(Stream stream)
    {
        var origin = stream.Position;
        var header = new byte[12];
        var read = 0;
        try
        {
            int n;
            while (read < header.Length && (n = stream.Read(header, read, header.Length - read)) > 0)
                read += n;
        }
        finally
        {
            stream.Position = origin;
        }

        return read == header.Length ? header : header[..read];
    }

    private static bool TryReadValidatedPng(Stream stream)
    {
        var origin = stream.Position;
        try
        {
            if (stream.Length - origin > MaximumPngBytes)
                return false;

            Span<byte> signature = stackalloc byte[8];
            if (!TryReadExactly(stream, signature)
                || !signature.SequenceEqual(PngSignature))
                return false;

            byte[]? header = null;
            byte bitDepth = 0;
            byte colorType = 0;
            var sawData = false;
            var sawPalette = false;
            var dataClosed = false;
            Span<byte> chunkHeader = stackalloc byte[8];
            Span<byte> chunkBuffer = stackalloc byte[4096];
            Span<byte> storedCrcBytes = stackalloc byte[4];
            while (TryReadExactly(stream, chunkHeader))
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[..4]);
                var type = chunkHeader[4..];
                if (!IsPngChunkType(type)) return false;
                var typeCode = BinaryPrimitives.ReadUInt32BigEndian(type);
                var isHeader = typeCode == 0x49484452; // IHDR
                var isPalette = typeCode == 0x504C5445; // PLTE
                var isData = typeCode == 0x49444154;   // IDAT
                var isEnd = typeCode == 0x49454E44;    // IEND

                if (header == null && (!isHeader || length != 13)) return false;
                if (isHeader && header != null) return false;
                if (isPalette && (sawPalette || sawData || length is 0 or > 768 || length % 3 != 0))
                    return false;
                if (!isHeader && !isPalette && !isData && !isEnd && IsCriticalPngChunk(type))
                    return false;
                if (isEnd && length != 0) return false;
                if (isData && dataClosed) return false;
                if (sawData && !isData) dataClosed = true;

                var crc = UpdatePngCrc(uint.MaxValue, type);
                var remaining = length;
                var headerOffset = 0;
                if (isHeader) header = new byte[13];
                while (remaining > 0)
                {
                    var count = (int)Math.Min((uint)chunkBuffer.Length, remaining);
                    var slice = chunkBuffer[..count];
                    if (!TryReadExactly(stream, slice)) return false;
                    crc = UpdatePngCrc(crc, slice);
                    if (isHeader)
                    {
                        slice.CopyTo(header!.AsSpan(headerOffset));
                        headerOffset += count;
                    }
                    remaining -= (uint)count;
                }

                if (!TryReadExactly(stream, storedCrcBytes)
                    || ~crc != BinaryPrimitives.ReadUInt32BigEndian(storedCrcBytes))
                    return false;

                if (isHeader)
                {
                    if (!TryReadPngHeader(header!, out bitDepth, out colorType)) return false;
                }
                else if (isPalette)
                {
                    if (colorType is 0 or 4) return false;
                    if (colorType == 3 && length / 3 > 1u << bitDepth) return false;
                    sawPalette = true;
                }
                else if (isData)
                {
                    if (header == null) return false;
                    if (colorType == 3 && !sawPalette) return false;
                    sawData = true;
                }
                else if (isEnd)
                {
                    if (header == null || !sawData || stream.Position != stream.Length)
                        return false;
                    return true;
                }
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            stream.Position = origin;
        }
    }

    private static bool IsPngChunkType(ReadOnlySpan<byte> type)
    {
        if (type.Length != 4) return false;
        foreach (var value in type)
            if (!((value >= (byte)'A' && value <= (byte)'Z')
                || (value >= (byte)'a' && value <= (byte)'z')))
                return false;
        return true;
    }

    private static bool IsCriticalPngChunk(ReadOnlySpan<byte> type)
        => (type[0] & 0x20) == 0;

    private static bool TryReadPngHeader(
        ReadOnlySpan<byte> header,
        out byte bitDepth,
        out byte colorType)
    {
        bitDepth = 0;
        colorType = 0;
        if (header.Length != 13 || header[10] != 0 || header[11] != 0 || header[12] > 1)
            return false;

        var pngWidth = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
        var pngHeight = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
        if (pngWidth == 0 || pngHeight == 0
            || pngWidth > int.MaxValue || pngHeight > int.MaxValue)
            return false;

        bitDepth = header[8];
        colorType = header[9];
        return colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 => bitDepth is 8 or 16,
            6 => bitDepth is 8 or 16,
            _ => false
        };
    }

    private static bool TryReadExactly(Stream stream, Span<byte> destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var count = stream.Read(destination[read..]);
            if (count == 0) return false;
            read += count;
        }
        return true;
    }

    private static uint UpdatePngCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
            crc = PngCrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildPngCrcTable()
    {
        var table = new uint[256];
        for (uint value = 0; value < table.Length; value++)
        {
            var crc = value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
            table[value] = crc;
        }
        return table;
    }

    private static ConstructorInfo? _navImageLoadErrorCtor;

    /// <summary>
    /// BC's own <c>NavImageLoadErrorException</c> wrapping an <c>ArgumentException</c> — the
    /// exact shape NavMediaFactory.ProcessMediaObject's `when (ex.InnerException is
    /// ArgumentException)` filter looks for. Any other type or inner type means the media
    /// write fails instead of falling back to application/octet-stream.
    /// </summary>
    private static Exception NotAnImage(string because)
    {
        var inner = new ArgumentException(because);
        try
        {
            if (_navImageLoadErrorCtor == null)
            {
                var navImageLoadErrorType = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Types")?
                    .GetType("Microsoft.Dynamics.Nav.Types.Exceptions.NavImageLoadErrorException");
                _navImageLoadErrorCtor = navImageLoadErrorType?.GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null, types: new[] { typeof(string), typeof(Exception) }, modifiers: null);
            }
            if (_navImageLoadErrorCtor != null)
                return (Exception)_navImageLoadErrorCtor.Invoke(new object[] { because, inner });

            return new InvalidOperationException(
                "NavImageLoadErrorException(String, Exception) not found — "
                + "Ncl shape changed; do not commit");
        }
        catch (Exception ex)
        {
            return new InvalidOperationException(
                "Could not construct NavImageLoadErrorException — Ncl shape changed; do not commit",
                ex);
        }
    }
}
