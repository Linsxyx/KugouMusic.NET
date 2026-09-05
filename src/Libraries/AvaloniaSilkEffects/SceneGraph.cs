using System.Numerics;

namespace AvaloniaSilkEffects;

public abstract class EffectNode
{
    public Vector2 Position { get; set; }
    public Vector2 Scale { get; set; } = Vector2.One;
    public Vector2 Pivot { get; set; }
    public float Rotation { get; set; }
    public float Alpha { get; set; } = 1;
    public bool IsVisible { get; set; } = true;
    public EffectBlendMode BlendMode { get; set; } = EffectBlendMode.Alpha;
    public EffectContainer? Parent { get; internal set; }

    public Matrix3x2 LocalTransform =>
        Matrix3x2.CreateTranslation(-Pivot) *
        Matrix3x2.CreateScale(Scale) *
        Matrix3x2.CreateRotation(Rotation) *
        Matrix3x2.CreateTranslation(Position);

    public Matrix3x2 WorldTransform => Parent is null
        ? LocalTransform
        : LocalTransform * Parent.WorldTransform;

    public float WorldAlpha => Alpha * (Parent?.WorldAlpha ?? 1);

    public abstract void Render(EffectRenderContext context);
}

public sealed class EffectContainer : EffectNode
{
    private readonly List<EffectNode> _children = [];
    public IReadOnlyList<EffectNode> Children => _children;

    public EffectContainer Add(EffectNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
            throw new InvalidOperationException("The effect node already belongs to a container.");
        child.Parent = this;
        _children.Add(child);
        return this;
    }

    public bool Remove(EffectNode child)
    {
        if (!_children.Remove(child))
            return false;
        child.Parent = null;
        return true;
    }

    public override void Render(EffectRenderContext context)
    {
        if (!IsVisible || WorldAlpha <= 0)
            return;
        foreach (var child in _children)
            child.Render(context);
    }
}

public enum EffectShapeKind
{
    Rectangle,
    Ellipse,
    Line,
}

public sealed class ShapeNode : EffectNode
{
    public EffectShapeKind Shape { get; set; } = EffectShapeKind.Rectangle;
    public Vector2 Size { get; set; } = new(100, 100);
    public EffectColor Color { get; set; } = EffectColor.White;
    public float StrokeWidth { get; set; } = 1;

    public override void Render(EffectRenderContext context)
    {
        if (!IsVisible || WorldAlpha <= 0)
            return;
        context.Primitives.DrawShape(this);
    }
}

/// <summary>A connected, tapered polyline rendered as one joined triangle strip.</summary>
public sealed class PolylineNode : EffectNode
{
    public IReadOnlyList<Vector2> Points { get; set; } = [];
    public int StartPointIndex { get; set; }
    public int EndPointIndex { get; set; } = int.MaxValue;
    public float TailWidth { get; set; } = 1;
    public float HeadWidth { get; set; } = 1;
    public float TailAlpha { get; set; } = 0.15f;
    public float HeadAlpha { get; set; } = 1;
    public EffectColor Color { get; set; } = EffectColor.White;

    public override void Render(EffectRenderContext context)
    {
        if (!IsVisible || WorldAlpha <= 0 || Points.Count < 2)
            return;
        context.Primitives.DrawPolyline(this);
    }
}

public sealed class PolygonNode : EffectNode
{
    private IReadOnlyList<Vector2> _points = Array.Empty<Vector2>();
    public IReadOnlyList<Vector2> Points
    {
        get => _points;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var snapshot = value.ToArray();
            var indices = PolygonTriangulator.Triangulate(snapshot);
            _points = Array.AsReadOnly(snapshot);
            TriangleIndices = indices;
        }
    }
    internal int[] TriangleIndices { get; private set; } = [];
    public EffectColor Color { get; set; } = EffectColor.White;

    public override void Render(EffectRenderContext context)
    {
        if (!IsVisible || WorldAlpha <= 0 || Points.Count < 3)
            return;
        context.Primitives.DrawPolygon(this);
    }
}

public sealed class SpriteNode : EffectNode
{
    public EffectTexture? Texture { get; set; }
    public Vector2 Size { get; set; }
    public EffectColor Tint { get; set; } = EffectColor.White;

    public override void Render(EffectRenderContext context)
    {
        if (!IsVisible || WorldAlpha <= 0 || Texture is null)
            return;
        context.Device.Textures.Touch(Texture);
        context.Primitives.DrawSprite(this);
    }
}

public sealed class TextNode : EffectNode
{
    private EffectTexture? _texture;
    private string? _cachedText;
    private string? _cachedFontFamily;
    private float _cachedFontSize;
    private int _cachedFontWeight;
    private EffectColor _cachedColor;
    private float _cachedRasterScale;

    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Inter";
    public float FontSize { get; set; } = 48;
    public int FontWeight { get; set; } = 600;
    public EffectColor Color { get; set; } = EffectColor.White;
    public float RasterScale { get; set; } = 2;
    public Vector2 Anchor { get; set; }

    public override void Render(EffectRenderContext context)
    {
        if (!IsVisible || WorldAlpha <= 0 || string.IsNullOrEmpty(Text))
            return;

        // Compare cached inputs instead of rebuilding the cache-key string every frame.
        if (_texture is null || _texture.IsDisposed ||
            !ReferenceEquals(_cachedText, Text) && _cachedText != Text ||
            !ReferenceEquals(_cachedFontFamily, FontFamily) && _cachedFontFamily != FontFamily ||
            _cachedFontSize != FontSize || _cachedFontWeight != FontWeight ||
            _cachedColor != Color || _cachedRasterScale != RasterScale)
        {
            _texture = context.Device.Textures.GetOrCreateText(
                Text,
                FontFamily,
                FontSize,
                FontWeight,
                Color,
                RasterScale);
            _cachedText = Text;
            _cachedFontFamily = FontFamily;
            _cachedFontSize = FontSize;
            _cachedFontWeight = FontWeight;
            _cachedColor = Color;
            _cachedRasterScale = RasterScale;
        }
        context.Device.Textures.Touch(_texture);
        var transform = Matrix3x2.CreateTranslation(-_texture.LogicalSize * Anchor) * WorldTransform;
        context.Primitives.DrawTexture(_texture, transform, _texture.LogicalSize, WorldAlpha, BlendMode, EffectColor.White);
    }
}
