#include <Windows.h>
#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>

import plugin.Config;
import plugin.Plugin;

auto splitTabs(const std::string &line) -> std::vector<std::string> {
    std::vector<std::string> fields;
    size_t start = 0;
    while (true) {
        const auto separator = line.find('\t', start);
        if (separator == std::string::npos) {
            fields.emplace_back(line.substr(start));
            break;
        }
        fields.emplace_back(line.substr(start, separator - start));
        start = separator + 1;
    }
    return fields;
}

auto decodeHex(const std::string &value) -> std::string {
    if (value.size() % 2 != 0) return {};
    std::string result;
    result.reserve(value.size() / 2);
    for (size_t index = 0; index < value.size(); index += 2) {
        try {
            result.push_back(static_cast<char>(std::stoul(value.substr(index, 2), nullptr, 16)));
        } catch (...) {
            return {};
        }
    }
    return result;
}

auto applyUpdate(const std::string &line) -> void {
    auto fields = splitTabs(line);
    if (fields.size() < 15 || fields[0] != "U") return;
    setConfig("playback_position_ms", fields[1]);
    setConfig("line_start_ms", fields[4]);
    setConfig("line_duration_ms", fields[5]);
    setConfig("lyric_primary", decodeHex(fields[6]));
    setConfig("lyric_secondary", decodeHex(fields[7]));
    setConfig("word_timings", fields[8]);
    setConfig("text_alignment", fields[9]);
    auto layoutChanged = setConfig(
        "window_alignment",
        fields[9] == "1" ? "3" : "1");
    setConfig("font_family", decodeHex(fields[10]));
    setConfig("size_primary", fields[11]);
    setConfig("color_primary", fields[12]);
    setConfig("color_secondary", fields[12]);
    setConfig("color_played", fields[13]);
    layoutChanged = setConfig("horizontal_offset", fields[14]) || layoutChanged;
    if (layoutChanged) Plugin::updateLayout();
    Plugin::refresh();
}

auto WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) -> int {
    CoInitializeEx(nullptr, COINIT_MULTITHREADED | COINIT_DISABLE_OLE1DDE);
    Plugin::getInstance();

    std::string line;
    while (std::getline(std::cin, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        applyUpdate(line);
    }

    ExitProcess(0);
}
