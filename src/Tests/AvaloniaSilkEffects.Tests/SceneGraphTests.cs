using System.Numerics;

namespace AvaloniaSilkEffects.Tests;

public sealed class SceneGraphTests
{
    [Fact]
    public void WorldTransform_ComposesPivotScaleRotationAndParents()
    {
        var parent = new EffectContainer
        {
            Position = new(100, 40),
            Scale = new(2, 2),
        };
        var child = new ShapeNode
        {
            Position = new(20, 10),
            Pivot = new(5, 5),
        };
        parent.Add(child);

        var transformedPivot = Vector2.Transform(child.Pivot, child.WorldTransform);

        Assert.Equal(new Vector2(140, 60), transformedPivot);
    }

    [Fact]
    public void WorldAlpha_MultipliesTheHierarchy()
    {
        var root = new EffectContainer { Alpha = 0.5f };
        var branch = new EffectContainer { Alpha = 0.4f };
        var leaf = new ShapeNode { Alpha = 0.25f };
        root.Add(branch);
        branch.Add(leaf);

        Assert.Equal(0.05f, leaf.WorldAlpha, 5);
    }

    [Fact]
    public void Add_RejectsASecondParent()
    {
        var first = new EffectContainer();
        var second = new EffectContainer();
        var child = new ShapeNode();
        first.Add(child);

        Assert.Throws<InvalidOperationException>(() => second.Add(child));
    }
}
