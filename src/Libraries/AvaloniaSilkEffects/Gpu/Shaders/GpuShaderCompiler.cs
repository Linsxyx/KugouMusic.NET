using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.SPIRV;
using Silk.NET.SPIRV.Cross;
using CrossCompiler = Silk.NET.SPIRV.Cross.Compiler;
using ShadercCompiler = Silk.NET.Shaderc.Compiler;

namespace AvaloniaSilkEffects.Gpu.Shaders;

internal enum GpuShaderStage
{
    Vertex,
    Fragment,
}

internal enum GpuUniformScalar
{
    Float,
    Int,
}

/// <summary>A member of a stage's std140 uniform block.</summary>
internal sealed record GpuUniformMember(
    string Name, GpuUniformScalar Scalar, int Offset, int VectorSize, int Columns, int ArraySize, int ArrayStride,
    int MatrixStride);

internal sealed record GpuUniformBlock(int Size, IReadOnlyList<GpuUniformMember> Members);

/// <summary>One compiled stage: SPIR-V for Vulkan and, on request, MSL for Metal.</summary>
internal sealed record GpuCompiledStage(
    GpuShaderStage Stage, byte[] Spirv, string? Msl, string MslEntryPoint, GpuUniformBlock? Uniforms);

/// <summary>
/// A program cross-compiled from the effects' GLSL 330 sources. Uniform blocks bind at
/// <see cref="VertexUniformBinding"/> / <see cref="FragmentUniformBinding"/> (set 0); samplers follow, in
/// <see cref="Samplers"/> order from <see cref="FirstSamplerBinding"/>. In MSL each stage's uniforms are
/// buffer 0, vertex data is buffer <see cref="MslVertexBufferIndex"/>, and sampler <c>i</c> is texture / sampler <c>i</c>.
/// </summary>
internal sealed record GpuCompiledProgram(
    string Name, GpuCompiledStage Vertex, GpuCompiledStage Fragment, IReadOnlyList<string> Samplers)
{
    public const int VertexUniformBinding = 0;
    public const int FragmentUniformBinding = 1;
    public const int FirstSamplerBinding = 2;
    public const int MslUniformBufferIndex = 0;
    public const int MslVertexBufferIndex = 16;
}

internal enum GpuShaderTarget
{
    Spirv,
    Msl,
}

/// <summary>
/// GLSL 330 → (shaderc, Vulkan relaxed rules for loose uniforms) SPIR-V → (SPIRV-Cross) reflection and MSL.
/// Varying locations are assigned by name, so a vertex shader can declare outputs the fragment shader ignores.
/// Results are cached per source pair for the process lifetime.
/// </summary>
internal static unsafe partial class GpuShaderCompiler
{
    private static readonly ConcurrentDictionary<(string, string, GpuShaderTarget), GpuCompiledProgram> Cache = new();
    private static readonly Lazy<Shaderc> ShadercApi = new(Shaderc.GetApi);
    // Held for the process lifetime: releasing the last shaderc compiler finalizes glslang, which drops its built-in
    // symbol tables, so a compiler per call rebuilds them every time (~30 ms per stage instead of ~0.2 ms).
    private static readonly Lazy<nint> ShadercInstance = new(() => (nint)ShadercApi.Value.CompilerInitialize());
    private static readonly Lazy<Cross> CrossApi = new(Cross.GetApi);
    private static readonly object Gate = new();

    [GeneratedRegex(@"^\s*(?:(?:flat|smooth|noperspective)\s+)?out\s+(\w+)\s+(\w+)\s*(\[[^\]]*\])?\s*;", RegexOptions.Multiline)]
    private static partial Regex VertexOutput();

    [GeneratedRegex(@"^(\s*)((?:(?:flat|smooth|noperspective)\s+)?in\s+(\w+)\s+(\w+)\s*(\[[^\]]*\])?\s*;)", RegexOptions.Multiline)]
    private static partial Regex FragmentInput();

    [GeneratedRegex(@"^(\s*)(out\s+vec4\s+\w+\s*;)", RegexOptions.Multiline)]
    private static partial Regex FragmentOutput();

    [GeneratedRegex(@"^\s*uniform\s+sampler2D\s+(\w+)\s*;", RegexOptions.Multiline)]
    private static partial Regex SamplerDeclaration();

    public static GpuCompiledProgram Compile(string vertexSource, string fragmentSource, string name, GpuShaderTarget target) =>
        Cache.GetOrAdd((vertexSource, fragmentSource, target), key => CompileCore(key.Item1, key.Item2, name, key.Item3));

    private static GpuCompiledProgram CompileCore(string vertexSource, string fragmentSource, string name, GpuShaderTarget target)
    {
        var (vertex, fragment) = Preprocess(vertexSource, fragmentSource);
        var samplers = SamplerDeclaration().Matches(vertex).Concat(SamplerDeclaration().Matches(fragment))
            .Select(match => match.Groups[1].Value).Distinct().ToArray();
        lock (Gate)
        {
            var vertexStage = CompileStage(vertex, GpuShaderStage.Vertex, name, samplers, target);
            var fragmentStage = CompileStage(fragment, GpuShaderStage.Fragment, name, samplers, target);
            return new GpuCompiledProgram(name, vertexStage, fragmentStage, samplers);
        }
    }

    internal static (string Vertex, string Fragment) Preprocess(string vertexSource, string fragmentSource)
    {
        var locations = new Dictionary<string, int>();
        foreach (Match match in VertexOutput().Matches(vertexSource))
            locations.TryAdd(match.Groups[2].Value, locations.Count);
        var vertex = VertexOutput().Replace(Version(vertexSource), match =>
            $"layout(location = {locations[match.Groups[2].Value]}) {match.Value.TrimStart()}");
        var next = locations.Count;
        var fragment = FragmentInput().Replace(Version(fragmentSource), match =>
        {
            var location = locations.TryGetValue(match.Groups[4].Value, out var known) ? known : next++;
            return $"{match.Groups[1].Value}layout(location = {location}) {match.Groups[2].Value}";
        });
        fragment = FragmentOutput().Replace(fragment, "$1layout(location = 0) $2");
        return (vertex, fragment);
    }

    private static string Version(string source)
    {
        var index = source.IndexOf("#version 330 core", StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException("Effect shaders start with '#version 330 core'.");
        return string.Concat(source.AsSpan(0, index), "#version 450", source.AsSpan(index + "#version 330 core".Length));
    }

    private static GpuCompiledStage CompileStage(string source, GpuShaderStage stage, string name, string[] samplers,
        GpuShaderTarget target)
    {
        var words = ToSpirv(source, stage, name);
        var cross = CrossApi.Value;
        Context* context = null;
        Check(cross.ContextCreate(&context), null, "context");
        try
        {
            var (block, blockId, samplerIds) = Reflect(context, words, name);
            // Bind the default uniform block and samplers where the program layout wants them.
            if (blockId != 0)
            {
                Patch(words, blockId, Decoration.DescriptorSet, 0);
                Patch(words, blockId, Decoration.Binding,
                    stage == GpuShaderStage.Vertex ? GpuCompiledProgram.VertexUniformBinding : GpuCompiledProgram.FragmentUniformBinding);
            }
            foreach (var (samplerName, id) in samplerIds)
            {
                Patch(words, id, Decoration.DescriptorSet, 0);
                Patch(words, id, Decoration.Binding, GpuCompiledProgram.FirstSamplerBinding + Array.IndexOf(samplers, samplerName));
            }
            string? msl = null;
            var entry = "main";
            if (target == GpuShaderTarget.Msl)
                (msl, entry) = ToMsl(context, words, stage, name, samplers);
            return new GpuCompiledStage(stage, MemoryMarshal.AsBytes(words.AsSpan()).ToArray(), msl, entry, block);
        }
        finally
        {
            cross.ContextDestroy(context);
        }
    }

    private static uint[] ToSpirv(string source, GpuShaderStage stage, string name)
    {
        var shaderc = ShadercApi.Value;
        var compiler = (ShadercCompiler*)ShadercInstance.Value;
        var options = shaderc.CompileOptionsInitialize();
        try
        {
            shaderc.CompileOptionsSetSourceLanguage(options, Silk.NET.Shaderc.SourceLanguage.Glsl);
            shaderc.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan10);
            shaderc.CompileOptionsSetTargetSpirv(options, SpirvVersion.Shaderc10);
            // Loose uniforms (gl_DefaultUniformBlock) and gl_VertexID, so the GL sources compile unchanged.
            shaderc.CompileOptionsSetVulkanRulesRelaxed(options, true);
            shaderc.CompileOptionsSetAutoBindUniforms(options, true);
            shaderc.CompileOptionsSetAutoMapLocations(options, true);
            // Unoptimized: the optimizer strips the names reflection binds uniforms by; drivers optimize anyway.
            shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Zero);
            var kind = stage == GpuShaderStage.Vertex ? ShaderKind.VertexShader : ShaderKind.FragmentShader;
            var bytes = Encoding.UTF8.GetBytes(source);
            CompilationResult* result;
            fixed (byte* text = bytes)
                result = shaderc.CompileIntoSpv(compiler, text, (nuint)bytes.Length, kind, name, "main", options);
            try
            {
                if (shaderc.ResultGetCompilationStatus(result) != CompilationStatus.Success)
                    throw new InvalidOperationException(
                        $"Shader '{name}' ({stage}) failed to compile to SPIR-V: {shaderc.ResultGetErrorMessageS(result)}");
                var length = (int)shaderc.ResultGetLength(result);
                var words = new uint[length / 4];
                new ReadOnlySpan<byte>(shaderc.ResultGetBytes(result), length).CopyTo(MemoryMarshal.AsBytes(words.AsSpan()));
                return words;
            }
            finally
            {
                shaderc.ResultRelease(result);
            }
        }
        finally
        {
            shaderc.CompileOptionsRelease(options);
        }
    }

    private static (GpuUniformBlock? Block, uint BlockId, List<(string Name, uint Id)> Samplers) Reflect(
        Context* context, uint[] words, string name)
    {
        var cross = CrossApi.Value;
        ParsedIr* ir = null;
        CrossCompiler* compiler = null;
        fixed (uint* spirv = words)
            Check(cross.ContextParseSpirv(context, spirv, (nuint)words.Length, &ir), context, name);
        Check(cross.ContextCreateCompiler(context, Backend.None, ir, CaptureMode.TakeOwnership, &compiler), context, name);
        Resources* resources = null;
        Check(cross.CompilerCreateShaderResources(compiler, &resources), context, name);

        GpuUniformBlock? block = null;
        uint blockId = 0;
        ReflectedResource* list = null;
        nuint count = 0;
        Check(cross.ResourcesGetResourceListForType(resources, ResourceType.UniformBuffer, &list, &count), context, name);
        if (count > 1)
            throw new InvalidOperationException($"Shader '{name}' declares {count} uniform blocks; effects use loose uniforms.");
        if (count == 1)
        {
            blockId = list[0].Id;
            block = ReadBlock(compiler, list[0].BaseTypeId);
        }

        var samplers = new List<(string, uint)>();
        Check(cross.ResourcesGetResourceListForType(resources, ResourceType.SampledImage, &list, &count), context, name);
        for (nuint index = 0; index < count; index++)
            samplers.Add((SilkMarshal.PtrToString((nint)list[index].Name)!, list[index].Id));
        return (block, blockId, samplers);
    }

    private static GpuUniformBlock ReadBlock(CrossCompiler* compiler, uint typeId)
    {
        var cross = CrossApi.Value;
        var type = cross.CompilerGetTypeHandle(compiler, typeId);
        var memberCount = cross.TypeGetNumMemberTypes(type);
        var members = new List<GpuUniformMember>((int)memberCount);
        nuint size = 0;
        cross.CompilerGetDeclaredStructSize(compiler, type, &size);
        for (var index = 0u; index < memberCount; index++)
        {
            var memberTypeId = cross.TypeGetMemberType(type, index);
            var memberType = cross.CompilerGetTypeHandle(compiler, memberTypeId);
            var memberName = cross.CompilerGetMemberNameS(compiler, typeId, index);
            uint offset = 0, arrayStride = 0, matrixStride = 0;
            cross.CompilerTypeStructMemberOffset(compiler, type, index, &offset);
            var arrays = cross.TypeGetNumArrayDimensions(memberType);
            var arraySize = arrays > 0 ? (int)cross.TypeGetArrayDimension(memberType, 0) : 1;
            if (arrays > 0) cross.CompilerTypeStructMemberArrayStride(compiler, type, index, &arrayStride);
            var columns = (int)cross.TypeGetColumns(memberType);
            if (columns > 1) cross.CompilerTypeStructMemberMatrixStride(compiler, type, index, &matrixStride);
            var scalar = cross.TypeGetBasetype(memberType) switch
            {
                Basetype.FP32 => GpuUniformScalar.Float,
                Basetype.Int32 or Basetype.Uint32 or Basetype.Boolean => GpuUniformScalar.Int,
                var other => throw new NotSupportedException($"Uniform '{memberName}' has unsupported type {other}."),
            };
            members.Add(new GpuUniformMember(memberName, scalar, (int)offset, (int)cross.TypeGetVectorSize(memberType),
                columns, arraySize, (int)arrayStride, (int)matrixStride));
        }
        return new GpuUniformBlock((int)size, members);
    }

    private static (string Msl, string Entry) ToMsl(Context* context, uint[] words, GpuShaderStage stage, string name,
        string[] samplers)
    {
        var cross = CrossApi.Value;
        ParsedIr* ir = null;
        CrossCompiler* compiler = null;
        fixed (uint* spirv = words)
            Check(cross.ContextParseSpirv(context, spirv, (nuint)words.Length, &ir), context, name);
        Check(cross.ContextCreateCompiler(context, Backend.Msl, ir, CaptureMode.TakeOwnership, &compiler), context, name);
        CompilerOptions* options = null;
        Check(cross.CompilerCreateCompilerOptions(compiler, &options), context, name);
        cross.CompilerOptionsSetUint(options, CompilerOption.MslVersion, MslVersion(2, 1));
        cross.CompilerOptionsSetUint(options, CompilerOption.MslPlatform, (uint)MslPlatform.Macos);
        // Metal's clip space is y-down relative to GL's framebuffer rows; flipping keeps row 0 at the bottom.
        cross.CompilerOptionsSetBool(options, CompilerOption.FlipVertexY, stage == GpuShaderStage.Vertex ? (byte)1 : (byte)0);
        Check(cross.CompilerInstallCompilerOptions(compiler, options), context, name);

        var model = stage == GpuShaderStage.Vertex ? ExecutionModel.Vertex : ExecutionModel.Fragment;
        AddBinding(compiler, model, stage == GpuShaderStage.Vertex
            ? GpuCompiledProgram.VertexUniformBinding : GpuCompiledProgram.FragmentUniformBinding,
            GpuCompiledProgram.MslUniformBufferIndex, 0);
        for (var index = 0; index < samplers.Length; index++)
            AddBinding(compiler, model, GpuCompiledProgram.FirstSamplerBinding + index, 0, (uint)index);
        if (stage == GpuShaderStage.Vertex)
        {
            Resources* resources = null;
            Check(cross.CompilerCreateShaderResources(compiler, &resources), context, name);
            ReflectedResource* inputs = null;
            nuint count = 0;
            Check(cross.ResourcesGetResourceListForType(resources, ResourceType.StageInput, &inputs, &count), context, name);
            for (nuint index = 0; index < count; index++)
            {
                var attribute = new MslVertexAttribute
                {
                    Location = cross.CompilerGetDecoration(compiler, inputs[index].Id, Decoration.Location),
                    MslBuffer = GpuCompiledProgram.MslVertexBufferIndex,
                };
                Check(cross.CompilerMslAddVertexAttribute(compiler, &attribute), context, name);
            }
        }

        byte* source = null;
        Check(cross.CompilerCompile(compiler, &source), context, name);
        var msl = SilkMarshal.PtrToString((nint)source)!;
        var entry = cross.CompilerGetCleansedEntryPointNameS(compiler, "main", model);
        return (msl, entry);
    }

    private static void AddBinding(CrossCompiler* compiler, ExecutionModel model, int binding, uint buffer, uint texture)
    {
        var resource = new MslResourceBinding
        {
            Stage = model,
            DescSet = 0,
            Binding = (uint)binding,
            MslBuffer = buffer,
            MslTexture = texture,
            MslSampler = texture,
        };
        CrossApi.Value.CompilerMslAddResourceBinding(compiler, &resource);
    }

    private static uint MslVersion(uint major, uint minor) => major * 10000 + minor * 100;

    /// <summary>Rewrites an existing OpDecorate's literal for <paramref name="id"/> in place.</summary>
    private static void Patch(uint[] words, uint id, Decoration decoration, int value)
    {
        const uint opDecorate = 71;
        for (var index = 5; index < words.Length;)
        {
            var count = (int)(words[index] >> 16);
            var opcode = words[index] & 0xFFFF;
            if (count == 0) break;
            if (opcode == opDecorate && count == 4 && words[index + 1] == id && words[index + 2] == (uint)decoration)
            {
                words[index + 3] = (uint)value;
                return;
            }
            index += count;
        }
        throw new InvalidOperationException($"SPIR-V id {id} has no {decoration} decoration to rebind.");
    }

    private static void Check(Result result, Context* context, string name)
    {
        if (result == Result.Success) return;
        var message = context is null ? result.ToString() : CrossApi.Value.ContextGetLastErrorStringS(context);
        throw new InvalidOperationException($"SPIRV-Cross failed on shader '{name}': {message}");
    }
}
