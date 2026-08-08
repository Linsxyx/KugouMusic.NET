module;

#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <string>

export module window.Lyrics;

import plugin.Config;

export class Lyrics {
private:
    ID2D1RenderTarget *renderTarget = nullptr;
    IDWriteFactory *writeFactory = nullptr;

    auto createTextFormat(
        const Config &state,
        bool secondary,
        float fontSize) const -> Microsoft::WRL::ComPtr<IDWriteTextFormat> {
        Microsoft::WRL::ComPtr<IDWriteTextFormat> format;
        if (!writeFactory) return format;
        writeFactory->CreateTextFormat(
            state.font_family.c_str(),
            nullptr,
            secondary ? state.weight_secondary : state.weight_primary,
            secondary ? state.slope_secondary : state.slope_primary,
            DWRITE_FONT_STRETCH_NORMAL,
            fontSize,
            L"zh-cn",
            &format);
        if (format) {
            format->SetTextAlignment(
                secondary ? state.align_secondary : state.align_primary);
            format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_NEAR);
            format->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
        }
        return format;
    }

    static auto toColor(unsigned int argb) -> D2D1_COLOR_F {
        return D2D1::ColorF(
            static_cast<float>((argb >> 16) & 0xFF) / 255.0f,
            static_cast<float>((argb >> 8) & 0xFF) / 255.0f,
            static_cast<float>(argb & 0xFF) / 255.0f,
            static_cast<float>((argb >> 24) & 0xFF) / 255.0f);
    }

    auto measureText(
        const std::wstring &text,
        IDWriteTextFormat *format,
        float maxWidth,
        float maxHeight) const -> float {
        if (text.empty() || !format) return 0;
        Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
        if (FAILED(writeFactory->CreateTextLayout(
                text.c_str(), static_cast<UINT32>(text.size()), format,
                maxWidth, maxHeight, &layout)) || !layout) {
            return 0;
        }
        DWRITE_TEXT_METRICS metrics{};
        layout->GetMetrics(&metrics);
        return metrics.widthIncludingTrailingWhitespace;
    }

    auto calculatePlayedWidth(
        const Config &state,
        IDWriteTextFormat *format,
        float maxWidth,
        float maxHeight) const -> float {
        if (state.lyric_primary.empty()) return 0;
        if (state.words.empty()) {
            const auto duration = std::max(1.0, state.line_duration_ms);
            const auto progress = std::clamp(
                (state.playback_position_ms - state.line_start_ms) / duration, 0.0, 1.0);
            return measureText(state.lyric_primary, format, maxWidth, maxHeight) * static_cast<float>(progress);
        }

        std::wstring completed;
        float playedWidth = 0;
        for (const auto &word : state.words) {
            const auto wordWidth = measureText(word.text, format, maxWidth, maxHeight);
            if (state.playback_position_ms >= word.start_ms + word.duration_ms) {
                completed += word.text;
                playedWidth = measureText(completed, format, maxWidth, maxHeight);
                continue;
            }
            if (state.playback_position_ms > word.start_ms) {
                const auto progress = std::clamp(
                    (state.playback_position_ms - word.start_ms) / std::max(1.0, word.duration_ms),
                    0.0,
                    1.0);
                playedWidth = measureText(completed, format, maxWidth, maxHeight)
                    + wordWidth * static_cast<float>(progress);
            }
            break;
        }
        return playedWidth;
    }

public:
    Lyrics(ID2D1RenderTarget *target, IDWriteFactory *factory)
        : renderTarget(target), writeFactory(factory) {}

    auto onDraw() -> void {
        if (!renderTarget || !writeFactory) return;
        const auto state = snapshotConfig();
        if (state.lyric_primary.empty() || state.lyric_primary == L" ") return;

        const auto size = renderTarget->GetSize();
        const auto hasSecondary = !state.lyric_secondary.empty() && state.lyric_secondary != L" ";
        const auto primaryFontSize = static_cast<float>(
            hasSecondary ? state.size_primary : state.size_primary_single);
        const auto primaryFormat = createTextFormat(state, false, primaryFontSize);
        if (!primaryFormat) return;

        const auto primaryLayoutHeight = primaryFontSize * 1.45f;
        const auto secondaryLayoutHeight = static_cast<float>(state.size_secondary) * 1.45f;
        const auto totalHeight = hasSecondary
            ? primaryLayoutHeight + static_cast<float>(state.line_spacing) + secondaryLayoutHeight
            : primaryLayoutHeight;
        const auto primaryY = std::max(0.0f, (size.height - totalHeight) / 2.0f);

        Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
        if (FAILED(writeFactory->CreateTextLayout(
                state.lyric_primary.c_str(),
                static_cast<UINT32>(state.lyric_primary.size()),
                primaryFormat.Get(),
                size.width,
                primaryLayoutHeight,
                &layout)) || !layout) {
            return;
        }

        Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> unplayedBrush;
        Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> playedBrush;
        renderTarget->CreateSolidColorBrush(toColor(state.color_primary), &unplayedBrush);
        renderTarget->CreateSolidColorBrush(toColor(state.color_played), &playedBrush);
        if (!unplayedBrush || !playedBrush) return;

        const auto origin = D2D1::Point2F(0, primaryY);
        renderTarget->DrawTextLayout(origin, layout.Get(), unplayedBrush.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);

        const auto playedWidth = std::clamp(
            calculatePlayedWidth(
                state,
                primaryFormat.Get(),
                size.width,
                primaryLayoutHeight),
            0.0f,
            size.width);
        if (playedWidth > 0) {
            const auto textWidth = measureText(
                state.lyric_primary,
                primaryFormat.Get(),
                size.width,
                primaryLayoutHeight);
            auto textStartX = 0.0f;
            if (state.align_primary == DWRITE_TEXT_ALIGNMENT_TRAILING) {
                textStartX = std::max(0.0f, size.width - textWidth);
            } else if (state.align_primary == DWRITE_TEXT_ALIGNMENT_CENTER) {
                textStartX = std::max(0.0f, (size.width - textWidth) / 2.0f);
            }
            renderTarget->PushAxisAlignedClip(
                D2D1::RectF(
                    textStartX,
                    primaryY,
                    std::min(size.width, textStartX + playedWidth),
                    primaryY + primaryLayoutHeight),
                D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
            renderTarget->DrawTextLayout(origin, layout.Get(), playedBrush.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
            renderTarget->PopAxisAlignedClip();
        }

        if (hasSecondary) {
            const auto secondaryFormat = createTextFormat(
                state,
                true,
                static_cast<float>(state.size_secondary));
            if (!secondaryFormat) return;
            Microsoft::WRL::ComPtr<IDWriteTextLayout> secondaryLayout;
            if (FAILED(writeFactory->CreateTextLayout(
                    state.lyric_secondary.c_str(),
                    static_cast<UINT32>(state.lyric_secondary.size()),
                    secondaryFormat.Get(),
                    size.width,
                    secondaryLayoutHeight,
                    &secondaryLayout)) || !secondaryLayout) {
                return;
            }

            Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> secondaryBrush;
            renderTarget->CreateSolidColorBrush(toColor(state.color_secondary), &secondaryBrush);
            if (!secondaryBrush) return;
            const auto secondaryY = primaryY + primaryLayoutHeight + static_cast<float>(state.line_spacing);
            renderTarget->DrawTextLayout(
                D2D1::Point2F(0, secondaryY),
                secondaryLayout.Get(),
                secondaryBrush.Get(),
                D2D1_DRAW_TEXT_OPTIONS_CLIP);
        }
    }
};
