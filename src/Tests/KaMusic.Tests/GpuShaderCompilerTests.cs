using System.Reflection;
using AvaloniaSilkEffects.Gpu.Shaders;

namespace AvaloniaSilkEffects.Tests;

public sealed class GpuShaderCompilerTests
{
    private const string Fullscreen = """
        #version 330 core
        out vec2 vUv;
        void main() {
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string Solid = """
        #version 330 core
        out vec4 finalColor;
        void main() { finalColor = vec4(1.0); }
        """;

    /// <summary>Every GLSL constant in the library: (owner.field, source).</summary>
    public static TheoryData<string> Sources()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in LibraryShaders()) data.Add(name);
        return data;
    }

    internal static IEnumerable<(string Name, string Source)> LibraryShaders() =>
        typeof(EffectDevice).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => ($"{type.Name}.{field.Name}", (string)field.GetRawConstantValue()!)))
            .Where(entry => entry.Item2.TrimStart().StartsWith("#version 330 core", StringComparison.Ordinal) &&
                entry.Item2.Contains("void main", StringComparison.Ordinal));

    [Theory]
    [MemberData(nameof(Sources))]
    public void CompilesToSpirvAndMsl(string name)
    {
        var source = LibraryShaders().Single(entry => entry.Name == name).Source;
        var vertex = source.Contains("gl_Position", StringComparison.Ordinal);
        foreach (var target in new[] { GpuShaderTarget.Spirv, GpuShaderTarget.Msl })
        {
            var program = vertex
                ? GpuShaderCompiler.Compile(source, Solid, name, target)
                : GpuShaderCompiler.Compile(Fullscreen, source, name, target);
            Assert.NotEmpty(program.Vertex.Spirv);
            Assert.NotEmpty(program.Fragment.Spirv);
            if (target == GpuShaderTarget.Msl)
            {
                Assert.Contains("#include <metal_stdlib>", program.Vertex.Msl);
                Assert.Contains("#include <metal_stdlib>", program.Fragment.Msl);
            }
        }
    }

    [Fact]
    public void ReflectsStd140LayoutAndSamplerOrder()
    {
        const string fragment = """
            #version 330 core
            in vec2 vUv;
            uniform sampler2D uA;
            uniform float uX, uY;
            uniform vec3 uStops[4];
            uniform mat3 uMatrix;
            uniform int uMode;
            uniform sampler2D uB;
            out vec4 finalColor;
            void main() {
                vec3 m = uMatrix * vec3(vUv, 1.0);
                finalColor = texture(uA, vUv) + texture(uB, m.xy) + vec4(uStops[uMode], uX + uY);
            }
            """;
        var program = GpuShaderCompiler.Compile(Fullscreen, fragment, "layout", GpuShaderTarget.Msl);
        Assert.Equal(["uA", "uB"], program.Samplers);
        var block = program.Fragment.Uniforms!;
        var stops = block.Members.Single(member => member.Name == "uStops");
        Assert.Equal((3, 4, 16), (stops.VectorSize, stops.ArraySize, stops.ArrayStride));
        var matrix = block.Members.Single(member => member.Name == "uMatrix");
        Assert.Equal((3, 3, 16), (matrix.VectorSize, matrix.Columns, matrix.MatrixStride));
        Assert.Equal(GpuUniformScalar.Int, block.Members.Single(member => member.Name == "uMode").Scalar);
        Assert.Null(program.Vertex.Uniforms);
    }

    [Fact]
    public void VaryingsMatchByNameAcrossStages()
    {
        var (vertex, fragment) = GpuShaderCompiler.Preprocess("""
            #version 330 core
            out vec2 v_objectUV;
            out vec2 vUv;
            void main() { gl_Position = vec4(0.0); }
            """, """
            #version 330 core
            in vec2 vUv;
            out vec4 fragColor;
            void main() { fragColor = vec4(vUv, 0.0, 1.0); }
            """);
        Assert.Contains("layout(location = 1) out vec2 vUv;", vertex);
        Assert.Contains("layout(location = 1) in vec2 vUv;", fragment);
    }
}
