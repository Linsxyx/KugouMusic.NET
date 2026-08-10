module;

#include <Windows.h>
#include <dwrite.h>
#include <algorithm>
#include <mutex>
#include <sstream>
#include <string>
#include <unordered_map>
#include <vector>

export module plugin.Config;

auto stringToWString(const std::string &value) -> std::wstring {
    if (value.empty()) return {};
    const auto length = MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0);
    if (length <= 0) return {};
    std::wstring result(static_cast<size_t>(length), L'\0');
    MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), length);
    return result;
}

auto hexToString(const std::string &value) -> std::string {
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

export enum TASKBAR_WINDOW_ALIGNMENT {
    TASKBAR_WINDOW_ALIGNMENT_AUTO,
    TASKBAR_WINDOW_ALIGNMENT_LEFT,
    TASKBAR_WINDOW_ALIGNMENT_CENTER,
    TASKBAR_WINDOW_ALIGNMENT_RIGHT
};

export struct TimedWord {
    double start_ms = 0;
    double duration_ms = 0;
    std::wstring text;
};

export struct Config {
    std::wstring lyric_primary = L" ";
    std::wstring lyric_secondary = L" ";
    std::wstring font_family = L"Microsoft YaHei UI";
    std::vector<TimedWord> words;
    double playback_position_ms = 0;
    double line_start_ms = 0;
    double line_duration_ms = 0;
    int margin_left = 0;
    int margin_right = 0;
    int line_spacing = 2;
    TASKBAR_WINDOW_ALIGNMENT window_alignment = TASKBAR_WINDOW_ALIGNMENT_AUTO;
    unsigned int color_primary = 0xFF2E2E2E;
    unsigned int color_played = 0xFF268EEB;
    int size_primary = 17;
    int size_primary_single = 20;
    bool underline_primary = false;
    bool strikethrough_primary = false;
    DWRITE_FONT_WEIGHT weight_primary = DWRITE_FONT_WEIGHT_MEDIUM;
    DWRITE_FONT_STYLE slope_primary = DWRITE_FONT_STYLE_NORMAL;
    DWRITE_TEXT_ALIGNMENT align_primary = DWRITE_TEXT_ALIGNMENT_LEADING;
    unsigned int color_secondary = 0xFF2E2E2E;
    int size_secondary = 12;
    bool underline_secondary = false;
    bool strikethrough_secondary = false;
    DWRITE_FONT_WEIGHT weight_secondary = DWRITE_FONT_WEIGHT_NORMAL;
    DWRITE_FONT_STYLE slope_secondary = DWRITE_FONT_STYLE_NORMAL;
    DWRITE_TEXT_ALIGNMENT align_secondary = DWRITE_TEXT_ALIGNMENT_LEADING;
};

export Config config;
std::mutex config_mutex;

auto parseWords(const std::string &value) -> std::vector<TimedWord> {
    std::vector<TimedWord> words;
    std::stringstream entries(value);
    std::string entry;
    while (std::getline(entries, entry, ';')) {
        const auto first = entry.find(',');
        const auto second = first == std::string::npos ? std::string::npos : entry.find(',', first + 1);
        if (first == std::string::npos || second == std::string::npos) continue;
        try {
            TimedWord word;
            word.start_ms = std::stod(entry.substr(0, first));
            word.duration_ms = std::max(1.0, std::stod(entry.substr(first + 1, second - first - 1)));
            word.text = stringToWString(hexToString(entry.substr(second + 1)));
            words.emplace_back(std::move(word));
        } catch (...) {
        }
    }
    return words;
}

export auto snapshotConfig() -> Config {
    std::lock_guard lock(config_mutex);
    return config;
}

export auto setConfig(const std::string &key, const std::string &value) -> bool {
    std::lock_guard lock(config_mutex);
    try {
        if (key == "lyric_primary") {
            auto next = stringToWString(value);
            if (next == config.lyric_primary) return false;
            config.lyric_primary = std::move(next);
        } else if (key == "lyric_secondary") {
            config.lyric_secondary = stringToWString(value);
        } else if (key == "playback_position_ms") {
            config.playback_position_ms = std::stod(value);
        } else if (key == "line_start_ms") {
            config.line_start_ms = std::stod(value);
        } else if (key == "line_duration_ms") {
            config.line_duration_ms = std::stod(value);
        } else if (key == "word_timings") {
            config.words = parseWords(value);
        } else if (key == "text_alignment") {
            const auto alignment = std::stoi(value) == 1
                ? DWRITE_TEXT_ALIGNMENT_TRAILING
                : DWRITE_TEXT_ALIGNMENT_LEADING;
            config.align_primary = alignment;
            config.align_secondary = alignment;
        } else if (key == "font_family") {
            config.font_family = stringToWString(value);
        } else if (key == "margin_left") {
            config.margin_left = std::stoi(value);
        } else if (key == "margin_right") {
            config.margin_right = std::stoi(value);
        } else if (key == "line_spacing") {
            config.line_spacing = std::stoi(value);
        } else if (key == "window_alignment") {
            const auto alignment = static_cast<TASKBAR_WINDOW_ALIGNMENT>(std::stoi(value));
            if (alignment == config.window_alignment) return false;
            config.window_alignment = alignment;
        } else if (key == "size_primary") {
            config.size_primary = std::clamp(std::stoi(value), 12, 24);
            config.size_primary_single = config.size_primary + 3;
            config.size_secondary = std::max(10, config.size_primary - 5);
        } else if (key == "color_primary") {
            config.color_primary = std::stoul(value, nullptr, 0);
        } else if (key == "color_played") {
            config.color_played = std::stoul(value, nullptr, 0);
        } else if (key == "size_secondary") {
            config.size_secondary = std::stoi(value);
        } else if (key == "color_secondary") {
            config.color_secondary = std::stoul(value, nullptr, 0);
        } else {
            return false;
        }
        return true;
    } catch (...) {
        return false;
    }
}
