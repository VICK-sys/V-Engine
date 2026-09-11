using System;
using System.IO;
using System.IO.Compression;
using Silk.NET.OpenGL;

namespace VEngine.Engine.Core;

/// <summary>
/// Captures the screen to a PNG file. Use Eng.CaptureScreen() for the simplest API,
/// or press F12 (bound to "engine:screenshot" action).
/// </summary>
public static class ScreenCapture
{
    /// <summary>Screenshots folder. Default: "Screenshots" next to the executable.</summary>
    public static string ScreenshotsPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "Screenshots");

    /// <summary>
    /// Request a screenshot. Captured at the end of the current frame
    /// (after post-processing, before buffer swap). Returns the file path.
    /// </summary>
    public static string Capture(string? path = null)
    {
        path ??= GeneratePath();
        Eng.Game.RequestCapture(path);
        return path;
    }

    /// <summary>
    /// Called by Game.Render after post-processing to perform the actual capture.
    /// Reads the backbuffer at viewport resolution, flips rows, writes PNG.
    /// </summary>
    internal static void PerformCapture(string path)
    {
        var gl = Eng.GL;
        int x = gl.ViewportX;
        int y = gl.ViewportY;
        int w = gl.ViewportW;
        int h = gl.ViewportH;

        if (w <= 0 || h <= 0) return;

        gl.FlushAll();

        byte[] pixels = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* ptr = pixels)
            {
                gl.Api.ReadPixels(x, y, (uint)w, (uint)h,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        // GL is bottom-up, PNG is top-down — flip rows
        int stride = w * 4;
        byte[] tempRow = new byte[stride];
        for (int top = 0, bot = h - 1; top < bot; top++, bot--)
        {
            System.Buffer.BlockCopy(pixels, top * stride, tempRow, 0, stride);
            System.Buffer.BlockCopy(pixels, bot * stride, pixels, top * stride, stride);
            System.Buffer.BlockCopy(tempRow, 0, pixels, bot * stride, stride);
        }

        WritePng(path, w, h, pixels);
        Console.WriteLine($"[Screenshot] Saved: {path} ({w}x{h})");
    }

    private static string GeneratePath()
    {
        Directory.CreateDirectory(ScreenshotsPath);
        return Path.Combine(ScreenshotsPath,
            $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
    }

    // ── Minimal PNG encoder (no extra dependencies) ────────────

    private static readonly byte[] PngSignature =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly byte[] TypeIHDR = { (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
    private static readonly byte[] TypeIDAT = { (byte)'I', (byte)'D', (byte)'A', (byte)'T' };
    private static readonly byte[] TypeIEND = { (byte)'I', (byte)'E', (byte)'N', (byte)'D' };

    /// <summary>Write RGBA pixel data as a PNG file.</summary>
    public static void WritePng(string path, int width, int height, byte[] rgba)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        fs.Write(PngSignature);

        // IHDR chunk
        var ihdr = new byte[13];
        WriteBE32(ihdr, 0, width);
        WriteBE32(ihdr, 4, height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: RGBA
        WriteChunk(fs, TypeIHDR, ihdr);

        // IDAT chunk: zlib-compressed filtered scanlines
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var zlib = new ZLibStream(ms, CompressionLevel.Fastest))
            {
                int stride = width * 4;
                for (int y = 0; y < height; y++)
                {
                    zlib.WriteByte(0); // filter: None
                    zlib.Write(rgba, y * stride, stride);
                }
            }
            compressed = ms.ToArray();
        }
        WriteChunk(fs, TypeIDAT, compressed);

        // IEND chunk
        WriteChunk(fs, TypeIEND, Array.Empty<byte>());
    }

    private static void WriteChunk(Stream s, byte[] type, byte[] data)
    {
        Span<byte> buf = stackalloc byte[4];

        WriteBE32(buf, 0, data.Length);
        s.Write(buf);
        s.Write(type);
        s.Write(data);

        uint crc = Crc32(type, data);
        WriteBE32(buf, 0, (int)crc);
        s.Write(buf);
    }

    private static void WriteBE32(Span<byte> buf, int offset, int value)
    {
        buf[offset]     = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    // ── CRC32 (PNG standard: ISO 3309) ─────────────────────────

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type) crc = CrcTable[(byte)(crc ^ b)] ^ (crc >> 8);
        foreach (byte b in data) crc = CrcTable[(byte)(crc ^ b)] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
