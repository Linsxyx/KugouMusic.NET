using Avalonia;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Backgrounds;

public sealed class LatentBackgroundScene : EffectScene
{
    private const string Vertex = """
        #version 330 core
        out vec2 v_objectUV;
        out vec2 vUv;
        uniform vec2 u_resolution;
        void main() {
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vUv = p;
            v_objectUV = (p-.5)*u_resolution/min(u_resolution.x,u_resolution.y);
            gl_Position = vec4(p*2.-1.,0.,1.);
        }
        """;
    private const string Composite = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uMesh;
        uniform sampler2D uDither;
        uniform vec3 uBackground;
        uniform float uPower, uBass, uMid;
        out vec4 fragColor;
        float soft(float b,float s) {
            float d = b <= .25 ? ((16.*b-12.)*b+4.)*b : sqrt(b);
            return s <= .5 ? b-(1.-2.*s)*b*(1.-b) : b+(2.*s-1.)*(d-b);
        }
        void main() {
            vec3 b = texture(uMesh,(vUv-.5)/(1.025+uPower*.018)+.5).rgb;
            float l = dot(b,vec3(.213,.715,.072));
            b = clamp(mix(vec3(l),b,1.04+uMid*.34)*( .94+uPower*.16),0.,1.);
            vec3 s = texture(uDither,(vUv-.5)/(1.015+uBass*.025)+.5).rgb;
            vec3 blended = vec3(soft(b.r,s.r),soft(b.g,s.g),soft(b.b,s.b));
            b = mix(b,blended,min(1.,.55+uBass*.25));
            fragColor = vec4(mix(b,uBackground,.35),1.);
        }
        """;

    public LatentAudio Audio { get; set; } = new();
    public LatentPalette Palette { get; set; } = LatentPalette.Midnight;
    private GL _gl = null!;
    private EffectShaderProgram? _mesh, _dither, _composite;
    private EffectFramebuffer? _meshBuffer, _ditherBuffer;
    private uint _vao;
    private double _scaling = 1;
    private LatentModulation _motion = new();
    private LatentPalette? _lastPalette;
    private EffectColor[] _colors = [];
    private static readonly string[] ColorUniforms =
        ["u_colors[0]","u_colors[1]","u_colors[2]","u_colors[3]","u_colors[4]","u_colors[5]"];

    public override void Initialize(EffectDevice device)
    {
        base.Initialize(device);
        _gl = device.Gl;
        // Previous handles can belong to a lost context; never delete them here.
        _mesh = _dither = _composite = null;
        _meshBuffer = _ditherBuffer = null;
        _vao = 0;
        try
        {
            _mesh = new EffectShaderProgram(_gl,Vertex,LatentShaders.Mesh,"Folia latent mesh");
            _dither = new EffectShaderProgram(_gl,Vertex,LatentShaders.Dither,"Folia latent dither");
            _composite = new EffectShaderProgram(_gl,Vertex,Composite,"Folia latent soft-light");
            _meshBuffer = new EffectFramebuffer(_gl);
            _ditherBuffer = new EffectFramebuffer(_gl);
            _vao = _gl.GenVertexArray();
        }
        catch { DisposeGpuResources(); throw; }
    }

    public override void Resize(PixelSize size, double scaling) => _scaling = scaling;
    public override void Update(in EffectFrame frame) => _motion.Step(Audio,frame.Delta.TotalSeconds);

    public override void Render(EffectRenderContext context)
    {
        if (_mesh == null || _dither == null || _composite == null) return;
        var palette = Palette;
        if (!ReferenceEquals(_lastPalette,palette))
        {
            _lastPalette = palette;
            _colors = palette.MeshColors();
        }
        var size = context.PixelSize;
        var factor = Math.Min(1,Math.Sqrt(1280d*720/(size.Width*(double)size.Height)));
        var w = Math.Max(1,(int)Math.Round(size.Width*factor));
        var h = Math.Max(1,(int)Math.Round(size.Height*factor));
        _gl.GetInteger(GetPName.DrawFramebufferBinding,out var target);
        _gl.Disable(EnableCap.Blend);
        _gl.BindVertexArray(_vao);
        try
        {
            _meshBuffer!.EnsureSize(w,h);
            _ditherBuffer!.EnsureSize(w,h);
            DrawPass(_mesh,_meshBuffer,w,h,(float)_motion.MeshTime);
            Set(_mesh,"u_colorsCount",6);
            for (var i=0;i<6;i++) Color(_mesh,ColorUniforms[i],_colors[i]);
            Set(_mesh,"u_distortion",.8f+_motion.Power*.62f);
            Set(_mesh,"u_swirl",.1f+_motion.Mid*.38f);
            Set(_mesh,"u_grainMixer",0);
            Set(_mesh,"u_grainOverlay",.01f);
            _gl.DrawArrays(PrimitiveType.Triangles,0,3);

            DrawPass(_dither,_ditherBuffer,w,h,(float)_motion.DitherTime);
            Set(_dither,"u_pixelRatio",(float)(_scaling*w/size.Width));
            Set(_dither,"u_originX",.5f); Set(_dither,"u_originY",.5f);
            Set(_dither,"u_scale",1); Set(_dither,"u_fit",0);
            Set(_dither,"u_pxSize",Math.Max(.5f,2.5f-_motion.Bass*2.5f*.34f));
            Set(_dither,"u_shape",2); Set(_dither,"u_type",3);
            Color(_dither,"u_colorBack",palette.DitheringBackground);
            Color(_dither,"u_colorFront",_colors[0]);
            _gl.DrawArrays(PrimitiveType.Triangles,0,3);

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer,(uint)target);
            _gl.Viewport(0,0,(uint)size.Width,(uint)size.Height);
            _composite.Use();
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D,_meshBuffer.Texture);
            _gl.Uniform1(_composite.Uniform("uMesh"),0);
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D,_ditherBuffer.Texture);
            _gl.Uniform1(_composite.Uniform("uDither"),1);
            _gl.Uniform3(_composite.Uniform("uBackground"),palette.Background.R,palette.Background.G,palette.Background.B);
            Set(_composite,"uPower",_motion.Power); Set(_composite,"uBass",_motion.Bass); Set(_composite,"uMid",_motion.Mid);
            _gl.DrawArrays(PrimitiveType.Triangles,0,3);
            context.Primitives.RecordExternalDrawCalls(3);
        }
        finally
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer,(uint)target);
            _gl.Viewport(0,0,(uint)size.Width,(uint)size.Height);
        }
    }

    private void DrawPass(EffectShaderProgram program, EffectFramebuffer buffer,int w,int h,float time)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer,buffer.Framebuffer);
        _gl.Viewport(0,0,(uint)w,(uint)h);
        program.Use();
        _gl.Uniform2(program.Uniform("u_resolution"),(float)w,(float)h);
        Set(program,"u_time",time);
    }
    private void Set(EffectShaderProgram p,string name,float value) => _gl.Uniform1(p.Uniform(name),value);
    private void Color(EffectShaderProgram p,string name,EffectColor c) => _gl.Uniform4(p.Uniform(name),c.R,c.G,c.B,c.A);

    public override void DisposeGpuResources()
    {
        _mesh?.Dispose(); _dither?.Dispose(); _composite?.Dispose();
        _meshBuffer?.Dispose(); _ditherBuffer?.Dispose();
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        _mesh = _dither = _composite = null;
        _meshBuffer = _ditherBuffer = null;
        _vao = 0;
    }
}
