namespace KPod.Core.Images;

/// <summary>Decodes uncompressed and RLE-compressed 24/32-bit true-colour TGA images.</summary>
public static class TgaImageDecoder
{
    private const int HeaderBytes = 18;

    public static DecodedImage Decode(byte[] data, string? name = null)
    {
        string displayName = string.IsNullOrEmpty(name) ? "image" : name!;
        if (data.Length < HeaderBytes)
        {
            throw Invalid(displayName, "header is truncated");
        }

        int idLength = data[0];
        int colorMapType = data[1];
        int imageType = data[2];
        int width = ReadUInt16(data, 12);
        int height = ReadUInt16(data, 14);
        int bitsPerPixel = data[16];
        int descriptor = data[17];

        if (colorMapType != 0 || (imageType != 2 && imageType != 10))
        {
            throw Invalid(displayName, "expected uncompressed or RLE true-colour data");
        }

        if (width == 0 || height == 0 || (bitsPerPixel != 24 && bitsPerPixel != 32))
        {
            throw Invalid(displayName, "expected non-zero dimensions and 24- or 32-bit pixels");
        }

        long pixelCount64 = (long)width * height;
        if (pixelCount64 > int.MaxValue)
        {
            throw Invalid(displayName, "dimensions are too large");
        }

        int pixelCount = (int)pixelCount64;
        int[] source = new int[pixelCount];
        int offset = HeaderBytes + idLength;
        if (offset > data.Length)
        {
            throw Invalid(displayName, "image ID is truncated");
        }

        int bytesPerPixel = bitsPerPixel / 8;
        int pixel = 0;
        if (imageType == 2)
        {
            while (pixel < pixelCount)
            {
                source[pixel++] = ReadPixel(data, ref offset, bytesPerPixel, displayName);
            }
        }
        else
        {
            while (pixel < pixelCount)
            {
                if (offset >= data.Length)
                {
                    throw Invalid(displayName, "RLE packet is truncated");
                }

                int packet = data[offset++];
                int count = (packet & 0x7F) + 1;
                if (count > pixelCount - pixel)
                {
                    throw Invalid(displayName, "RLE packet contains too many pixels");
                }

                if ((packet & 0x80) != 0)
                {
                    int argb = ReadPixel(data, ref offset, bytesPerPixel, displayName);
                    for (int i = 0; i < count; i++) source[pixel++] = argb;
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        source[pixel++] = ReadPixel(data, ref offset, bytesPerPixel, displayName);
                    }
                }
            }
        }

        bool topOrigin = (descriptor & 0x20) != 0;
        bool rightOrigin = (descriptor & 0x10) != 0;
        if (topOrigin && !rightOrigin)
        {
            return new DecodedImage(width, height, source);
        }

        int[] oriented = new int[pixelCount];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int targetX = rightOrigin ? width - 1 - x : x;
                int targetY = topOrigin ? y : height - 1 - y;
                oriented[(targetY * width) + targetX] = source[(y * width) + x];
            }
        }

        return new DecodedImage(width, height, oriented);
    }

    private static int ReadPixel(byte[] data, ref int offset, int bytesPerPixel, string name)
    {
        if (offset > data.Length - bytesPerPixel)
        {
            throw Invalid(name, "pixel data is truncated");
        }

        int b = data[offset++];
        int g = data[offset++];
        int r = data[offset++];
        int a = bytesPerPixel == 4 ? data[offset++] : 255;
        return unchecked((int)0xFF000000) & (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static int ReadUInt16(byte[] data, int offset) => data[offset] | (data[offset + 1] << 8);

    private static ArgumentException Invalid(string name, string reason) =>
        new("Invalid or unsupported TGA " + name + ": " + reason);
}
