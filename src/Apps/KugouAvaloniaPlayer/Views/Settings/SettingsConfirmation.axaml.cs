using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace KugouAvaloniaPlayer.Views.Settings;

public partial class SettingsConfirmation : UserControl
{
    public SettingsConfirmation() => InitializeComponent();
    public SettingsConfirmation(string title, string description, string action, Action confirm, Action cancel) : this()
    {
        Heading.Text = title;
        Description.Text = description;
        ConfirmButton.Content = action;
        CancelButton.Click += (_, _) => cancel();
        ConfirmButton.Click += (_, _) => { ConfirmButton.IsEnabled = false; confirm(); };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            cancel();
        };
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => CancelButton.Focus(), DispatcherPriority.Loaded);
    }
}
