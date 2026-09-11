// image_process.cpp — Native RGBA image processing for V-Engine

#include "image_process.h"
#include <cstring>
#include <cmath>
#include <algorithm>
#include <vector>

// ── Box Blur (separable, two-pass) ──────────────────────────

IMGPROC_API void imgproc_blur(
    const unsigned char* input, unsigned char* output,
    int width, int height, int radius
) {
    if (radius <= 0) {
        if (input != output)
            memcpy(output, input, width * height * 4);
        return;
    }

    int n = width * height;
    std::vector<unsigned char> temp(n * 4);

    // Horizontal pass: input -> temp
    for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
            int sumR = 0, sumG = 0, sumB = 0, sumA = 0, count = 0;
            for (int kx = -radius; kx <= radius; kx++) {
                int sx = x + kx;
                if (sx < 0 || sx >= width) continue;
                int idx = (y * width + sx) * 4;
                sumR += input[idx];
                sumG += input[idx + 1];
                sumB += input[idx + 2];
                sumA += input[idx + 3];
                count++;
            }
            int oi = (y * width + x) * 4;
            temp[oi]     = (unsigned char)(sumR / count);
            temp[oi + 1] = (unsigned char)(sumG / count);
            temp[oi + 2] = (unsigned char)(sumB / count);
            temp[oi + 3] = (unsigned char)(sumA / count);
        }
    }

    // Vertical pass: temp -> output
    for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
            int sumR = 0, sumG = 0, sumB = 0, sumA = 0, count = 0;
            for (int ky = -radius; ky <= radius; ky++) {
                int sy = y + ky;
                if (sy < 0 || sy >= height) continue;
                int idx = (sy * width + x) * 4;
                sumR += temp[idx];
                sumG += temp[idx + 1];
                sumB += temp[idx + 2];
                sumA += temp[idx + 3];
                count++;
            }
            int oi = (y * width + x) * 4;
            output[oi]     = (unsigned char)(sumR / count);
            output[oi + 1] = (unsigned char)(sumG / count);
            output[oi + 2] = (unsigned char)(sumB / count);
            output[oi + 3] = (unsigned char)(sumA / count);
        }
    }
}

// ── Outline ─────────────────────────────────────────────────

IMGPROC_API void imgproc_outline(
    const unsigned char* input, unsigned char* output,
    int width, int height, int thickness,
    unsigned char r, unsigned char g, unsigned char b, unsigned char a
) {
    int n = width * height;
    // Clear output
    memset(output, 0, n * 4);

    // Pass 1: for each transparent pixel, check if any non-transparent pixel
    // exists within 'thickness' distance. If so, write outline color.
    int t2 = thickness * thickness;
    for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
            int idx = (y * width + x) * 4;
            // If pixel is non-transparent, skip (will be composited later)
            if (input[idx + 3] > 0) continue;

            // Check neighborhood for non-transparent pixels
            bool found = false;
            int minKY = std::max(0, y - thickness);
            int maxKY = std::min(height - 1, y + thickness);
            int minKX = std::max(0, x - thickness);
            int maxKX = std::min(width - 1, x + thickness);

            for (int ky = minKY; ky <= maxKY && !found; ky++) {
                for (int kx = minKX; kx <= maxKX && !found; kx++) {
                    int dx = kx - x, dy = ky - y;
                    if (dx * dx + dy * dy > t2) continue;
                    int ki = (ky * width + kx) * 4;
                    if (input[ki + 3] > 0) found = true;
                }
            }

            if (found) {
                output[idx] = r;
                output[idx + 1] = g;
                output[idx + 2] = b;
                output[idx + 3] = a;
            }
        }
    }

    // Pass 2: composite original on top
    for (int i = 0; i < n; i++) {
        int idx = i * 4;
        if (input[idx + 3] > 0) {
            output[idx]     = input[idx];
            output[idx + 1] = input[idx + 1];
            output[idx + 2] = input[idx + 2];
            output[idx + 3] = input[idx + 3];
        }
    }
}

// ── Tint ────────────────────────────────────────────────────

IMGPROC_API void imgproc_tint(
    unsigned char* pixels, int width, int height,
    unsigned char r, unsigned char g, unsigned char b, unsigned char a
) {
    int n = width * height;
    for (int i = 0; i < n; i++) {
        int idx = i * 4;
        pixels[idx]     = (unsigned char)((pixels[idx] * r) / 255);
        pixels[idx + 1] = (unsigned char)((pixels[idx + 1] * g) / 255);
        pixels[idx + 2] = (unsigned char)((pixels[idx + 2] * b) / 255);
        pixels[idx + 3] = (unsigned char)((pixels[idx + 3] * a) / 255);
    }
}

// ── Palette Swap ────────────────────────────────────────────

IMGPROC_API void imgproc_palette_swap(
    unsigned char* pixels, int width, int height,
    const unsigned char* from, const unsigned char* to,
    int count, int tolerance
) {
    int n = width * height;
    for (int i = 0; i < n; i++) {
        int idx = i * 4;
        for (int c = 0; c < count; c++) {
            int ci = c * 4;
            int dr = abs((int)pixels[idx] - (int)from[ci]);
            int dg = abs((int)pixels[idx+1] - (int)from[ci+1]);
            int db = abs((int)pixels[idx+2] - (int)from[ci+2]);
            if (dr <= tolerance && dg <= tolerance && db <= tolerance) {
                pixels[idx]     = to[ci];
                pixels[idx + 1] = to[ci + 1];
                pixels[idx + 2] = to[ci + 2];
                pixels[idx + 3] = to[ci + 3];
                break;
            }
        }
    }
}

// ── Grayscale ───────────────────────────────────────────────

IMGPROC_API void imgproc_grayscale(unsigned char* pixels, int width, int height) {
    int n = width * height;
    for (int i = 0; i < n; i++) {
        int idx = i * 4;
        // ITU-R BT.601 luminance
        unsigned char lum = (unsigned char)(
            pixels[idx] * 0.299f +
            pixels[idx + 1] * 0.587f +
            pixels[idx + 2] * 0.114f
        );
        pixels[idx] = pixels[idx + 1] = pixels[idx + 2] = lum;
    }
}

// ── Invert ──────────────────────────────────────────────────

IMGPROC_API void imgproc_invert(unsigned char* pixels, int width, int height) {
    int n = width * height;
    for (int i = 0; i < n; i++) {
        int idx = i * 4;
        pixels[idx]     = 255 - pixels[idx];
        pixels[idx + 1] = 255 - pixels[idx + 1];
        pixels[idx + 2] = 255 - pixels[idx + 2];
        // Alpha preserved
    }
}
