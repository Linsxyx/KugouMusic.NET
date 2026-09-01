using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AvaloniaSilkEffects.Showcase;

internal sealed class ShowcaseWindow : Window
{
    private readonly SilkEffectControl _surface;
    private readonly TextBlock _status;
    private readonly TextBlock _performance;
    private readonly DispatcherTimer _statisticsTimer;
    private ShowcaseScene _scene;

    public ShowcaseWindow()
    {
        Title = "AvaloniaSilkEffects Showcase";
        Width = 1590;
        Height = 780;
        MinWidth = 920;
        MinHeight = 620;
        Background = new SolidColorBrush(Color.Parse("#090A10"));

        _scene = new SonnetShowcaseScene();
        _status = new TextBlock
        {
            Text = "Waiting for the OpenGL compositor…",
            Foreground = Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap,
        };
        _performance = new TextBlock
        {
            Text = "Waiting for the first measured frame…",
            FontFamily = FontFamily.Parse("Menlo"),
            FontSize = 11,
            Foreground = Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap,
        };
        _surface = new SilkEffectControl
        {
            Scene = _scene,
            ClearColor = Color.Parse("#080912"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _surface.InitializationFailed += (_, eventArgs) => _status.Text = eventArgs.Message;
        _statisticsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500),
            DispatcherPriority.Background, (_, _) => RefreshStatistics());
        _statisticsTimer.Start();
        Closed += (_, _) => _statisticsTimer.Stop();

        var layout = new Grid
        {
            ColumnDefinitions = new("*,310"),
            Children =
            {
                _surface,
                BuildControls(),
            },
        };
        Grid.SetColumn(_surface, 0);
        Grid.SetColumn(layout.Children[1], 1);
        Content = layout;
    }

    private Control BuildControls()
    {
        var panel = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(22),
        };
        panel.Children.Add(new TextBlock
        {
            Text = "AVALONIA SILK EFFECTS",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "OpenGL 3.3 kinetic graphics laboratory",
            Foreground = Brushes.Gray,
        });
        panel.Children.Add(_performance);

        panel.Children.Add(Section("SCENES"));
        panel.Children.Add(SceneButton("Sonnet v0.7.2", () => new SonnetShowcaseScene()));
        panel.Children.Add(SceneButton("Dynamic type", () => new KineticTypographyScene()));
        panel.Children.Add(SceneButton("Geometry field", () => new GeometryScene()));
        panel.Children.Add(SceneButton("Post process lab", () => new PostProcessScene()));

        var paused = new CheckBox { Content = "Pause timeline", Foreground = Brushes.White };
        paused.IsCheckedChanged += (_, _) => _surface.IsPaused = paused.IsChecked == true;
        panel.Children.Add(paused);

        panel.Children.Add(Section("INTENSITY"));
        panel.Children.Add(Slider(0, 1, 0.65, value => _scene.Intensity = (float)value));
        panel.Children.Add(Section("TEXTURE RESOLUTION"));
        panel.Children.Add(Slider(1, 4, 2, value => _scene.TextRasterScale = (float)value));
        panel.Children.Add(Section("FILTER RESOLUTION"));
        panel.Children.Add(Slider(0.35, 1, 0.65, value => _scene.FilterResolutionScale = (float)value));
        panel.Children.Add(Section("ABSOLUTE TIME"));
        panel.Children.Add(Slider(0, 58, 0, value => _surface.Seek(TimeSpan.FromSeconds(value))));

        panel.Children.Add(Section("SEED"));
        var seed = new NumericUpDown
        {
            Minimum = 1,
            Maximum = uint.MaxValue,
            Value = _scene.Seed,
            Increment = 1,
            Foreground = Brushes.White,
        };
        seed.ValueChanged += (_, _) => _scene.Seed = (uint)(seed.Value ?? 1);
        panel.Children.Add(seed);

        panel.Children.Add(Section("ACCENT"));
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        colors.Children.Add(ColorButton("#31E8FF", new(0.19f, 0.91f, 1f)));
        colors.Children.Add(ColorButton("#FF3EA5", new(1f, 0.24f, 0.65f)));
        colors.Children.Add(ColorButton("#FFCB45", new(1f, 0.8f, 0.27f)));
        panel.Children.Add(colors);

        panel.Children.Add(Section("STATUS"));
        panel.Children.Add(_status);
        panel.Children.Add(new TextBlock
        {
            Text = "If this stays blank on macOS, confirm the Showcase started with the OpenGL backend instead of Metal/software.",
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
        });

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#11131C")),
            BorderBrush = new SolidColorBrush(Color.Parse("#252A3B")),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer { Content = panel },
        };
    }

    private void RefreshStatistics()
    {
        var stats = _surface.FrameStatistics;
        if (stats.SubmittedFrames == 0)
            return;

        _performance.Text =
            $"{stats.FramesPerSecond,5:F1} FPS   CPU {stats.CpuMilliseconds,5:F2} ms\n" +
            $"{stats.FramebufferSize.Width}×{stats.FramebufferSize.Height} px   " +
            $"draw {stats.DrawCalls} / flush {stats.Flushes}\n" +
            $"upload {stats.UploadedBytes / 1024d:F1} KiB   " +
            $"post {(stats.PostProcessingEnabled ? "on" : "bypass")}   skipped {stats.SkippedFrames}\n" +
            $"textures {stats.ResidentTextures} / {stats.ResidentTextureBytes / 1048576d:F1} MiB\n" +
            $"{stats.OpenGlVersion}\n{stats.Renderer}";
        if (_scene is SonnetShowcaseScene)
            _status.Text = _scene.Name;
    }

    private Button SceneButton(string label, Func<ShowcaseScene> factory)
    {
        var button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = Brushes.White,
        };
        button.Click += (_, _) =>
        {
            _scene = factory();
            _surface.Scene = _scene;
            _status.Text = $"Loaded {_scene.Name}.";
        };
        return button;
    }

    private Button ColorButton(string label, EffectColor color)
    {
        var button = new Button
        {
            Content = string.Empty,
            Width = 38,
            Height = 28,
            Background = new SolidColorBrush(Color.Parse(label)),
        };
        button.Click += (_, _) => _scene.Accent = color;
        return button;
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brushes.Gray,
        Margin = new Thickness(0, 8, 0, -6),
    };

    private static Slider Slider(double minimum, double maximum, double value, Action<double> changed)
    {
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            TickFrequency = (maximum - minimum) / 20,
        };
        slider.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.Property == RangeBase.ValueProperty)
                changed(slider.Value);
        };
        return slider;
    }
}
