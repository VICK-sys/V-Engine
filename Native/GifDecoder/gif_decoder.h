// gif_decoder.h — Native GIF decoder for V-Engine
// Decodes animated GIF to flat RGBA buffer. Streams frames on demand.
// Uses stb_image.h (public domain, bundled).

#pragma once

#ifdef _WIN32
    #ifdef GIF_EXPORTS
        #define GIF_API __declspec(dllexport)
    #else
        #define GIF_API __declspec(dllimport)
    #endif
#else
    #define GIF_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct GifHandle* GifDecoder;

/// Open and decode a GIF file. All frames decoded upfront into a single buffer.
GIF_API GifDecoder gif_open(const char* path);

/// Free all resources.
GIF_API void gif_close(GifDecoder g);

/// Number of frames.
GIF_API int gif_frame_count(GifDecoder g);

/// Width and height (all frames same size).
GIF_API int gif_width(GifDecoder g);
GIF_API int gif_height(GifDecoder g);

/// Delay for a specific frame in milliseconds.
GIF_API int gif_frame_delay(GifDecoder g, int frame);

/// Get pointer to RGBA data for a specific frame.
/// Returns width * height * 4 bytes. Valid until gif_close.
GIF_API const unsigned char* gif_frame_data(GifDecoder g, int frame);

#ifdef __cplusplus
}
#endif
