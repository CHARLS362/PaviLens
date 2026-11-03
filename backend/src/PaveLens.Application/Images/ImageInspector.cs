using System.Buffers.Binary;

namespace PaveLens.Application.Images;

public sealed record ImageInfo(string ContentType, string Extension, int Width, int Height);

/// <summary>
/// Identifies JPEG, PNG and WebP files from their bytes (never from the file name or the
/// client-supplied content type) and reads their pixel dimensions from the headers.
/// </summary>
public static class ImageInspector
{
    public static ImageInfo? Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return InspectPng(data);
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return InspectJpeg(data);
        }

        if (data.Length >= 30 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return InspectWebp(data);
        }

        return null;
    }

    private static ImageInfo? InspectPng(ReadOnlySpan<byte> data)
    {
        if (!data[12..16].SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(data[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(data[20..24]);
        return Valid(width, height) ? new ImageInfo("image/png", ".png", width, height) : null;
    }

    private static ImageInfo? InspectJpeg(ReadOnlySpan<byte> data)
    {
        var offset = 2;
        while (offset + 9 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                return null;
            }

            var marker = data[offset + 1];
            if (marker == 0xFF)
            {
                offset++; // fill byte
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 2, 2));
            if (length < 2)
            {
                return null;
            }

            // SOF0..SOF15 except DHT (C4), JPG (C8) and DAC (CC) carry the frame size.
            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isStartOfFrame)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 7, 2));
                return Valid(width, height) ? new ImageInfo("image/jpeg", ".jpg", width, height) : null;
            }

            offset += 2 + length;
        }

        return null;
    }

    private static ImageInfo? InspectWebp(ReadOnlySpan<byte> data)
    {
        var chunk = data[12..16];
        int width, height;
        if (chunk.SequenceEqual("VP8X"u8))
        {
            width = 1 + (data[24] | data[25] << 8 | data[26] << 16);
            height = 1 + (data[27] | data[28] << 8 | data[29] << 16);
        }
        else if (chunk.SequenceEqual("VP8L"u8))
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..25]);
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
        }
        else if (chunk.SequenceEqual("VP8 "u8))
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(data[26..28]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(data[28..30]) & 0x3FFF;
        }
        else
        {
            return null;
        }

        return Valid(width, height) ? new ImageInfo("image/webp", ".webp", width, height) : null;
    }

    private static bool Valid(int width, int height) => width > 0 && height > 0;
}
