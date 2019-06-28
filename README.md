# FFMpegWrapper
Wraps up a bunch of FFMPEG calls into a simple DLL which can be used for
controlling and retrieving frames from a webcam.

This was made after one too many frustrations using OpenCV for the same task
(mostly to do with certain camera's either not working at all, or only running
at very low framerates). FFMPEG seems to do a much better job (though a much
more complex API).

This has been written with usage in a .NET WPF app in mind and a WPF example
app is included.

It can retrieve frames completedly unconverted (in whatever format the camera
sends the frame in - eg: MJPEG), or convert them to BGR24 (which can easily be
converted to Bitmap or OpenCV images).

The wrapper can also perform Cropping and rotation of BGR24 converted images.