// image_process.h — Native RGBA image processing for V-Engine
// Box blur, outline generation, palette swap, tint. Operates on raw RGBA buffers.

#pragma once

#ifdef _WIN32
    #ifdef IMGPROC_EXPORTS
        #define IMGPROC_API __declspec(dllexport)
    #else
        #define IMGPROC_API __declspec(dllimport)
    #endif
#else
    #define IMGPROC_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// All functions operate on RGBA byte arrays (4 bytes per pixel, top-left origin).
// Input and output may be the same buffer (in-place) unless noted otherwise.

// Box blur with configurable radius. Writes to 'output' (may equal 'input' for in-place).
IMGPROC_API void imgproc_blur(
    const unsigned char* input, unsigned char* output,
    int width, int height, int radius
);

// Generate an outline: expand non-transparent pixels by 'thickness' pixels in the given color.
// Writes to 'output' which must be a separate buffer (size = width * height * 4).
// The original image is composited on top.
IMGPROC_API void imgproc_outline(
    const unsigned char* input, unsigned char* output,
    int width, int height, int thickness,
    unsigned char r, unsigned char g, unsigned char b, unsigned char a
);

// Tint: multiply each pixel's RGB by (r,g,b)/255, alpha by a/255.
// In-place operation.
IMGPROC_API void imgproc_tint(
    unsigned char* pixels, int width, int height,
    unsigned char r, unsigned char g, unsigned char b, unsigned char a
);

// Palette swap: replace pixels matching 'from' colors with 'to' colors.
// from/to are arrays of RGBA (4 bytes each), count is number of palette entries.
// Tolerance: max per-channel difference for matching (0 = exact match).
IMGPROC_API void imgproc_palette_swap(
    unsigned char* pixels, int width, int height,
    const unsigned char* from_colors, const unsigned char* to_colors,
    int count, int tolerance
);

// Grayscale: convert to luminance while preserving alpha.
IMGPROC_API void imgproc_grayscale(unsigned char* pixels, int width, int height);

// Invert: flip RGB channels (255 - value). Alpha preserved.
IMGPROC_API void imgproc_invert(unsigned char* pixels, int width, int height);

#ifdef __cplusplus
}
#endif
