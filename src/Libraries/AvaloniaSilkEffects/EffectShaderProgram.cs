using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects;

internal sealed class EffectShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniforms = [];

    public EffectShaderProgram(GL gl, string vertexSource, string fragmentSource, string name)
    {
        _gl = gl;
        var vertex = Compile(ShaderType.VertexShader, vertexSource, name);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource, name);
        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, vertex);
        gl.AttachShader(Handle, fragment);
        gl.LinkProgram(Handle);
        gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out var linked);
        var log = gl.GetProgramInfoLog(Handle);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        if (linked == 0)
        {
            gl.DeleteProgram(Handle);
            throw new InvalidOperationException($"OpenGL program '{name}' failed to link: {log}");
        }
    }

    public uint Handle { get; }

    public void Use() => _gl.UseProgram(Handle);

    public int Uniform(string name)
    {
        if (_uniforms.TryGetValue(name, out var location))
            return location;
        location = _gl.GetUniformLocation(Handle, name);
        _uniforms.Add(name, location);
        return location;
    }

    private uint Compile(ShaderType type, string source, string name)
    {
        if (_gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase))
            source = source.Replace("#version 330 core", "#version 300 es\nprecision highp float;");
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        var log = _gl.GetShaderInfoLog(shader);
        if (compiled != 0)
            return shader;
        _gl.DeleteShader(shader);
        throw new InvalidOperationException($"OpenGL shader '{name}' failed to compile: {log}");
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}
