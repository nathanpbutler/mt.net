<!-- markdownlint-disable MD024 -->
# Migration: ImageSharp to FFmpeg — Complete

**Status: complete as of v2.4.0.**

This document recorded the migration of image composition from SixLabors.ImageSharp to
FFmpeg.AutoGen. That migration is now finished: ImageSharp, ImageSharp.Drawing and
SixLabors.Fonts have all been removed, and FFmpeg is the only imaging dependency.

## Timeline

| Version | Change |
|---|---|
| v2.0 | FFmpeg.AutoGen composer added and made the default. Hybrid: FFmpeg filter graphs for rendering, ImageSharp for canvas allocation, layout, watermarks and encoding. `--composer` added to switch between the two. |
| v2.4.0 | Migration completed. ImageSharp removed entirely, the legacy composer deleted, and `--composer` removed. |

## What v2.4.0 changed

**Removed:**

- `SixLabors.ImageSharp`, `SixLabors.ImageSharp.Drawing`, `SixLabors.Fonts` package references
- `Services/ImageComposer.cs` — the legacy ImageSharp composer
- `Services/FilterService.cs` — the ImageSharp filter implementation
- The `--composer` option; `FFmpegFilterGraphComposer` is now unconditional

**Added:**

- `Models/RgbaImage.cs` — a pooled, tightly-packed RGBA raster that replaces
  `Image<Rgba32>` as the pixel type passed across every service boundary. Supports the
  three operations the pipeline actually needs: fill, opaque blit, and alpha blit.
- `Utilities/RgbaColor.cs` — replaces `SixLabors.ImageSharp.Color` / `Rgba32`.
- `Utilities/AVFrameBridge.cs` — the single `RgbaImage` ↔ `AVFrame` converter, replacing
  five near-identical hand-rolled per-pixel implementations.
- `Services/ImageEncoder.cs` — PNG and JPEG output via FFmpeg's own encoders, replacing
  `SaveAsPngAsync` / `SaveAsJpegAsync`.
- `Services/ImageLoader.cs` — still-image decoding via FFmpeg, replacing `Image.Load`.
- `Services/WatermarkService.cs` — relocated from `ImageComposer.ApplyWatermark`, which
  was shared by both composers.

**Fixed along the way:**

- The decoder now converts straight to `AV_PIX_FMT_RGBA` instead of `RGB24` followed by a
  per-pixel repack, so frame ingress is a row-wise copy.
- `AVFrameBridge.ToAVFrame` allocates via `av_frame_get_buffer`, so the frame owns its
  buffer. The previous implementations used `av_malloc` and stored the raw pointer into
  `frame->data[]` without an `AVBufferRef`, leaking one full frame buffer per call.

## Current pipeline

```plaintext
video file
  └── FFmpegAutoGenVideoDecoder      libavcodec decode + swscale to RGBA
        └── RgbaImage                 pooled RGBA raster
              └── FFmpegFilterGraphComposer
                    ├── per frame:    scale, drawtext (timestamp), drawbox (border), v360
                    ├── filters:      FFmpegFilterService (colorchannelmixer, negate, ...)
                    ├── header:       drawtext onto a filled canvas
                    └── layout:       RgbaImage.Fill + DrawImage blits
                          └── ImageEncoder    PNG (rgba) or MJPEG (yuvj420p)
```

## Requirement this introduces

Text rendering depends on FFmpeg's `drawtext` filter, which requires a build compiled with
libfreetype. Homebrew's stock `ffmpeg` formula is no longer built with it — use
`brew install ffmpeg-full` on macOS. `FFmpegHelper` searches the `ffmpeg-full` keg first for
this reason, and `FFmpegHelper.WarnIfDrawTextMissing()` prints an explicit warning when the
loaded build cannot render text, rather than silently omitting it.
