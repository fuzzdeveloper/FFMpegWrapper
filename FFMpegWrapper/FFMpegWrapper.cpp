#include "stdafx.h"

#include <thread> 
#include <atomic> 

extern "C"
{
#include "libavformat/avformat.h"
#include "libavcodec/avcodec.h"
#include "libavdevice/avdevice.h"
#include "libswscale/swscale.h"
#include "libavutil/imgutils.h"
}
#include "FFMpegWrapper.h"
#include "DsDeviceEnumerator.h"

#pragma warning(disable: 4996)

std::thread _thread;
std::atomic<bool> _running(false);

void sendError(error_callback_t error_callback, const char* error) {
    error_callback((char*)error, (int)strlen(error));
}

void getOffsetDataFromFrame(AVFrame *pFrame, uint8_t **offset_data, int x, int y)
{
    int i, s;
    for (i = 0; i < AV_NUM_DATA_POINTERS; i++)
    {
        offset_data[i] = pFrame->data[i];
        if (offset_data[i] == NULL || x < 0 || y < 0 || (x == 0 && y == 0))
            continue;
        s = pFrame->linesize[i];
        offset_data[i] += (y * s) + ((x * s) / pFrame->width);
    }
}

void flipFrameBytes(uint8_t *src, uint8_t *dst, int stride, int w, int h, bool flip_h, bool flip_v)
{
    int bpp, x, y, z, dx, dy;
    bpp = stride / w;
    if (bpp > 1)
    {
        for (y = 0; y < h; y++)
        {
            dy = flip_v ? h - y - 1 : y;
            for (x = 0; x < w; x++)
            {
                dx = flip_h ? w - x - 1 : x;
                for (z = 0; z < bpp; z++)
                {
                    dst[dy * stride + dx * bpp + z] = src[y * stride + x * bpp + z];
                }
            }
        }
    }
    else
    {
        for (y = 0; y < h; y++)
        {
            dy = flip_v ? h - y - 1 : y;
            for (x = 0; x < stride; x++)
            {
                dx = flip_h ? stride - x - 1 : x;
                dst[dy * stride + dx] = src[y * stride + x];
            }
        }
    }
}

void transposeFrameBytes(uint8_t *src, uint8_t *dst, int stride, int w, int h)
{
    int bpp, x, y, z;
    bpp = stride / w;
    int newstride = (h * stride) / w;
    if (bpp > 1)
    {
        for (y = 0; y < h; y++)
        {
            for (x = 0; x < w; x++)
            {
                for (z = 0; z < bpp; z++)
                {
                    dst[x * newstride + y * bpp + z] = src[y * stride + x * bpp + z];
                }
            }
        }
    }
    else
    {
        for (y = 0; y < h; y++)
        {
            for (x = 0; x < stride; x++)
            {
                dst[x * newstride + y] = src[y * stride + x];
            }
        }
    }
}

void loop(char* device_name, char* vcodec, char* framerate, char* video_size, bool show_video_device_dialog, bool no_convert, int crop_x, int crop_y, int crop_w, int crop_h, bool flip_h, bool flip_v, bool transpose, error_callback_t error_callback, image_callback_t image_callback) {
    char str[1024];
    uint8_t *offset_data[AV_NUM_DATA_POINTERS];
    uint8_t *bgr_data[AV_NUM_DATA_POINTERS];
    int bgr_linesize[AV_NUM_DATA_POINTERS];
    uint8_t *flipped_transposed_bytes;
    int i;
    int videoindex;
    int w;
    int h;
    int ret;
    int got_picture;
    AVInputFormat *pInputFormat;
    AVDictionary *pDictionary;
    AVFormatContext *pFormatContext;
    AVCodecContext    *pCodecContext;
    AVCodec *pCodec;
    AVPacket *pPacket;
    AVFrame *pFrame;
    struct SwsContext *img_convert_ctx;

	if (device_name == NULL) {
		device_name = GetFirstVideoInputDevice();
		if (device_name == NULL) {
			sendError(error_callback, "Couldn't find any input devices.");
			_running = false;
			return;
		}
	}
	
	avdevice_register_all();

    //Show Dshow Device
    //    show_dshow_device();
    //Show Device Options
    //    show_dshow_device_option();

    pInputFormat = av_find_input_format("dshow");
    pDictionary = NULL;
	if (show_video_device_dialog) {
		av_dict_set(&pDictionary, "show_video_device_dialog", "true", 0);
	}
	if (vcodec != NULL) {
		av_dict_set(&pDictionary, "vcodec", vcodec, 0);
	}
	if (framerate != NULL) {
        av_dict_set(&pDictionary, "framerate", framerate, 0);
    }
    if (video_size != NULL) {
        av_dict_set(&pDictionary, "video_size", video_size, 0);
    }
    pFormatContext = avformat_alloc_context();
    sprintf(str, "video=%s", device_name);
    if (avformat_open_input(&pFormatContext, str, pInputFormat, &pDictionary) != 0){
        sendError(error_callback, "Couldn't open input stream.");
        _running = false;
        return;
    }
    if (avformat_find_stream_info(pFormatContext, NULL)<0)
    {
        sendError(error_callback, "Couldn't find stream information.");
        avformat_close_input(&pFormatContext);
        _running = false;
        return;
    }
    videoindex = -1;
    for (i = 0; i < (int)pFormatContext->nb_streams; i++)
    {
        if (pFormatContext->streams[i]->codec->codec_type == AVMEDIA_TYPE_VIDEO)
        {
            videoindex = i;
            break;
        }
    }
    if (videoindex == -1)
    {
        sendError(error_callback, "Couldn't find a video stream.");
        avformat_close_input(&pFormatContext);
        _running = false;
        return;
    }
    pCodecContext = pFormatContext->streams[videoindex]->codec;
    pCodec = avcodec_find_decoder(pCodecContext->codec_id);
    if (pCodec == NULL)
    {
        sendError(error_callback, "Codec not found.");
        avformat_close_input(&pFormatContext);
        _running = false;
        return;
    }
    if (avcodec_open2(pCodecContext, pCodec, NULL)<0)
    {
        sendError(error_callback, "Could not open codec.");
        avformat_close_input(&pFormatContext);
        //close pCodecContext or codec?
        _running = false;
        return;
    }
    w = pCodecContext->width;
    h = pCodecContext->height;
    pPacket = av_packet_alloc();
    if (no_convert) {
        while (_running.load())
        {
            if (av_read_frame(pFormatContext, pPacket) >= 0)
            {
                if (pPacket->stream_index == videoindex)
                {
                    image_callback((char*)pPacket->data, pPacket->size, w, h);
                }
                av_free_packet(pPacket);
            }
            else
            {
                sendError(error_callback, "Stream finished.");
                break;
            }
        }
    }
    else
    {
        if (crop_x >= 0 && crop_y >= 0 && crop_x + crop_w <= w && crop_y + crop_h <= h)
        {
            w = crop_w;
            h = crop_h;
        }
        else
        {
            crop_x = -1;
            crop_y = -1;
        }
        i = w * h * 3;//size of frame in bytes
        pFrame = av_frame_alloc();
        bgr_linesize[0] = w * 3;//stride
        bgr_data[0] = new uint8_t[i];
        flipped_transposed_bytes = flip_h || flip_v || transpose ? new uint8_t[i] : NULL;
        img_convert_ctx = sws_getContext(w, h, pCodecContext->pix_fmt, w, h, AV_PIX_FMT_BGR24, SWS_FAST_BILINEAR, NULL, NULL, NULL);
        while (_running.load())
        {
            if (av_read_frame(pFormatContext, pPacket) >= 0)
            {
                if (pPacket->stream_index == videoindex)
                {
                    ret = avcodec_decode_video2(pCodecContext, pFrame, &got_picture, pPacket);
                    if (ret < 0)
                    {
                        sendError(error_callback, "Decode Error.");
                        break;
                    }
                    if (got_picture)
                    {
                        getOffsetDataFromFrame(pFrame, offset_data, crop_x, crop_y);
                        sws_scale(img_convert_ctx, (const unsigned char* const*)offset_data, pFrame->linesize, 0, h, bgr_data, bgr_linesize);
                        if (flip_h || flip_v)
                        {
                            flipFrameBytes(bgr_data[0], flipped_transposed_bytes, bgr_linesize[0], w, h, flip_h, flip_v);
                            if (transpose)
                            {
                                transposeFrameBytes(flipped_transposed_bytes, bgr_data[0], bgr_linesize[0], w, h);
                                image_callback((char*)bgr_data[0], i, h, w);
                            }
                            else
                            {
                                image_callback((char*)flipped_transposed_bytes, i, w, h);
                            }
                        }
                        else if (transpose)
                        {
                            transposeFrameBytes(bgr_data[0], flipped_transposed_bytes, bgr_linesize[0], w, h);
                            image_callback((char*)flipped_transposed_bytes, i, h, w);
                        }
                        else
                        {
                            image_callback((char*)bgr_data[0], i, w, h);
                        }
                    }
                }
                av_free_packet(pPacket);
            }
            else
            {
                sendError(error_callback, "Stream finished.");
                break;
            }
        }
        if (NULL != flipped_transposed_bytes)
            free(flipped_transposed_bytes);
        free(bgr_data[0]);
        av_free(pFrame);
        
    }
    //av_packet_free(&pPacket);
    avcodec_close(pCodecContext);
    //avcodec_free_context(&pCodecContext);
    avformat_close_input(&pFormatContext);
    _running = false;
    return;
}

void __stdcall start(char* device_name, char* vcodec, char* framerate, char* video_size, bool show_video_device_dialog, bool no_convert, int crop_x, int crop_y, int crop_w, int crop_h, bool flip_h, bool flip_v, bool transpose, error_callback_t error_callback, image_callback_t image_callback)
{
    stop();
    _running = true;
    _thread = std::thread(loop, device_name, vcodec, framerate, video_size, show_video_device_dialog, no_convert, crop_x, crop_y, crop_w, crop_h, flip_h, flip_v, transpose, error_callback, image_callback);
}

void __stdcall stop() {
    if (_running.load())
        _running = false;
    if (_thread.joinable())
        _thread.join();
}

int __stdcall get_ds_video_input_devices(char** devices) {
    return GetVideoInputDevices(devices);
}

