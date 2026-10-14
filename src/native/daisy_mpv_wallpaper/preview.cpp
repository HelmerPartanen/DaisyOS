// Keep the offscreen renderer/decoder out of the long-lived desktop process.
#include "mpv_wallpaper.h"
#include <charconv>
#include <cstdio>
#include <cstring>
#include <vector>

static bool dimension(const char *value, int &result) {
    const auto end = value + std::strlen(value);
    const auto parsed = std::from_chars(value, end, result);
    return parsed.ec == std::errc{} && parsed.ptr == end && result > 0 && result <= 4096;
}
int main(int argc, char **argv) {
    int width = 0, height = 0;
    if (argc != 5 || !dimension(argv[2], width) || !dimension(argv[3], height)) return 2;
    std::vector<unsigned char> pixels(size_t(width) * height * 4);
    if (daisy_mpv_preview(argv[1], width, height, pixels.data(), width * 4) != 0) return 3;
    auto output = std::fopen(argv[4], "wb");
    if (!output) return 4;
    const auto written = std::fwrite(pixels.data(), 1, pixels.size(), output);
    const int closed = std::fclose(output);
    return written == pixels.size() && closed == 0 ? 0 : 5;
}
