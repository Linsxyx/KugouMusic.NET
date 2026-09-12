using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Avalonia;
using AvaloniaSilkEffects;
using SkiaSharp;

namespace KugouAvaloniaPlayer.Controls;

// Fume rendering adapter. Layout/timing data is published on the UI thread;
// texture creation, reuse and release happen only in the Silk GL callbacks.
internal sealed class FumeEffectScene : EffectScene
{
    private sealed record Presentation(Rect Bounds, FumeFrame Frame);
    private Presentation? _pending;
    private FumeArticleLayout? _article;
    private readonly Dictionary<FumeRenderLine, RowTexture> _rows = new();
    private readonly List<PolylineNode> _geometry = new();
    private readonly Dictionary<int, EffectTexture> _sparkGlows = new();
    private double _scaling = 1;
    private float _rasterScale = 2;

    public void Publish(Rect bounds, FumeFrame frame) =>
        Volatile.Write(ref _pending, new Presentation(bounds, frame));
    public void Clear() => Volatile.Write(ref _pending, null);

    public override void Initialize(EffectDevice device)
    {
        // The old context may have been lost without a deinit callback.
        _rows.Clear();
        _sparkGlows.Clear();
        _geometry.Clear();
        _article = null;
        base.Initialize(device);
    }

    public override void Resize(PixelSize size, double renderScaling)
    {
        _scaling = renderScaling;
        var rasterScale = (float)Math.Clamp(renderScaling * 1.5, 1, 4);
        if (_rasterScale != rasterScale)
        {
            DisposeGpuResources();
            _rasterScale = rasterScale;
        }
    }

    public override void Render(EffectRenderContext context)
    {
        var presentation = Volatile.Read(ref _pending);
        if (presentation == null)
        {
            DisposeGpuResources();
            return;
        }
        var frame = presentation.Frame;
        if (!ReferenceEquals(_article, frame.Article))
        {
            DisposeGpuResources();
            _article = frame.Article;
            foreach (var shape in frame.BackgroundShapes)
                _geometry.Add(BuildGeometry(shape));
        }
        new FumeGpuRenderer(presentation.Bounds, frame, this, context).Draw();
    }

    public override void DisposeGpuResources()
    {
        ReleaseRows();
        foreach (var texture in _sparkGlows.Values)
            Device.Textures.Release(texture);
        _sparkGlows.Clear();
        _geometry.Clear();
        _article = null;
    }

    private void ReleaseRows()
    {
        foreach (var row in _rows.Values)
        {
            if (row.Sharp != null) Device.Textures.Release(row.Sharp);
            if (row.Glow != null) Device.Textures.Release(row.Glow);
        }
        _rows.Clear();
    }

    private sealed class RowTexture
    {
        public EffectTexture? Sharp;
        public EffectTexture? Glow;
        public float Padding;
        public float Sigma;
    }

    private RowTexture GetRow(FumeArticleBlock block, FumeRenderLine line, bool glow)
    {
        if (!_rows.TryGetValue(line, out var row))
            _rows.Add(line, row = new RowTexture());
        var texture = glow ? row.Glow : row.Sharp;
        if (texture != null && !texture.IsDisposed)
        {
            Device.Textures.Touch(texture);
            return row;
        }
        // Fixed masks: time, color and opacity never enter the texture cache key.
        row.Sigma = (float)(3 + block.FontSize * 0.12);
        row.Padding = MathF.Ceiling(row.Sigma * 3 + (float)block.FontSize * 0.2f);
        var padding = row.Padding;
        var size = new Vector2((float)line.Width + 2 * padding,
            (float)block.LineHeight + 2 * padding);
        var key = FormattableString.Invariant(
            $"fume-row:{block.TypefaceFamily.Length}:{block.TypefaceFamily}:{block.IsHero}:{block.FontSize:R}:{block.LineHeight:R}:{glow}:{line.Text}");
        texture = Device.Textures.GetOrCreateVector(key, size, _rasterScale, canvas =>
        {
            using var typeface = SKTypeface.FromFamilyName(block.TypefaceFamily,
                block.IsHero ? SKFontStyleWeight.SemiBold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            using var font = new SKFont(typeface, (float)block.FontSize);
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
            using var blur = glow ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, row.Sigma) : null;
            paint.MaskFilter = blur;
            canvas.DrawText(line.Text, padding, padding + (float)(block.LineHeight * 0.78), font, paint);
        });
        if (glow) row.Glow = texture;
        else row.Sharp = texture;
        return row;
    }

    internal static PolylineNode BuildGeometry(FumeBackgroundShape shape)
    {
        var h = (float)(shape.Size * 0.5);
        Vector2[] points;
        if (shape.Kind == FumeShapeKind.Ring)
        {
            points = new Vector2[129];
            for (var i = 0; i < points.Length; i++)
            {
                var gap = Math.Clamp(shape.RingGapSize, 0.18, Math.PI * 0.6);
                var angle = (float)(shape.RingGapStart + gap + (Math.PI * 2 - gap) * i / (points.Length - 1));
                points[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * h;
            }
        }
        else if (shape.Kind == FumeShapeKind.Square)
            points = [new(-h,-h), new(h,-h), new(h,h), new(-h,h), new(-h,-h)];
        else if (shape.Kind == FumeShapeKind.Spark)
        {
            var a = h * 0.26f;
            points = [new(0,-h),new(a,-a),new(h,0),new(a,a),new(0,h),new(-a,a),new(-h,0),new(-a,-a),new(0,-h)];
        }
        else
        {
            var a = h * 0.6f;
            points = [new(-a,-h),new(a,-h),new(a,-a),new(h,-a),new(h,a),new(a,a),new(a,h),
                new(-a,h),new(-a,a),new(-h,a),new(-h,-a),new(-a,-a),new(-a,-h)];
        }
        if (shape.Kind == FumeShapeKind.Spark)
            return new PolylineNode { Points = points, TailAlpha = 1, HeadAlpha = 1 };

        // Subdivide straight edges too, so the four spatial gradient stops survive rotation.
        var gradientPoints = new List<Vector2>();
        for (var i = 0; i < points.Length - 1; i++)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(points[i], points[i + 1]) / (shape.Size / 48)));
            for (var step = 0; step < steps; step++)
                gradientPoints.Add(Vector2.Lerp(points[i], points[i + 1], (float)step / steps));
        }
        gradientPoints.Add(points[^1]);
        var colors = new EffectColor[gradientPoints.Count];
        for (var i = 0; i < colors.Length; i++)
            colors[i] = GeometryColor(gradientPoints[i] / (float)shape.Size);
        return new PolylineNode { Points = gradientPoints, PointColors = colors, TailAlpha = 1, HeadAlpha = 1 };
    }

    private static EffectColor GeometryColor(Vector2 point)
    {
        var t = Math.Clamp(Vector2.Dot(point + new Vector2(0.55f, 0.28f), new Vector2(1.1f, 0.56f)) / 1.5236f, 0, 1);
        float colorMix, alpha;
        if (t < 0.28f)
        {
            var progress = t / 0.28f;
            colorMix = 0.24f * progress;
            alpha = 0.18f + (0.58f - 0.18f) * progress;
        }
        else if (t < 0.54f)
        {
            var progress = (t - 0.28f) / 0.26f;
            colorMix = 0.24f + (0.62f - 0.24f) * progress;
            alpha = 0.58f + (0.92f - 0.58f) * progress;
        }
        else
        {
            var progress = (t - 0.54f) / 0.46f;
            colorMix = 0.62f + (1 - 0.62f) * progress;
            alpha = 0.92f + (0.7f - 0.92f) * progress;
        }
        return new EffectColor((98 + (214 - 98) * colorMix) / 255,
            (126 + (169 - 126) * colorMix) / 255, (145 + (31 - 145) * colorMix) / 255, alpha);
    }

    private sealed class FumeGpuRenderer(
        Rect bounds, FumeFrame frame, FumeEffectScene scene, EffectRenderContext context)
    {
        private static readonly EffectColor Primary = new(242/255f,235/255f,221/255f);
        private static readonly EffectColor Accent = new(214/255f,169/255f,31/255f);
        private static readonly EffectColor Secondary = new(98/255f,126/255f,145/255f);
        private Matrix3x2 Camera(double x, double y, double scale) =>
            Matrix3x2.CreateTranslation((float)-x, (float)-y) *
            Matrix3x2.CreateScale((float)scale) *
            Matrix3x2.CreateTranslation((float)(bounds.Width*0.5), (float)(bounds.Height*0.5)) *
            Matrix3x2.CreateScale((float)scene._scaling);

        public void Draw()
        {
            DrawBackground();
            var camera = Camera(frame.CameraX, frame.CameraY, frame.CameraScale);
            foreach (var block in frame.Article.Blocks)
            {
                var left = bounds.Width*0.5 + (block.X-frame.CameraX)*frame.CameraScale;
                var top = bounds.Height*0.5 + (block.Y-frame.CameraY)*frame.CameraScale;
                if (left+block.Width*frame.CameraScale < -180 || left > bounds.Width+180 ||
                    top+block.Height*frame.CameraScale < -180 || top > bounds.Height+180) continue;
                DrawBlock(block, camera);
            }
        }

        private void DrawBackground()
        {
            var cx = Mix(frame.Article.Width*0.5, frame.CameraX, 0.9);
            var cy = Mix(frame.Article.Height*0.5, frame.CameraY, 0.74) -
                Math.Clamp(bounds.Height*0.22/Math.Max(frame.CameraScale,0.001),48,180);
            var scale = Math.Clamp(frame.CameraScale*0.94,0.22,2.24);
            for (var i=0; i<scene._geometry.Count; i++)
            {
                var shape = frame.BackgroundShapes[i];
                var node = scene._geometry[i];
                var band = Math.Clamp(frame.Energy.At(shape.AudioBand),0,1);
                var audioScale = shape.AudioBand < 0 ? 1 : Mix(0.95,1.45,band);
                var audioOpacity = shape.AudioBand < 0 ? 1 : Mix(0.85,1.55,band);
                var response = Mix(0.58,1.16,shape.Depth);
                var vibration = shape.AudioBand < 0 ? 0 : band * (shape.Kind == FumeShapeKind.Spark ? 10 : 5);
                var phase = shape.Rotation * 1.7 + shape.Depth * 12.0;
                var vibrationX = Math.Sin(frame.PlaybackSeconds * (2.4 + shape.Depth * 1.8) + phase) * vibration;
                var vibrationY = Math.Cos(frame.PlaybackSeconds * (2.0 + shape.Depth * 1.4) + phase * 1.31) * vibration * 0.72;
                var x = shape.X + (cx-frame.Article.Width*0.5)*(1-response)*0.72 + vibrationX;
                var y = shape.Y + (cy-frame.Article.Height*0.5)*(1-response)*0.72 + vibrationY;
                node.Position = new Vector2(
                    (float)((bounds.Width*0.5+(x-cx)*scale)*scene._scaling),
                    (float)((bounds.Height*0.5+(y-cy)*scale)*scene._scaling));
                node.Scale = new Vector2((float)(audioScale*scale*scene._scaling));
                node.Rotation = (float)(shape.Rotation+frame.PlaybackSeconds*shape.RotationSpeed);
                node.TailWidth = node.HeadWidth = (float)((shape.Kind == FumeShapeKind.Spark ? shape.StrokeWidth * 1.15 : shape.StrokeWidth) * scale * scene._scaling);
                node.Color = shape.IsAccent ? Accent : Secondary;
                var opacityBoost = shape.Kind == FumeShapeKind.Spark ? 2.35 : 2.15;
                node.Alpha = (float)Math.Clamp(shape.Opacity*audioOpacity*frame.BackgroundObjectOpacity*opacityBoost,0,0.56);
                if (node.Alpha <= 0) continue;
                var extent = shape.Size * audioScale * scale * scene._scaling;
                if (node.Position.X + extent < 0 || node.Position.Y + extent < 0 ||
                    node.Position.X - extent > bounds.Width * scene._scaling ||
                    node.Position.Y - extent > bounds.Height * scene._scaling) continue;
                if (shape.Kind == FumeShapeKind.Spark && node.Alpha > 0)
                {
                    const float padding = 18;
                    if (!scene._sparkGlows.TryGetValue(i, out var glow) || glow.IsDisposed)
                    {
                        var size = (float)shape.Size;
                        var key = FormattableString.Invariant($"fume-spark-v2:{size:R}:{shape.StrokeWidth:R}");
                        glow = scene.Device.Textures.GetOrCreateVector(key,
                            new Vector2(size+padding*2), scene._rasterScale, canvas =>
                            {
                                using var path = new SKPath();
                                var points = node.Points;
                                path.MoveTo(points[0].X, points[0].Y);
                                for (var p=1; p<points.Count; p++) path.LineTo(points[p].X,points[p].Y);
                                path.Close();
                                using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal,5f);
                                using var paint = new SKPaint
                                {
                                    IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Stroke,
                                    StrokeWidth = (float)shape.StrokeWidth, StrokeCap = SKStrokeCap.Round, MaskFilter = blur
                                };
                                canvas.Translate(size*0.5f+padding,size*0.5f+padding);
                                canvas.DrawPath(path,paint);
                            });
                        scene._sparkGlows[i] = glow;
                    }
                    scene.Device.Textures.Touch(glow);
                    var transform = Matrix3x2.CreateTranslation(-glow.LogicalSize*0.5f)*node.WorldTransform;
                    context.Primitives.DrawTexture(glow,transform,glow.LogicalSize,node.Alpha * 0.75f,
                        EffectBlendMode.Alpha,node.Color);
                    node.Render(context);
                    continue;
                }
                var alpha = node.Alpha;
                node.Alpha = alpha * 0.56f;
                node.TailWidth = node.HeadWidth = (float)(Math.Max(shape.StrokeWidth * 0.28, 0.14) * scale * scene._scaling);
                node.Render(context);
                node.Alpha = alpha;
                node.TailWidth = node.HeadWidth = (float)(Math.Max(shape.StrokeWidth * 0.92, 0.78) * scale * scene._scaling);
                node.Render(context);
            }
        }

        private void DrawBlock(FumeArticleBlock block, Matrix3x2 camera)
        {
            var start = block.Line.Start.TotalSeconds;
            var end = start+Math.Max(block.Line.Duration.TotalSeconds,0.12);
            var trailDuration = Math.Clamp(Math.Max(end-start,0.18)*(block.IsHero?0.42:0.52),0.45,1.45);
            var waiting = block.IsHero?0.06:0.035;
            var active = block.IsHero?0.985:0.92;
            var passed = block.IsHero?0.74:0.58;
            var holding = block.SourceLineIndex == frame.CurrentLineIndex && frame.PlaybackSeconds >= end;
            var isPassed = !holding && frame.PlaybackSeconds >= end+trailDuration;
            if (isPassed && frame.TextHoldRatio < 1 && !frame.IsOverview)
            {
                var duration = Math.Clamp((frame.Article.LastEndSeconds-frame.Article.FirstStartSeconds)*frame.TextHoldRatio,2.4,130);
                passed = Mix(passed,block.IsHero?0.11:0.075,EaseInCubic((frame.PlaybackSeconds-end-trailDuration)/duration));
            }
            var printed = FumePlayback.ResolvePrintedProgress(block, frame.PlaybackSeconds);
            var timed = FumePlayback.HasTimedWordRanges(block);
            for (var lineIndex=0; lineIndex<block.RenderLines.Count; lineIndex++)
            {
                var line = block.RenderLines[lineIndex];
                var row = scene.GetRow(block,line,false);
                var transform = Matrix3x2.CreateTranslation((float)block.X-row.Padding,
                    (float)(block.Y+lineIndex*block.LineHeight)-row.Padding)*camera;
                var alpha = frame.PlaybackSeconds < start ? waiting : isPassed ? passed : waiting;

                // Separate local whole-line glow; never blur the entire scene.
                if (frame.GlowIntensity > 0 && frame.PlaybackSeconds >= start && !isPassed)
                {
                    row = scene.GetRow(block,line,true);
                    var progress = Math.Clamp((frame.PlaybackSeconds-start)/Math.Max(end-start,0.18),0,1);
                    var envelope = progress <= 0.8 ? EaseOutCubic(progress/0.8) : 1-EaseInCubic((progress-0.8)/0.2);
                    var glowAlpha = ((block.IsHero?0.16:0.12)+envelope*(block.IsHero?0.26:0.2))*frame.GlowIntensity;
                    context.Primitives.DrawTexture(row.Glow!,transform,row.Glow!.LogicalSize,
                        (float)glowAlpha,EffectBlendMode.Alpha,Accent);
                }
                context.Primitives.DrawTexture(row.Sharp!,transform,row.Sharp!.LogicalSize,
                    (float)alpha,EffectBlendMode.Alpha,Primary);
                if (frame.PlaybackSeconds < start || isPassed) continue;
                for (var glyph=line.Start; glyph<line.End; glyph++)
                {
                    var range = timed && glyph<block.WordRangeByGlyph.Count ? block.WordRangeByGlyph[glyph] : -1;
                    var fraction = FumePlayback.ResolvePlayedFraction(block,glyph,range,printed,frame.PlaybackSeconds);
                    if (fraction<=0) continue;
                    FumePlayback.ResolveGlyphTiming(block,glyph,range,out var glyphStart,out var glyphEnd);
                    var trailStart = glyphStart+Math.Max(glyphEnd-glyphStart,0.001)*0.18;
                    var trail = Math.Pow(Math.Clamp((frame.PlaybackSeconds-trailStart)/trailDuration,0,1),1.35);
                    var t = (float)(0.18+trail*0.82);
                    var color = new EffectColor(
                        Accent.R+(Primary.R-Accent.R)*t, Accent.G+(Primary.G-Accent.G)*t,
                        Accent.B+(Primary.B-Accent.B)*t,(float)active);
                    var x = (float)(block.GlyphOffsets[glyph]-block.GlyphOffsets[line.Start])+row.Padding;
                    var width = (float)(block.GlyphOffsets[glyph+1]-block.GlyphOffsets[glyph]);
                    context.Primitives.DrawTextureSlice(row.Sharp!,transform,x,x+width*(float)fraction,color);
                }
            }
        }

        private static double Mix(double from,double to,double t) => from+(to-from)*t;
        private static double EaseOutCubic(double t) => 1-Math.Pow(1-Math.Clamp(t,0,1),3);
        private static double EaseInCubic(double t) => Math.Pow(Math.Clamp(t,0,1),3);
    }
}
