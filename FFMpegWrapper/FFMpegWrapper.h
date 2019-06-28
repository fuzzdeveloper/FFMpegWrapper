#ifdef __cplusplus
extern "C" {  // only need to export C interface if
	// used by C++ source code
#endif
	typedef void(__stdcall *error_callback_t)(char* error, int len);
	typedef void(__stdcall *image_callback_t)(char* img, int len, int w, int h);
    __declspec(dllexport) void __stdcall start(char* device_name, char* vcodec, char* framerate, char* video_size, bool show_video_device_dialog, bool no_convert, int crop_x, int crop_y, int crop_w, int crop_h, bool flip_h, bool flip_v, bool transpose, error_callback_t error_callback, image_callback_t image_callback);
    __declspec(dllexport) void __stdcall stop();
	__declspec(dllexport) int __stdcall get_ds_video_input_devices(char** devices);

#ifdef __cplusplus
}
#endif