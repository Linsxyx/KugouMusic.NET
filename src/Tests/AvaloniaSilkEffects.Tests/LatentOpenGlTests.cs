using System.Runtime.InteropServices;
using Avalonia;
using KugouAvaloniaPlayer.Controls;
using AvaloniaSilkEffects.Backgrounds;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Tests;

public sealed class MacOpenGlFactAttribute : FactAttribute
{
    public MacOpenGlFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "Requires macOS CGL desktop OpenGL.";
    }
}

public sealed class LatentOpenGlTests
{
    private const string Framework = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    [DllImport(Framework)] private static extern int CGLChoosePixelFormat(int[] attributes,out nint format,out int count);
    [DllImport(Framework)] private static extern int CGLCreateContext(nint format,nint share,out nint context);
    [DllImport(Framework)] private static extern int CGLSetCurrentContext(nint context);
    [DllImport(Framework)] private static extern nint CGLGetCurrentContext();
    [DllImport(Framework)] private static extern int CGLDestroyContext(nint context);
    [DllImport(Framework)] private static extern int CGLDestroyPixelFormat(nint format);

    [MacOpenGlFact]
    public unsafe void FumeBackgroundRespondsToAudioWithFrozenTimeAndCamera()
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
                var size = new PixelSize(480, 320);
                target.EnsureSize(size.Width, size.Height);
                var shapes = FumeBackgroundScene.Build(null, 480, 320);
                foreach (var kind in Enum.GetValues<FumeShapeKind>())
                {
                    var article = new FumeArticleLayout
                    {
                        Width = 480, Height = 320, ViewportHeight = 320,
                        PaperBounds = new(0, 0, 480, 320), Blocks = [], ChronologicalBlocks = [],
                        BlocksBySourceIndex = new Dictionary<int, FumeArticleBlock>()
                    };
                    var shape = shapes.First(s => s.Kind == kind) with { X = 240, Y = 90, Size = 120, Opacity = 0.16 };
                    var scene = new FumeEffectScene();
                    scene.Initialize(device);
                    scene.Resize(size, 1);
                    try
                    {
                        byte[] Capture(double energy)
                        {
                            scene.Publish(new Rect(0, 0, 480, 320), new FumeFrame(
                                article, [shape], 2, -1, 2, 240, 160, 1,
                                new(energy, energy, energy, energy, energy), 1, 1, 0, "Arial", true));
                            device.Render(scene, new EffectFrame(TimeSpan.FromSeconds(2), TimeSpan.Zero, size, 1, 0),
                                (int)target.Framebuffer, EffectColor.Transparent);
                            var pixels = new byte[size.Width * size.Height * 4];
                            fixed (byte* p = pixels)
                                gl.ReadPixels(0, 0, (uint)size.Width, (uint)size.Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                            Assert.Equal(GLEnum.NoError, gl.GetError());
                            return pixels;
                        }
                        var silent = Capture(0);
                        Assert.Contains(silent, value => value != 0);
                        var loud = Capture(1);
                        Assert.False(silent.SequenceEqual(loud), $"{kind} must react to audio without camera or time changes.");
                        Assert.True(silent.SequenceEqual(Capture(0)), $"{kind} must return to its silent state.");
                    }
                    finally { scene.DisposeGpuResources(); }
                }
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

    [MacOpenGlFact]
    public unsafe void SonnetRecreatesEvictedGlyphTextures()
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
                var size = new PixelSize(640, 360);
                target.EnsureSize(size.Width, size.Height);
                var theme = new Sonnet.SonnetTheme(EffectColor.Transparent, EffectColor.White,
                    EffectColor.White, EffectColor.White);
                var program = Sonnet.SonnetProgramCompiler.Compile(
                    [new Sonnet.SonnetLine("歌词恢复", 0, 10, [new("歌词恢复", 0, 10)])], "eviction");
                var scene = new Sonnet.SonnetScene(program, theme, new Sonnet.SonnetTuning
                {
                    ShowOnlyText = true, ShowChromaticSplit = false, EnableTransitions = false,
                });
                scene.Initialize(device);
                scene.Resize(size, 1);
                try
                {
                    var frame = new EffectFrame(TimeSpan.FromSeconds(2), TimeSpan.Zero, size, 1, 0);
                    byte[] Capture()
                    {
                        device.Render(scene, frame, (int)target.Framebuffer, EffectColor.Transparent);
                        var pixels = new byte[size.Width * size.Height * 4];
                        fixed (byte* p = pixels)
                            gl.ReadPixels(0, 0, (uint)size.Width, (uint)size.Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                        Assert.Equal(GLEnum.NoError, gl.GetError());
                        return pixels;
                    }
                    var before = Capture();
                    Assert.Contains(before, value => value != 0);
                    // Simulate a cached paragraph becoming idle while other lyrics play.
                    for (var i = 0; i < 160; i++) device.Textures.BeginFrame();
                    device.Textures.Collect();
                    Assert.Equal(0, device.Textures.Count);
                    var after = Capture();
                    Assert.True(before.SequenceEqual(after), "Revisiting cached lyrics after eviction must render identical pixels.");
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

    [MacOpenGlFact]
    public unsafe void FrozenShadersRenderResizeAndRecreateOnRealGl()
    {
        var previous = CGLGetCurrentContext();
        Assert.Equal(0,CGLChoosePixelFormat([99,0x4100,73,0],out var format,out _));
        nint native = 0;
        try
        {
            Assert.Equal(0,CGLCreateContext(format,0,out native));
            Assert.Equal(0,CGLSetCurrentContext(native));
            var library = NativeLibrary.Load(Framework);
            try
            {
                using var gl = GL.GetApi(name => NativeLibrary.TryGetExport(library,name,out var address) ? address : 0);
                using var device = new EffectDevice(gl);
                using var target = new EffectFramebuffer(gl);
                var scene = new LatentBackgroundScene();
                scene.Initialize(device);
                try
                {
                    for (var iteration=0;iteration<3;iteration++)
                    {
                        var size = new PixelSize(128+iteration*16,72+iteration*8);
                        target.EnsureSize(size.Width,size.Height);
                        scene.Resize(size,1.5);
                        var frame = new EffectFrame(TimeSpan.Zero,TimeSpan.Zero,size,1.5,0);
                        device.Render(scene,frame,(int)target.Framebuffer,EffectColor.Transparent);
                        var pixels = new byte[size.Width*size.Height*4];
                        fixed (byte* p = pixels)
                            gl.ReadPixels(0,0,(uint)size.Width,(uint)size.Height,PixelFormat.Rgba,PixelType.UnsignedByte,p);
                        Assert.Equal(GLEnum.NoError,gl.GetError());
                        Assert.All(Enumerable.Range(0,size.Width*size.Height),i => Assert.Equal((byte)255,pixels[i*4+3]));
                        Assert.True(pixels.Where((_,i)=>i%4!=3).Distinct().Count()>8,"Material must contain spatial/color variation.");
                        var first = pixels.ToArray();
                        scene.Palette = LatentPalette.Midnight with
                        {
                            Cover = new EffectColor[] { new(1,0,0),new(1,.4f,0),new(.7f,.1f,0),new(.9f,.3f,.1f) }
                        };
                        device.Render(scene,frame,(int)target.Framebuffer,EffectColor.Transparent);
                        fixed (byte* p = pixels)
                            gl.ReadPixels(0,0,(uint)size.Width,(uint)size.Height,PixelFormat.Rgba,PixelType.UnsignedByte,p);
                        Assert.False(first.SequenceEqual(pixels),"Cover palette must change the rendered material.");
                        Assert.Equal(3,device.FrameMetrics.DrawCalls);
                        scene.Palette = LatentPalette.Midnight;
                        scene.DisposeGpuResources();
                        scene.Initialize(device);
                    }
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
}
