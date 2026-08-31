using System.Buffers.Binary;
using AlRunner.Infrastructure;
using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

public sealed class MediaPatchesTests
{
    private const string NpCoreMemberImagePng =
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAABmJLR0QA/wD/AP+gvaeTAAAACXBIWXMAAAsTAAALEwEAmpwYAAAA" +
        "B3RJTUUH3wwcCikwn4sr7QAAA85JREFUWMO9lk1sG0UUx/9vZndtr6lDlZJGpAGaIj5ECSAOoYIWu1kBrdQLObXlABIFCQhCHECUjwup" +
        "UKHQCxJSEJEQBypEOaA2UlDi1EFVURTTQyNy4EDEAQniJnHJhze7O49Dd82mdbJJHHska7x/j+f39uk/7w1ZVlri+mB/FqHnZZrneTw8/LNn" +
        "xoztruN2AbibmQHgd8Mwfpi37b/379+nCSF4LfsB8CgUAPmf8MKyZts2JsYnEsXZ4ndE9AwA+HAQEZgZRNSfTKUO7959/0IsHsNq+wWa8L9E" +
        "wvOj+R3F2eLVSvBQEAfnisXC5bHLOxzHiYQDIBH6ccXFjY2N7Cw540SkV4LfoOm2bY8buhEJDzIgVoP3ft2r+n/sP01EyTXAgzmZy+ZO9Xza" +
        "o6KyK24w4E2L21ruYQDda4UHGjO//thDT3BUdkVUmkzDaF8v3NcoGYs9GOEDijShp1TLBuAAANd1W6s3IfPCRuD+w1zVJtzSkMojNNaTjYat" +
        "W/NVm7AwPTNHRL9uwAf5f6am5qs2YSazV5dSvrZeH2ia1p3J7NWrNqGUkhcd5xIIH6/DBx8tOs4lKeXmVELLSktH8dsA3ouCCyHeWVLquGWl" +
        "tU2phEdfOKrGRvO3GkKcbtvVdkrTtTsA9BHRtF9wwMwzRPRVPB5vbWpu+twQ4rOx0bEt6c50ZJ+hlSJVSiGbHXENId4F0OO/pUtE7ydvSX45" +
        "c+3fq+HTkTLN20ul0ssAPihnSNBbS576pLPzSY2IKr2gqtiOQ/BhAOkV0r7AzH/6/9lJREaldcz8k8P8tH9PQGQ7ZmbOZkdcXYhfVoGDmU0A" +
        "9xHRvSvB/fGUTpTLZkfcNZlwaCjnGUJ8Q0DHBntAJW2fIUTv4OAFZ1UTSikRk/JZAM9tIjyQjhlSHPD1ypVQKUXMfKYG8OunRfHZwcELXsVK" +
        "eKjrEOeGcicB6LWA+1oirmkn2h9p926qhH1f9OkA3qwhHH6Wjx8+0rXchLF4DBPjE921hgfj8Uczr0gp/zfh+XMDLjO/Wmt4oAF4aWBgyC2b" +
        "MGWaTQDuqgfcv763tzRvT5ZNWCqVDtQLHsyFqYIVNuGd9YIHmmL1QNmEesw4W084AGhSu1g24dzC4m8AjtQLToLeWHSc3LJ2vO22bTjz7fde" +
        "XNcs5alOZt4D4GEiaqgWzsxXiOgiEZ2zPe/8i8eel5N/TFLQjsnPRLADd+zpUCc+PFk+Mwld3+l5XiuAZiJqZOYGBifA0IiIiUgxs83M8wBm" +
        "SVBBCPFXwjQnZ4rXpv0gyLLS0g+qfCf8D8L6EhAUv5Y3AAAAAElFTkSuQmCC";

    [Fact]
    public void SupportedRgbaPng_IsRoutedThroughBcMediaFallback()
    {
        _ = typeof(Microsoft.Dynamics.Nav.Types.Exceptions.NavImageLoadErrorException);
        using var stream = new MemoryStream(Convert.FromBase64String(NpCoreMemberImagePng));

        var exception = Record.Exception(
            () => MediaPatches.NavMediaImage_GetImageWithContentHeaderValidation(stream));

        Assert.NotNull(exception);
        Assert.Equal("NavImageLoadErrorException", exception.GetType().Name);
        Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Equal("image/png", MediaPatches.NavMedia_GetFallbackMimeType(stream));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void SignatureOnlyPng_RemainsANamedDecodeRefusal()
    {
        using var stream = new MemoryStream(Convert.FromBase64String("iVBORw0KGgo="));

        var exception = Assert.Throws<RunnerOutOfScopeException>(
            () => MediaPatches.NavMediaImage_GetImageWithContentHeaderValidation(stream));

        Assert.Contains("media-image-decode", exception.Message, StringComparison.Ordinal);
        Assert.Equal("application/octet-stream", MediaPatches.NavMedia_GetFallbackMimeType(stream));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void NonImage_RetainsMicrosoftBinaryFallbackAndStreamPosition()
    {
        _ = typeof(Microsoft.Dynamics.Nav.Types.Exceptions.NavImageLoadErrorException);
        using var stream = new MemoryStream(new byte[] { 0xAA, 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 });
        stream.Position = 1;

        var exception = Record.Exception(
            () => MediaPatches.NavMediaImage_GetImageWithContentHeaderValidation(stream));

        Assert.NotNull(exception);
        Assert.Equal("NavImageLoadErrorException", exception.GetType().Name);
        Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Equal("application/octet-stream", MediaPatches.NavMedia_GetFallbackMimeType(stream));
        Assert.Equal(1, stream.Position);
    }

    [Fact]
    public void RecognizedUnsupportedImage_RefusesByNameAndRestoresStreamPosition()
    {
        using var stream = new MemoryStream(new byte[] { 0x00, 0xFF, 0xD8, 0xFF, 0xE0, 0x00 });
        stream.Position = 1;

        var exception = Assert.Throws<RunnerOutOfScopeException>(
            () => MediaPatches.NavMediaImage_GetImageWithContentHeaderValidation(stream));

        Assert.Contains("media-image-decode", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, stream.Position);
    }

    [Fact]
    public void UnknownCriticalPngChunk_IsNotSilentlyStripped()
    {
        var png = InsertChunkBefore(
            Convert.FromBase64String(NpCoreMemberImagePng),
            "IDAT"u8,
            "ABCD"u8,
            ReadOnlySpan<byte>.Empty);

        AssertNamedImageRefusal(png);
    }

    [Fact]
    public void PaletteAfterImageData_IsNotSilentlyReordered()
    {
        var png = InsertChunkBefore(
            Convert.FromBase64String(NpCoreMemberImagePng),
            "IEND"u8,
            "PLTE"u8,
            new byte[] { 0x00, 0x00, 0x00 });

        AssertNamedImageRefusal(png);
    }

    private static void AssertNamedImageRefusal(byte[] content)
    {
        using var stream = new MemoryStream(content);

        var exception = Assert.Throws<RunnerOutOfScopeException>(
            () => MediaPatches.NavMediaImage_GetImageWithContentHeaderValidation(stream));

        Assert.Contains("media-image-decode", exception.Message, StringComparison.Ordinal);
        Assert.Equal("application/octet-stream", MediaPatches.NavMedia_GetFallbackMimeType(stream));
        Assert.Equal(0, stream.Position);
    }

    private static byte[] InsertChunkBefore(
        byte[] png,
        ReadOnlySpan<byte> beforeType,
        ReadOnlySpan<byte> newType,
        ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        output.Write(png.AsSpan(0, 8));
        var inserted = false;
        for (var offset = 8; offset < png.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            if (!inserted && png.AsSpan(offset + 4, 4).SequenceEqual(beforeType))
            {
                WriteChunk(output, newType, data);
                inserted = true;
            }
            output.Write(png.AsSpan(offset, length + 12));
            offset += length + 12;
        }

        Assert.True(inserted, $"PNG had no {System.Text.Encoding.ASCII.GetString(beforeType)} chunk");
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)data.Length));
        output.Write(number);
        output.Write(type);
        output.Write(data);

        var crc = UpdateCrc(uint.MaxValue, type);
        crc = UpdateCrc(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
        }
        return crc;
    }

}
