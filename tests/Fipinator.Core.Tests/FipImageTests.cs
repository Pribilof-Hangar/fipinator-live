// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using Fipinator.Core;
using SkiaSharp;
using Xunit;

namespace Fipinator.Core.Tests;

public class FipImageTests
{
    [Fact]
    public void Buffer_is_bgr_with_the_bottom_row_first()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(320, 240, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Black);
        bitmap.SetPixel(0, 0, new SKColor(10, 20, 30));     // top-left
        bitmap.SetPixel(319, 239, new SKColor(40, 50, 60)); // bottom-right

        var buffer = FipImage.ToBuffer(bitmap);

        Assert.Equal(230400, buffer.Length);
        var topLeft = (239 * 320) * 3; // top row is stored last
        Assert.Equal([30, 20, 10], buffer[topLeft..(topLeft + 3)]);
        var bottomRight = 319 * 3;     // bottom row is stored first
        Assert.Equal([60, 50, 40], buffer[bottomRight..(bottomRight + 3)]);
    }

    [Fact]
    public void See_through_pixels_show_as_if_over_black()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(320, 240, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(new SKColor(200, 100, 0, 0)); // fully transparent
        Assert.All(FipImage.ToBuffer(bitmap), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Wrong_size_is_rejected_with_a_clear_message()
    {
        using var bitmap = new SKBitmap(100, 100);
        var ex = Assert.Throws<ArgumentException>(() => FipImage.ToBuffer(bitmap));
        Assert.Contains("320x240", ex.Message);
    }
}
