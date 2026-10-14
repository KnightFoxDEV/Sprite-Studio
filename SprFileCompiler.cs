using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace SpriteStudio;

internal static class SprFileCompiler
{
    private const int SpriteSide = 32;
    private const int SpritePixelCount = SpriteSide * SpriteSide;

    public static void Compile(string sourcePath, IReadOnlyList<uint> sourceOffsets, IReadOnlyList<uint> sourceSpriteIds,
        int sourcePixelStride, uint signature, bool extended, bool transparency, string outputPath)
    {
        if (!extended && sourceSpriteIds.Count > ushort.MaxValue)
            throw new InvalidDataException("The output SPR exceeds 65,535 sprites without Extended.");

        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.RandomAccess);
        using var destination = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.RandomAccess);
        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);

        writer.Write(signature);
        if (extended) writer.Write((uint)sourceSpriteIds.Count);
        else writer.Write((ushort)sourceSpriteIds.Count);

        var tableStart = destination.Position;
        var tableLength = checked(sourceSpriteIds.Count * sizeof(uint));
        destination.Write(new byte[tableLength]);
        var outputOffsets = new uint[sourceSpriteIds.Count];
        var spriteHeader = new byte[5];

        for (var index = 0; index < sourceSpriteIds.Count; index++)
        {
            var sourceId = sourceSpriteIds[index];
            if (sourceId == 0 || sourceId > sourceOffsets.Count)
                throw new InvalidDataException($"Invalid source sprite ID {sourceId}.");
            var sourceOffset = sourceOffsets[checked((int)sourceId - 1)];
            if (sourceOffset == 0) continue;
            if (sourceOffset + 5L > source.Length)
                throw new InvalidDataException($"Sprite {sourceId} points outside the source SPR file.");

            source.Position = sourceOffset;
            source.ReadExactly(spriteHeader);
            var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(spriteHeader[3..]);
            if (compressedLength == 0) continue;
            if (source.Position + compressedLength > source.Length)
                throw new InvalidDataException($"Sprite {sourceId} has truncated pixel data.");

            var compressed = new byte[compressedLength];
            source.ReadExactly(compressed);
            var pixels = DecodePixels(compressed, sourcePixelStride);
            if (pixels is null) throw new InvalidDataException($"Sprite {sourceId} has invalid compressed pixels.");
            var encoded = EncodeSprite(pixels, transparency);
            if (encoded.Length == 0) continue;

            var outputOffset = destination.Position;
            if (outputOffset > uint.MaxValue)
                throw new InvalidDataException("The output SPR is too large for its sprite offset table.");
            outputOffsets[index] = (uint)outputOffset;
            destination.Write(encoded);
        }

        destination.Position = tableStart;
        foreach (var offset in outputOffsets) writer.Write(offset);
        writer.Flush();
        destination.Flush(flushToDisk: true);
    }

    private static byte[]? DecodePixels(ReadOnlySpan<byte> compressed, int stride)
    {
        if (stride is not (3 or 4)) throw new ArgumentOutOfRangeException(nameof(stride));
        var pixels = new byte[SpritePixelCount * 4];
        var position = 0;
        var pixel = 0;
        var valid = true;
        while (position + 4 <= compressed.Length && pixel < SpritePixelCount)
        {
            var transparent = BinaryPrimitives.ReadUInt16LittleEndian(compressed.Slice(position, 2));
            var colored = BinaryPrimitives.ReadUInt16LittleEndian(compressed.Slice(position + 2, 2));
            position += 4;
            if (pixel + transparent + colored > SpritePixelCount
                || position + (long)colored * stride > compressed.Length)
            {
                valid = false;
                break;
            }
            pixel += transparent;
            for (var index = 0; index < colored; index++)
            {
                var destination = pixel++ * 4;
                pixels[destination] = compressed[position + 2];
                pixels[destination + 1] = compressed[position + 1];
                pixels[destination + 2] = compressed[position];
                pixels[destination + 3] = stride == 4 ? compressed[position + 3] : (byte)255;
                position += stride;
            }
        }
        return valid && position == compressed.Length && pixel <= SpritePixelCount ? pixels : null;
    }

    private static byte[] EncodeSprite(byte[] pixels, bool transparency)
    {
        using var compressedStream = new MemoryStream();
        using (var writer = new BinaryWriter(compressedStream, Encoding.UTF8, leaveOpen: true))
        {
            var pixel = 0;
            while (pixel < SpritePixelCount)
            {
                var transparentCount = 0;
                while (pixel < SpritePixelCount && pixels[pixel * 4 + 3] == 0)
                {
                    transparentCount++;
                    pixel++;
                }
                if (pixel >= SpritePixelCount) break;

                var coloredStart = pixel;
                while (pixel < SpritePixelCount && pixels[pixel * 4 + 3] != 0) pixel++;
                var coloredCount = pixel - coloredStart;
                writer.Write((ushort)transparentCount);
                writer.Write((ushort)coloredCount);
                for (var index = coloredStart; index < pixel; index++)
                {
                    var source = index * 4;
                    writer.Write(pixels[source + 2]);
                    writer.Write(pixels[source + 1]);
                    writer.Write(pixels[source]);
                    if (transparency) writer.Write(pixels[source + 3]);
                }
            }
        }

        var compressed = compressedStream.ToArray();
        if (compressed.Length == 0) return [];
        if (compressed.Length > ushort.MaxValue)
            throw new InvalidDataException("A sprite is too large to store in the SPR format.");
        using var spriteStream = new MemoryStream(compressed.Length + 5);
        using (var writer = new BinaryWriter(spriteStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)0xFF);
            writer.Write((byte)0x00);
            writer.Write((byte)0xFF);
            writer.Write((ushort)compressed.Length);
            writer.Write(compressed);
        }
        return spriteStream.ToArray();
    }
}
