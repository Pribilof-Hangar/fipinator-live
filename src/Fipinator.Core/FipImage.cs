// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using SkiaSharp;

namespace Fipinator.Core;

/// <summary>
/// Converts rendered gauges into the pixel layout the FIP screen expects.
/// Per Logitech's FIP data sheet: 320×240, 24 bits per pixel, the same layout as the pixel
/// data of a 24-bit Windows bitmap (blue, green, red byte order; bottom row first).
/// </summary>
public static class FipImage
{
    public const int Width = 320;
    public const int Height = 240;
    public const int BufferSize = Width * Height * 3; // 230400 bytes

    public static byte[] ToBuffer(SKBitmap bitmap)
    {
        var buffer = new byte[BufferSize];
        ToBuffer(bitmap, buffer);
        return buffer;
    }

    /// <summary>Writes the bitmap into <paramref name="buffer"/>, which must be <see cref="BufferSize"/> bytes.</summary>
    public static void ToBuffer(SKBitmap bitmap, Span<byte> buffer)
    {
        if (bitmap.Width != Width || bitmap.Height != Height)
            throw new ArgumentException($"FIP images must be {Width}x{Height}; this one is {bitmap.Width}x{bitmap.Height}.", nameof(bitmap));
        if (buffer.Length != BufferSize)
            throw new ArgumentException($"FIP buffer must be {BufferSize} bytes.", nameof(buffer));

        // Premultiplied RGBA is the same as the image drawn over black, which is what we want
        // for any see-through pixels, since the FIP has no transparency.
        using var converted = bitmap.ColorType == SKColorType.Rgba8888 && bitmap.AlphaType == SKAlphaType.Premul
            ? null
            : OverBlack(bitmap);
        var source = converted ?? bitmap;

        var pixels = source.GetPixelSpan();
        var rowBytes = source.RowBytes;
        for (var y = 0; y < Height; y++)
        {
            var src = pixels.Slice(y * rowBytes, Width * 4);
            var dst = buffer.Slice((Height - 1 - y) * Width * 3, Width * 3);
            for (var x = 0; x < Width; x++)
            {
                dst[x * 3] = src[x * 4 + 2];     // blue
                dst[x * 3 + 1] = src[x * 4 + 1]; // green
                dst[x * 3 + 2] = src[x * 4];     // red
            }
        }
    }

    private static SKBitmap OverBlack(SKBitmap bitmap)
    {
        var result = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Black);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return result;
    }
}
