using System.Numerics;
using AvaloniaSilkEffects.Sonnet;
using System.Runtime.InteropServices;
using Avalonia;
using AvaloniaSilkEffects.Backgrounds;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Tests;

public sealed class SonnetDecorationOpenGlTests
{
    private const string Framework = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    [DllImport(Framework)] private static extern int CGLChoosePixelFormat(int[] attributes,out nint format,out int count);
    [DllImport(Framework)] private static extern int CGLCreateContext(nint format,nint share,out nint context);
    [DllImport(Framework)] private static extern int CGLSetCurrentContext(nint context);
    [DllImport(Framework)] private static extern nint CGLGetCurrentContext();
    [DllImport(Framework)] private static extern int CGLDestroyContext(nint context);
    [DllImport(Framework)] private static extern int CGLDestroyPixelFormat(nint format);

    [MacOpenGlFact]
    public unsafe void DecorationsRenderAndSeekOnRealGl()
    {
        var previous = CGLGetCurrentContext();
        Assert.Equal(0, CGLChoosePixelFormat([99, 0x4100, 73, 0], out var format, out _));
        nint native = 0;
        try
        {
            Assert.Equal(0, CGLCreateContext(format, 0, out native));
            Assert.Equal(0, CGLSetCurrentContext(native));
            var library = NativeLibrary.Load(Framework);
            try
            {
                using var gl = GL.GetApi(name => NativeLibrary.TryGetExport(library, name, out var address) ? address : 0);
                using var device = new EffectDevice(gl);
                using var target = new EffectFramebuffer(gl);
                var size = new PixelSize(1280, 720);
                target.EnsureSize(size.Width, size.Height);
                var scene = new DecorationScene();
                scene.Initialize(device);
                scene.Resize(size, 1);
                try
                {
                    var frame = new EffectFrame(TimeSpan.FromSeconds(0), TimeSpan.Zero, size, 1, 0);
                    byte[] Capture()
                    {
                        device.Render(scene, frame, (int)target.Framebuffer, new EffectColor(.03f, .04f, .07f));
                        var pixels = new byte[size.Width * size.Height * 4];
                        fixed (byte* p = pixels)
                            gl.ReadPixels(0, 0, (uint)size.Width, (uint)size.Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                        Assert.Equal(GLEnum.NoError, gl.GetError());
                        return pixels;
                    }
                    var before = Capture();
                    frame = frame with { Elapsed = TimeSpan.FromSeconds(1) };
                    var after = Capture();
                    Assert.False(before.SequenceEqual(after));
                    var directory = Environment.GetEnvironmentVariable("SONNET_DECOR_CAPTURE_DIR");
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                        void Save(byte[] pixels, string name)
                        {
                            using var bitmap = new SkiaSharp.SKBitmap(size.Width, size.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
                            for (var y = 0; y < size.Height; y++)
                                Marshal.Copy(pixels, (size.Height - 1 - y) * size.Width * 4, bitmap.GetPixels() + y * bitmap.RowBytes, size.Width * 4);
                            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                            using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                            using var output = File.Create(Path.Combine(directory, name));
                            encoded.SaveTo(output);
                        }
                        Save(after, "sonnet-frames-opengl.png");
                        for (var i = 0; i < 36; i++)
                        {
                            frame = frame with { Elapsed = TimeSpan.FromSeconds(i / 30d) };
                            Save(Capture(), $"sonnet-grow-{i:D2}.png");
                        }
                    }
                    frame = frame with { Elapsed = TimeSpan.Zero };
                    Assert.True(before.SequenceEqual(Capture()), "Seeking back must restore exactly the same frame.");
                }
                finally { scene.DisposeGpuResources(); }
            }
            finally { NativeLibrary.Free(library); }
        }
        finally
        {
            CGLSetCurrentContext(previous);
            if (native != 0) CGLDestroyContext(native);
            CGLDestroyPixelFormat(format);
        }
    }

    private sealed class DecorationScene : EffectScene
    {
        private readonly EffectContainer _root = new();
        private readonly List<SonnetFrameDecorView> _frames = [];
        public DecorationScene()
        {
            var theme = new SonnetTheme(new(0.03f, 0.04f, 0.07f), new(.9f, .91f, .95f), new(.55f,.59f,.72f), new(.42f,.45f,.57f));
            string[] labels = ["CROP MARKS", "FOUR PETALS", "BRACKETS / DIAMONDS", "DASHES / TRIANGLES"];
            for (var i = 0; i < 4; i++)
            {
                var center = new Vector2(320 + i % 2 * 640, 180 + i / 2 * 360);
                var placement = SonnetDecorationTests.Placement() with { X = center.X, Y = center.Y, MeasuredWidth = 300, MeasuredHeight = 72 };
                var decor = new SonnetFrameDecorView(placement, 64, theme, i, 0, 0, 5);
                _frames.Add(decor);
                _root.Add(decor.Root);
                _root.Add(new TextNode { Text = "沿着光的轨迹", FontFamily = "PingFang SC", FontSize = 46,
                    Color = theme.Primary, Position = center, Anchor = new Vector2(.5f) });
                _root.Add(new TextNode { Text = labels[i], FontFamily = "Menlo", FontSize = 13,
                    Color = theme.Accent, Position = center + new Vector2(0, 110), Anchor = new Vector2(.5f) });
            }
        }
        public override void Update(in EffectFrame frame)
        {
            foreach (var decor in _frames) decor.Update(frame.Elapsed.TotalSeconds);
        }
        public override void Render(EffectRenderContext context) => context.Render(_root);
    }
}
