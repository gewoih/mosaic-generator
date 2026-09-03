using System.Buffers.Binary;

namespace MosaicGenerator.Core.Rendering;

/// <summary>
/// Stamps the physical scale into an encoded PNG. Skia does not emit a pHYs chunk, and without it
/// printing the scheme at 100% gives an arbitrary size instead of the panel's real dimensions.
/// </summary>
public static class PngMetadata
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const int IhdrChunkLength = 25; // length + type + 13 data bytes + CRC
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] WithPhysicalScale(byte[] png, double pixelsPerMm)
    {
        ArgumentNullException.ThrowIfNull(png);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelsPerMm);

        if (png.Length < Signature.Length + IhdrChunkLength ||
            !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new ArgumentException("Not a PNG.", nameof(png));
        }

        uint pixelsPerMetre = (uint)Math.Round(pixelsPerMm * 1000.0);
        byte[] chunk = BuildPhysChunk(pixelsPerMetre);

        int insertAt = Signature.Length + IhdrChunkLength;

        var result = new byte[png.Length + chunk.Length];
        png.AsSpan(0, insertAt).CopyTo(result);
        chunk.CopyTo(result.AsSpan(insertAt));
        png.AsSpan(insertAt).CopyTo(result.AsSpan(insertAt + chunk.Length));

        return result;
    }

    /// <summary>Reads back the pHYs scale, in pixels per millimetre. Returns null when absent.</summary>
    public static double? ReadPhysicalScale(ReadOnlySpan<byte> png)
    {
        int offset = Signature.Length;

        while (offset + 8 <= png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            ReadOnlySpan<byte> type = png.Slice(offset + 4, 4);

            if (type.SequenceEqual("pHYs"u8))
            {
                uint perMetre = BinaryPrimitives.ReadUInt32BigEndian(png[(offset + 8)..]);
                byte unit = png[offset + 16];
                return unit == 1 ? perMetre / 1000.0 : null;
            }

            offset += 12 + length;
        }

        return null;
    }

    private static byte[] BuildPhysChunk(uint pixelsPerMetre)
    {
        var chunk = new byte[21]; // 4 length + 4 type + 9 data + 4 CRC

        BinaryPrimitives.WriteUInt32BigEndian(chunk, 9);
        "pHYs"u8.CopyTo(chunk.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8), pixelsPerMetre);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(12), pixelsPerMetre);
        chunk[16] = 1; // unit: metre

        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(17), Crc32(chunk.AsSpan(4, 13)));

        return chunk;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
