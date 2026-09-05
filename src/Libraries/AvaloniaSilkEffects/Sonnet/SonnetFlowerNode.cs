using System.Numerics;
using SkiaSharp;

namespace AvaloniaSilkEffects.Sonnet;

// Lucide React 1.31.0 Flower. ISC License.
// Copyright (c) 2026 Lucide Icons and Contributors
// Permission to use, copy, modify, and/or distribute this software for any
// purpose with or without fee is hereby granted, provided that the above
// copyright notice and this permission notice appear in all copies.
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
// WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
// ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
// WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
// ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
// OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
internal sealed class SonnetFlowerNode : EffectNode
{
    internal static readonly string[] Paths =
    [
        "M12 16.5A4.5 4.5 0 1 1 7.5 12 4.5 4.5 0 1 1 12 7.5a4.5 4.5 0 1 1 4.5 4.5 4.5 4.5 0 1 1-4.5 4.5",
        "M12 7.5V9", "M7.5 12H9", "M16.5 12H15", "M12 16.5V15",
        "m8 8 1.88 1.88", "M14.12 9.88 16 8", "m8 16 1.88-1.88", "M14.12 14.12 16 16",
    ];
    private readonly EffectColor _color;
    private readonly string _key;

    internal SonnetFlowerNode(float particleSize, EffectColor color)
    {
        _color = color;
        _key = $"lucide/1.31.0/Flower/192/3.5/{color}";
        Position = new Vector2(-particleSize * 3.5f);
        Scale = new Vector2(particleSize * 7 / 192);
    }

    public override void Render(EffectRenderContext context)
    {
        if (!IsVisible || WorldAlpha <= 0) return;
        var texture = context.Device.Textures.GetOrCreateVector(_key, new Vector2(192), 1.5f, Draw);
        context.Primitives.DrawTexture(texture, WorldTransform, new Vector2(192), WorldAlpha, BlendMode);
    }

    private void Draw(SKCanvas canvas)
    {
        canvas.Scale(8);
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3.5f / 8,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Color = new SKColor((byte)Math.Clamp(_color.R * 255, 0, 255),
                (byte)Math.Clamp(_color.G * 255, 0, 255), (byte)Math.Clamp(_color.B * 255, 0, 255),
                (byte)Math.Clamp(_color.A * 255, 0, 255)),
        };
        canvas.DrawCircle(12, 12, 3, paint);
        foreach (var data in Paths)
        {
            using var path = SKPath.ParseSvgPathData(data);
            canvas.DrawPath(path, paint);
        }
    }
}
