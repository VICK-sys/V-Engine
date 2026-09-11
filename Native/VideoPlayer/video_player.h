// video_player.h — Native FFmpeg video decoder for V-Engine
// Decodes video to RGBA frames, called via P/Invoke from C#.

#pragma once

#ifdef _WIN32
    #ifdef VIDEO_EXPORTS
        #define VIDEO_API __declspec(dllexport)
    #else
        #define VIDEO_API __declspec(dllimport)
    #endif
#else
    #define VIDEO_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct VideoPlayerHandle* VideoPlayer;

// ── Lifecycle ───────────────────────────────────────────

/// Open a video file. Returns NULL on failure.
VIDEO_API VideoPlayer video_open(const char* path);

/// Close and free all resources.
VIDEO_API void video_close(VideoPlayer vp);

// ── Info ────────────────────────────────────────────────

VIDEO_API int   video_width(VideoPlayer vp);
VIDEO_API int   video_height(VideoPlayer vp);
VIDEO_API double video_duration(VideoPlayer vp);   // seconds
VIDEO_API double video_fps(VideoPlayer vp);
VIDEO_API int   video_finished(VideoPlayer vp);

// ── Playback ────────────────────────────────────────────

/// Decode the next frame. Returns 1 if a frame is ready, 0 if EOF/error.
VIDEO_API int video_next_frame(VideoPlayer vp);

/// Get pointer to the current RGBA frame data (width * height * 4 bytes).
/// Valid until the next call to video_next_frame.
VIDEO_API const unsigned char* video_frame_data(VideoPlayer vp);

/// Seek to a position in seconds.
VIDEO_API void video_seek(VideoPlayer vp, double seconds);

/// Reset to beginning.
VIDEO_API void video_rewind(VideoPlayer vp);

// ── Audio ───────────────────────────────────────────────

/// Returns 1 if the video has an audio stream.
VIDEO_API int video_has_audio(VideoPlayer vp);

/// Audio sample rate (e.g., 44100).
VIDEO_API int video_audio_sample_rate(VideoPlayer vp);

/// Audio channels (1=mono, 2=stereo).
VIDEO_API int video_audio_channels(VideoPlayer vp);

/// Get decoded audio samples (signed 16-bit PCM, interleaved).
/// Call after video_next_frame(). Returns number of samples written.
/// Buffer must hold at least max_samples * channels * 2 bytes.
VIDEO_API int video_get_audio(VideoPlayer vp, short* buffer, int max_samples);

#ifdef __cplusplus
}
#endif
