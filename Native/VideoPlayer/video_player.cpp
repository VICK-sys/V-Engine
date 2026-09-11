// video_player.cpp — FFmpeg video decoder for V-Engine
// Decodes video frames to RGBA pixel data for GPU texture upload.

#include "video_player.h"

extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libswscale/swscale.h>
#include <libswresample/swresample.h>
#include <libavutil/imgutils.h>
#include <libavutil/opt.h>
}

#include <cstring>
#include <cmath>

struct VideoPlayerHandle {
    AVFormatContext*  fmtCtx = nullptr;

    // Video
    AVCodecContext*   codecCtx = nullptr;
    SwsContext*       swsCtx = nullptr;
    AVFrame*         frame = nullptr;
    AVFrame*         rgbaFrame = nullptr;
    AVPacket*        packet = nullptr;
    int              videoStreamIdx = -1;
    int              width = 0;
    int              height = 0;
    double           duration = 0;
    double           fps = 0;
    bool             finished = false;
    bool             draining = false;
    unsigned char*   rgbaBuffer = nullptr;

    // Audio
    AVCodecContext*   audioCodecCtx = nullptr;
    SwrContext*       swrCtx = nullptr;
    AVFrame*         audioFrame = nullptr;
    int              audioStreamIdx = -1;
    int              audioSampleRate = 0;
    int              audioChannels = 0;
    bool             hasAudio = false;

    // Audio output buffer (S16 interleaved PCM)
    short*           audioBuf = nullptr;
    int              audioBufSize = 0;    // samples per channel available
    int              audioBufCapacity = 0;
};

// ── Lifecycle ───────────────────────────────────────────

VIDEO_API VideoPlayer video_open(const char* path) {
    auto* vp = new VideoPlayerHandle();

    // Open file
    if (avformat_open_input(&vp->fmtCtx, path, nullptr, nullptr) < 0) {
        delete vp;
        return nullptr;
    }

    if (avformat_find_stream_info(vp->fmtCtx, nullptr) < 0) {
        avformat_close_input(&vp->fmtCtx);
        delete vp;
        return nullptr;
    }

    // Find video stream
    for (unsigned i = 0; i < vp->fmtCtx->nb_streams; i++) {
        if (vp->fmtCtx->streams[i]->codecpar->codec_type == AVMEDIA_TYPE_VIDEO) {
            vp->videoStreamIdx = (int)i;
            break;
        }
    }
    if (vp->videoStreamIdx < 0) {
        avformat_close_input(&vp->fmtCtx);
        delete vp;
        return nullptr;
    }

    auto* codecpar = vp->fmtCtx->streams[vp->videoStreamIdx]->codecpar;

    // Find decoder
    auto* codec = avcodec_find_decoder(codecpar->codec_id);
    if (!codec) {
        avformat_close_input(&vp->fmtCtx);
        delete vp;
        return nullptr;
    }

    vp->codecCtx = avcodec_alloc_context3(codec);
    avcodec_parameters_to_context(vp->codecCtx, codecpar);

    if (avcodec_open2(vp->codecCtx, codec, nullptr) < 0) {
        avcodec_free_context(&vp->codecCtx);
        avformat_close_input(&vp->fmtCtx);
        delete vp;
        return nullptr;
    }

    vp->width = vp->codecCtx->width;
    vp->height = vp->codecCtx->height;

    // Duration
    if (vp->fmtCtx->duration > 0)
        vp->duration = (double)vp->fmtCtx->duration / AV_TIME_BASE;

    // FPS
    auto stream = vp->fmtCtx->streams[vp->videoStreamIdx];
    if (stream->avg_frame_rate.den > 0)
        vp->fps = av_q2d(stream->avg_frame_rate);
    else if (stream->r_frame_rate.den > 0)
        vp->fps = av_q2d(stream->r_frame_rate);
    else
        vp->fps = 30.0;

    // Allocate frames
    vp->frame = av_frame_alloc();
    vp->rgbaFrame = av_frame_alloc();
    vp->packet = av_packet_alloc();

    // RGBA buffer
    int bufSize = av_image_get_buffer_size(AV_PIX_FMT_RGBA, vp->width, vp->height, 1);
    vp->rgbaBuffer = (unsigned char*)av_malloc(bufSize);
    av_image_fill_arrays(vp->rgbaFrame->data, vp->rgbaFrame->linesize,
                         vp->rgbaBuffer, AV_PIX_FMT_RGBA, vp->width, vp->height, 1);

    // SWS context for YUV -> RGBA conversion
    vp->swsCtx = sws_getContext(vp->width, vp->height, vp->codecCtx->pix_fmt,
                                 vp->width, vp->height, AV_PIX_FMT_RGBA,
                                 SWS_BILINEAR, nullptr, nullptr, nullptr);

    // ── Audio stream ──
    for (unsigned i = 0; i < vp->fmtCtx->nb_streams; i++) {
        if (vp->fmtCtx->streams[i]->codecpar->codec_type == AVMEDIA_TYPE_AUDIO) {
            vp->audioStreamIdx = (int)i;
            break;
        }
    }

    if (vp->audioStreamIdx >= 0) {
        auto* apar = vp->fmtCtx->streams[vp->audioStreamIdx]->codecpar;
        auto* acodec = avcodec_find_decoder(apar->codec_id);
        if (acodec) {
            vp->audioCodecCtx = avcodec_alloc_context3(acodec);
            avcodec_parameters_to_context(vp->audioCodecCtx, apar);
            if (avcodec_open2(vp->audioCodecCtx, acodec, nullptr) >= 0) {
                vp->hasAudio = true;
                vp->audioSampleRate = vp->audioCodecCtx->sample_rate;
                vp->audioChannels = vp->audioCodecCtx->ch_layout.nb_channels;
                if (vp->audioChannels < 1) vp->audioChannels = 2;

                vp->audioFrame = av_frame_alloc();

                // Resample to S16 stereo at native sample rate
                vp->swrCtx = swr_alloc();
                AVChannelLayout outLayout;
                av_channel_layout_default(&outLayout, vp->audioChannels);
                swr_alloc_set_opts2(&vp->swrCtx,
                    &outLayout, AV_SAMPLE_FMT_S16, vp->audioSampleRate,
                    &vp->audioCodecCtx->ch_layout, vp->audioCodecCtx->sample_fmt, vp->audioSampleRate,
                    0, nullptr);
                swr_init(vp->swrCtx);

                // Audio buffer (enough for ~1 second)
                vp->audioBufCapacity = vp->audioSampleRate;
                vp->audioBuf = (short*)malloc(vp->audioBufCapacity * vp->audioChannels * sizeof(short));
                vp->audioBufSize = 0;
            }
        }
    }

    return vp;
}

VIDEO_API void video_close(VideoPlayer vp) {
    if (!vp) return;
    if (vp->swsCtx) sws_freeContext(vp->swsCtx);
    if (vp->rgbaBuffer) av_free(vp->rgbaBuffer);
    if (vp->rgbaFrame) av_frame_free(&vp->rgbaFrame);
    if (vp->frame) av_frame_free(&vp->frame);
    if (vp->packet) av_packet_free(&vp->packet);
    if (vp->codecCtx) avcodec_free_context(&vp->codecCtx);
    // Audio cleanup
    if (vp->swrCtx) swr_free(&vp->swrCtx);
    if (vp->audioFrame) av_frame_free(&vp->audioFrame);
    if (vp->audioCodecCtx) avcodec_free_context(&vp->audioCodecCtx);
    if (vp->audioBuf) free(vp->audioBuf);
    if (vp->fmtCtx) avformat_close_input(&vp->fmtCtx);
    delete vp;
}

// ── Info ────────────────────────────────────────────────

VIDEO_API int   video_width(VideoPlayer vp)    { return vp ? vp->width : 0; }
VIDEO_API int   video_height(VideoPlayer vp)   { return vp ? vp->height : 0; }
VIDEO_API double video_duration(VideoPlayer vp) { return vp ? vp->duration : 0; }
VIDEO_API double video_fps(VideoPlayer vp)      { return vp ? vp->fps : 0; }
VIDEO_API int   video_finished(VideoPlayer vp)  { return vp ? (int)vp->finished : 1; }

// ── Playback ────────────────────────────────────────────

VIDEO_API int video_next_frame(VideoPlayer vp) {
    if (!vp || vp->finished) return 0;

    vp->audioBufSize = 0; // reset audio buffer for this frame

    while (true) {
        int ret = avcodec_receive_frame(vp->codecCtx, vp->frame);
        if (ret >= 0) {
            sws_scale(vp->swsCtx, vp->frame->data, vp->frame->linesize,
                0, vp->height, vp->rgbaFrame->data, vp->rgbaFrame->linesize);
            return 1;
        }
        if (ret == AVERROR_EOF || vp->draining) {
            vp->finished = true;
            return 0;
        }
        ret = av_read_frame(vp->fmtCtx, vp->packet);
        if (ret < 0) {
            vp->draining = true;
            avcodec_send_packet(vp->codecCtx, nullptr);
            continue;
        }

        // Audio packet — decode and buffer
        if (vp->hasAudio && vp->packet->stream_index == vp->audioStreamIdx) {
            avcodec_send_packet(vp->audioCodecCtx, vp->packet);
            while (avcodec_receive_frame(vp->audioCodecCtx, vp->audioFrame) >= 0) {
                int outSamples = vp->audioFrame->nb_samples;
                if (vp->audioBufSize + outSamples <= vp->audioBufCapacity) {
                    uint8_t* outBuf = (uint8_t*)(vp->audioBuf + vp->audioBufSize * vp->audioChannels);
                    int converted = swr_convert(vp->swrCtx, &outBuf, outSamples,
                        (const uint8_t**)vp->audioFrame->data, outSamples);
                    if (converted > 0) vp->audioBufSize += converted;
                }
            }
            av_packet_unref(vp->packet);
            continue;
        }

        // Video packet
        if (vp->packet->stream_index != vp->videoStreamIdx) {
            av_packet_unref(vp->packet);
            continue;
        }

        ret = avcodec_send_packet(vp->codecCtx, vp->packet);
        av_packet_unref(vp->packet);
        if (ret < 0) continue;
    }
}

VIDEO_API const unsigned char* video_frame_data(VideoPlayer vp) {
    return vp ? vp->rgbaBuffer : nullptr;
}

VIDEO_API void video_seek(VideoPlayer vp, double seconds) {
    if (!vp || !std::isfinite(seconds) || seconds < 0) return;
    int64_t ts = (int64_t)(seconds * AV_TIME_BASE);
    if (av_seek_frame(vp->fmtCtx, -1, ts, AVSEEK_FLAG_BACKWARD) < 0) return;
    avcodec_flush_buffers(vp->codecCtx);
    if (vp->audioCodecCtx) avcodec_flush_buffers(vp->audioCodecCtx);
    if (vp->swrCtx) { swr_close(vp->swrCtx); swr_init(vp->swrCtx); }
    vp->audioBufSize = 0;
    vp->draining = false;
    vp->finished = false;
}

VIDEO_API void video_rewind(VideoPlayer vp) {
    video_seek(vp, 0);
}

// ── Audio ───────────────────────────────────────────────

VIDEO_API int video_has_audio(VideoPlayer vp) {
    return vp && vp->hasAudio ? 1 : 0;
}

VIDEO_API int video_audio_sample_rate(VideoPlayer vp) {
    return vp ? vp->audioSampleRate : 0;
}

VIDEO_API int video_audio_channels(VideoPlayer vp) {
    return vp ? vp->audioChannels : 0;
}

VIDEO_API int video_get_audio(VideoPlayer vp, short* buffer, int max_samples) {
    if (!vp || !vp->hasAudio || vp->audioBufSize <= 0 || max_samples <= 0 || !buffer) return 0;
    int samples = vp->audioBufSize < max_samples ? vp->audioBufSize : max_samples;
    memcpy(buffer, vp->audioBuf, samples * vp->audioChannels * sizeof(short));
    return samples;
}
