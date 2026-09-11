using VEngine.Engine.Core;

namespace VEngine.Engine.Graphics;

public static class ImageProcessing
{
    private static void Validate(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Pixel buffer must contain four bytes per pixel.", nameof(pixels));
        NativeRuntime.Require("image_process");
    }

    public static byte[] Blur(byte[] pixels, int width, int height, int radius)
    {
        Validate(pixels, width, height);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radius, 1024);
        var result = new byte[pixels.Length];
        NativeImageProcess.imgproc_blur(pixels, result, width, height, radius);
        return result;
    }

    public static byte[] Outline(byte[] pixels, int width, int height, int thickness, byte r, byte g, byte b, byte a = 255)
    {
        Validate(pixels, width, height);
        ArgumentOutOfRangeException.ThrowIfNegative(thickness);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(thickness, 1024);
        var result = new byte[pixels.Length];
        NativeImageProcess.imgproc_outline(pixels, result, width, height, thickness, r, g, b, a);
        return result;
    }

    public static void Tint(byte[] pixels, int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        Validate(pixels, width, height);
        NativeImageProcess.imgproc_tint(pixels, width, height, r, g, b, a);
    }

    public static void PaletteSwap(byte[] pixels, int width, int height, byte[] from, byte[] to, int tolerance = 0)
    {
        Validate(pixels, width, height);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from.Length != to.Length || from.Length % 4 != 0)
            throw new ArgumentException("Palettes must contain matching RGBA entries.");
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tolerance, 255);
        NativeImageProcess.imgproc_palette_swap(pixels, width, height, from, to, from.Length / 4, tolerance);
    }

    public static void Grayscale(byte[] pixels, int width, int height)
    {
        Validate(pixels, width, height);
        NativeImageProcess.imgproc_grayscale(pixels, width, height);
    }

    public static void Invert(byte[] pixels, int width, int height)
    {
        Validate(pixels, width, height);
        NativeImageProcess.imgproc_invert(pixels, width, height);
    }
}
