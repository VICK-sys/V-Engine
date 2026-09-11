// gif_decoder.cpp — Native GIF decoder using stb_image.h
// Decodes all frames upfront into a contiguous RGBA buffer.
// C# uploads one frame at a time to a single GPU texture (reused).

#define GIF_EXPORTS
#include "gif_decoder.h"

#define STB_IMAGE_IMPLEMENTATION
#define STBI_ONLY_GIF
#include "stb_image.h"

#include <cstdlib>
#include <cstring>
#include <vector>

struct GifHandle {
    unsigned char* pixels = nullptr;  // all frames contiguous: frame0 | frame1 | ...
    int width = 0;
    int height = 0;
    int frame_count = 0;
    int frame_size = 0;               // width * height * 4
    std::vector<int> delays;          // delay per frame in ms
};

GIF_API GifDecoder gif_open(const char* path) {
    FILE* f = fopen(path, "rb");
    if (!f) return nullptr;

    // Get file size
    fseek(f, 0, SEEK_END);
    long file_size = ftell(f);
    fseek(f, 0, SEEK_SET);

    // Read entire file into memory
    auto* file_data = (unsigned char*)malloc(file_size);
    if (!file_data) { fclose(f); return nullptr; }
    fread(file_data, 1, file_size, f);
    fclose(f);

    // Decode all GIF frames
    int* delays_raw = nullptr;
    int width, height, frames, comp;

    unsigned char* pixels = stbi_load_gif_from_memory(
        file_data, (int)file_size,
        &delays_raw, &width, &height, &frames, &comp, 4); // force RGBA

    free(file_data);

    if (!pixels || frames <= 0) {
        if (pixels) stbi_image_free(pixels);
        if (delays_raw) stbi_image_free(delays_raw);
        return nullptr;
    }

    auto* g = new GifHandle();
    g->width = width;
    g->height = height;
    g->frame_count = frames;
    g->frame_size = width * height * 4;
    g->pixels = pixels;

    g->delays.resize(frames);
    for (int i = 0; i < frames; i++)
        g->delays[i] = delays_raw ? delays_raw[i] : 100;

    if (delays_raw) stbi_image_free(delays_raw);

    return g;
}

GIF_API void gif_close(GifDecoder g) {
    if (!g) return;
    if (g->pixels) stbi_image_free(g->pixels);
    delete g;
}

GIF_API int gif_frame_count(GifDecoder g) { return g ? g->frame_count : 0; }
GIF_API int gif_width(GifDecoder g)       { return g ? g->width : 0; }
GIF_API int gif_height(GifDecoder g)      { return g ? g->height : 0; }

GIF_API int gif_frame_delay(GifDecoder g, int frame) {
    if (!g || frame < 0 || frame >= g->frame_count) return 100;
    return g->delays[frame];
}

GIF_API const unsigned char* gif_frame_data(GifDecoder g, int frame) {
    if (!g || frame < 0 || frame >= g->frame_count) return nullptr;
    return g->pixels + (size_t)frame * g->frame_size;
}
