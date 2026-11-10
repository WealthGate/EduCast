#!/usr/bin/env bash
# Build the Windows x64 engine from pinned FFmpeg source on a Linux build host.
set -euo pipefail
script_dir=$(cd "$(dirname "$0")" && pwd)
output=$(mkdir -p "$1" && cd "$1" && pwd)
version=8.1.3
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
cd "$work"
archive="ffmpeg-$version.tar.xz"
curl -fSL --retry 3 --connect-timeout 15 --max-time 120 "https://ffmpeg.org/releases/$archive" -o "$archive"
echo "7138d28c96d9d3e3af4ee3d8cad72741f8ffb40da90c1112235dea3ecd3178a3  $archive" | sha256sum --check
tar -xf "$archive"
cd "ffmpeg-$version"
patch -p1 < "$script_dir/ffmpeg-educast.patch"
assembly=()
if ! command -v nasm >/dev/null; then
  assembly+=(--disable-x86asm)
fi
./configure --enable-cross-compile --cross-prefix=x86_64-w64-mingw32- --target-os=mingw32 --arch=x86_64 \
  --cc=x86_64-w64-mingw32-clang "${assembly[@]}" \
  --enable-static --disable-shared --disable-autodetect --disable-debug --disable-doc \
  --disable-ffplay --disable-ffprobe --disable-everything --enable-schannel --enable-mediafoundation --enable-d3d11va \
  --extra-ldflags=-static \
  --enable-protocol=file,pipe,tcp,rtmp,rtmps,tls \
  --enable-indev=dshow,lavfi \
  --enable-demuxer=avi,flv,mov,matroska,mp3,wav,aac,rawvideo \
  --enable-muxer=avi,flv,mp4,rawvideo,pcm_s16le,null \
  --enable-decoder=rawvideo,wrapped_avframe,h264,hevc,mpeg4,mjpeg,vp8,vp9,av1,aac,alac,mp3,opus,vorbis,flac,pcm_s16le,pcm_s24le,pcm_s32le,pcm_f32le,pcm_f64le,pcm_u8,pcm_alaw,pcm_mulaw \
  --enable-encoder=rawvideo,wrapped_avframe,h264_mf,aac,pcm_s16le \
  --enable-filter=scale,pad,fps,setsar,format,aformat,aresample,anull,null,buffer,buffersink,abuffer,abuffersink,color,sine,testsrc2,drawbox,crop,aevalsrc
grep -q '^#define CONFIG_H264_MF_ENCODER 1$' config_components.h
grep -q '^#define CONFIG_RTMPS_PROTOCOL 1$' config_components.h
grep -q '^#define CONFIG_DSHOW_INDEV 1$' config_components.h
make -j "$(nproc)" ffmpeg.exe
cp ffmpeg.exe "$output/EduCast.Media.exe"
cp COPYING.LGPLv2.1 "$output/EduCast.Media.LICENSE.txt"
mkdir "$work/recipe"
cp "$script_dir/Build-MediaEngine.sh" "$script_dir/Fetch-MediaToolchain.sh" "$script_dir/ffmpeg-educast.patch" "$work/recipe/"
cp config.h config_components.h ffbuild/config.mak "$work/recipe/"
tar -cJf "$output/EduCast-Media-Source.tar.xz" -C "$work" "$archive" recipe
