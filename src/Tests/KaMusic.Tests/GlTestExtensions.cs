using AvaloniaSilkEffects.Gpu.OpenGl;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Tests;

/// <summary>Raw GL access for tests that run the OpenGL backend and inspect its objects.</summary>
internal static class GlTestExtensions
{
    extension(EffectDevice device)
    {
        public GL Gl => ((GlGpuDevice)device.Gpu).Gl;
    }

    extension(EffectTexture texture)
    {
        public uint Handle => ((GlTexture)texture.Gpu).Handle;
    }
}
