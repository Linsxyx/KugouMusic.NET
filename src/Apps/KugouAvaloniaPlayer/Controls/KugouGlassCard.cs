using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Vela.Avalonia.Controls;

namespace KugouAvaloniaPlayer.Controls;

/// <summary>
/// VelaGlassCard-backed glass card with SukiUI GlassCard interaction/compatibility properties
/// (IsInteractive, IsOpaque, IsAnimated, Command, CommandParameter).
/// Defaults to lightweight translucent glass without per-card heavy 80px backdrop blur.
/// </summary>
public class KugouGlassCard : VelaGlassCard
{
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<KugouGlassCard, bool>(nameof(IsInteractive));

    public static readonly StyledProperty<bool> IsAnimatedProperty =
        AvaloniaProperty.Register<KugouGlassCard, bool>(nameof(IsAnimated));

    public static readonly StyledProperty<bool> IsOpaqueProperty =
        AvaloniaProperty.Register<KugouGlassCard, bool>(nameof(IsOpaque));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<KugouGlassCard, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<KugouGlassCard, object?>(nameof(CommandParameter));

    static KugouGlassCard()
    {
        BlurBackgroundProperty.OverrideDefaultValue<KugouGlassCard>(false);
        CardShadowProperty.OverrideDefaultValue<KugouGlassCard>(default);
        CornerRadiusProperty.OverrideDefaultValue<KugouGlassCard>(new CornerRadius(14));
        PaddingProperty.OverrideDefaultValue<KugouGlassCard>(new Thickness(14));
        HorizontalContentAlignmentProperty.OverrideDefaultValue<KugouGlassCard>(Avalonia.Layout.HorizontalAlignment.Stretch);
        VerticalContentAlignmentProperty.OverrideDefaultValue<KugouGlassCard>(Avalonia.Layout.VerticalAlignment.Stretch);
        TintLevelProperty.OverrideDefaultValue<KugouGlassCard>(VelaGlassTintLevel.Standard);

        IsInteractiveProperty.Changed.AddClassHandler<KugouGlassCard>((card, _) =>
        {
            card.Cursor = card.IsInteractive ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        });
    }

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public bool IsAnimated
    {
        get => GetValue(IsAnimatedProperty);
        set => SetValue(IsAnimatedProperty, value);
    }

    public bool IsOpaque
    {
        get => GetValue(IsOpaqueProperty);
        set => SetValue(IsOpaqueProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(KugouGlassCard);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!IsInteractive || Command is null)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (Command.CanExecute(CommandParameter))
        {
            Command.Execute(CommandParameter);
            e.Handled = true;
        }
    }
}
