using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Vela.Avalonia.Controls;
using Vela.Avalonia.Tokens;

namespace KugouAvaloniaPlayer.Services;

public interface IKugouToast
{
    void Dismiss();
}

public interface IKugouToastManager
{
    void AttachHost(VelaToastHost host);
    void DetachHost(VelaToastHost host);
    KugouToastBuilder CreateToast();
    void Dismiss(IKugouToast? toast);
}

public sealed class KugouToastBuilder
{
    private readonly KugouToastManager _manager;
    private string _title = string.Empty;
    private string? _description;
    private Control? _customContent;
    private NotificationType _type = NotificationType.Information;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _dismissOnClick;
    private readonly List<(object ButtonContent, Action<IKugouToast> OnClick, bool DismissOnClick)> _actions = [];

    internal KugouToastBuilder(KugouToastManager manager)
    {
        _manager = manager;
    }

    public KugouToastBuilder OfType(NotificationType type)
    {
        _type = type;
        return this;
    }

    public KugouToastBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public KugouToastBuilder WithContent(object? content)
    {
        if (content is Control ctrl)
            _customContent = ctrl;
        else if (content is not null)
            _description = content.ToString();
        return this;
    }

    public KugouToastBuilder WithActionButton(object buttonContent, Action<IKugouToast> onClick, bool dismissOnClick = true)
    {
        _actions.Add((buttonContent, onClick, dismissOnClick));
        return this;
    }

    public KugouToastDismissBuilder Dismiss() => new(this);

    internal void SetDuration(TimeSpan duration) => _duration = duration;
    internal void SetDismissOnClick() => _dismissOnClick = true;

    public IKugouToast Queue()
    {
        var handle = new KugouToastHandle();
        _manager.Enqueue(handle, _title, _description, _customContent, _type, _duration, _dismissOnClick, _actions);
        return handle;
    }
}

public readonly struct KugouToastDismissBuilder(KugouToastBuilder builder)
{
    public KugouToastBuilder After(TimeSpan delay)
    {
        builder.SetDuration(delay);
        return builder;
    }

    public KugouToastBuilder ByClicking()
    {
        builder.SetDismissOnClick();
        return builder;
    }
}

internal sealed class KugouToastHandle : IKugouToast
{
    private IDisposable? _inner;
    private bool _dismissedBeforeAttach;

    public void Attach(IDisposable inner)
    {
        if (_dismissedBeforeAttach)
        {
            inner.Dispose();
            return;
        }
        _inner = inner;
    }

    public void Dismiss()
    {
        _dismissedBeforeAttach = true;
        _inner?.Dispose();
        _inner = null;
    }
}

public sealed class KugouToastManager : IKugouToastManager
{
    private VelaToastHost? _host;

    public void AttachHost(VelaToastHost host) => _host = host;

    public void DetachHost(VelaToastHost host)
    {
        if (ReferenceEquals(_host, host))
            _host = null;
    }

    public KugouToastBuilder CreateToast() => new(this);

    public void Dismiss(IKugouToast? toast) => toast?.Dismiss();

    internal void Enqueue(
        KugouToastHandle handle,
        string title,
        string? description,
        Control? customContent,
        NotificationType type,
        TimeSpan duration,
        bool dismissOnClick,
        List<(object ButtonContent, Action<IKugouToast> OnClick, bool DismissOnClick)> actions)
    {
        void ShowOnUi()
        {
            if (_host is null)
                return;

            var tone = type switch
            {
                NotificationType.Success => VelaToastTone.Success,
                NotificationType.Warning => VelaToastTone.Warning,
                NotificationType.Error => VelaToastTone.Danger,
                NotificationType.Information => VelaToastTone.Info,
                _ => VelaToastTone.Neutral,
            };

            Control? bodyContent = customContent;
            if (actions.Count > 0)
            {
                var panel = new StackPanel { Spacing = 8 };
                if (bodyContent is not null)
                    panel.Children.Add(bodyContent);

                var btnRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(0, 4, 0, 0),
                };

                foreach (var (btnContent, onClick, dismissAfter) in actions)
                {
                    if (btnContent is Button existingBtn)
                    {
                        existingBtn.Click += (_, _) =>
                        {
                            onClick(handle);
                            if (dismissAfter)
                                handle.Dismiss();
                        };
                        btnRow.Children.Add(existingBtn);
                    }
                    else
                    {
                        var velaBtn = new VelaButton
                        {
                            Variant = VelaButtonVariant.Secondary,
                            Size = VelaControlSize.Small,
                            Content = btnContent,
                        };
                        velaBtn.Click += (_, _) =>
                        {
                            onClick(handle);
                            if (dismissAfter)
                                handle.Dismiss();
                        };
                        btnRow.Children.Add(velaBtn);
                    }
                }

                panel.Children.Add(btnRow);
                bodyContent = panel;
            }

            var inner = _host.ShowWithHandle(new VelaToastOptions
            {
                Title = string.IsNullOrWhiteSpace(title) ? "提示" : title,
                Description = description,
                Content = bodyContent,
                Tone = tone,
                Duration = duration,
                DismissOnClick = dismissOnClick,
            });
            handle.Attach(inner);
        }

        if (Dispatcher.UIThread.CheckAccess())
            ShowOnUi();
        else
            Dispatcher.UIThread.Post(ShowOnUi);
    }
}

public interface IKugouDialog
{
    void Dismiss();
}

public sealed class KugouDialogManagerEventArgs(IKugouDialog dialog) : EventArgs
{
    public IKugouDialog Dialog { get; } = dialog;
}

public interface IKugouDialogManager
{
    event EventHandler<KugouDialogManagerEventArgs>? OnDialogShown;
    event EventHandler<KugouDialogManagerEventArgs>? OnDialogDismissed;

    void AttachHost(VelaDialogHost host);
    void DetachHost(VelaDialogHost host);
    KugouDialogBuilder CreateDialog();
    void DismissDialog();
    bool TryDismissDialog(IKugouDialog? dialog);
}

public sealed class KugouDialogBuilder
{
    private readonly KugouDialogManager _manager;
    private readonly KugouDialogHandle _dialog;
    private string? _title;
    private object? _content;
    private Func<IKugouDialog, object>? _viewModelFactory;
    private NotificationType? _type;
    private bool _showCardBackground = true;
    private Action<IKugouDialog>? _onDismissed;
    private readonly List<(object Content, Action<IKugouDialog> OnClick, bool DismissOnClick, string[] Classes)> _buttons = [];

    internal KugouDialogBuilder(KugouDialogManager manager)
    {
        _manager = manager;
        _dialog = new KugouDialogHandle(manager);
    }

    public IKugouDialog Dialog => _dialog;

    public KugouDialogBuilder OfType(NotificationType type)
    {
        _type = type;
        return this;
    }

    public KugouDialogBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public KugouDialogBuilder WithContent(object? content)
    {
        _content = content;
        return this;
    }

    public KugouDialogBuilder WithViewModel(Func<IKugouDialog, object> factory)
    {
        _viewModelFactory = factory;
        return this;
    }

    public KugouDialogBuilder ShowCardBackground(bool show)
    {
        _showCardBackground = show;
        return this;
    }

    public KugouDialogBuilder OnDismissed(Action<IKugouDialog> callback)
    {
        _onDismissed = callback;
        return this;
    }

    public KugouDialogBuilder WithActionButton(
        object buttonContent,
        Action<IKugouDialog> onClick,
        bool dismissOnClick = true,
        params string[] classes)
    {
        _buttons.Add((buttonContent, onClick, dismissOnClick, classes));
        return this;
    }

    public bool TryShow()
    {
        return _manager.Show(_dialog, _title, _content, _viewModelFactory, _type, _showCardBackground, _onDismissed, _buttons);
    }
}

internal sealed class KugouDialogHandle(KugouDialogManager manager) : IKugouDialog
{
    public void Dismiss() => manager.TryDismissDialog(this);
}

public sealed class KugouDialogManager : IKugouDialogManager
{
    private VelaDialogHost? _host;
    private IKugouDialog? _activeDialog;

    public event EventHandler<KugouDialogManagerEventArgs>? OnDialogShown;
    public event EventHandler<KugouDialogManagerEventArgs>? OnDialogDismissed;

    public void AttachHost(VelaDialogHost host) => _host = host;

    public void DetachHost(VelaDialogHost host)
    {
        if (ReferenceEquals(_host, host))
            _host = null;
    }

    public KugouDialogBuilder CreateDialog() => new(this);

    public void DismissDialog()
    {
        void CloseOnUi()
        {
            _host?.Close();
        }

        if (Dispatcher.UIThread.CheckAccess())
            CloseOnUi();
        else
            Dispatcher.UIThread.Post(CloseOnUi);
    }

    public bool TryDismissDialog(IKugouDialog? dialog)
    {
        if (dialog is null)
            return false;

        DismissDialog();
        return true;
    }

    internal bool Show(
        IKugouDialog dialog,
        string? title,
        object? content,
        Func<IKugouDialog, object>? viewModelFactory,
        NotificationType? type,
        bool showCardBackground,
        Action<IKugouDialog>? onDismissed,
        List<(object Content, Action<IKugouDialog> OnClick, bool DismissOnClick, string[] Classes)> buttons)
    {
        if (_host is null)
            return false;

        void ShowOnUi()
        {
            if (_host is null)
                return;

            var resolved = viewModelFactory is not null ? viewModelFactory(dialog) : content;
            Control bodyControl = resolved switch
            {
                Control ctrl => ctrl,
                string text => new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    LineHeight = 22,
                },
                not null => new ContentControl { Content = resolved },
                _ => new Border(),
            };

            if (buttons.Count > 0)
            {
                var wrapper = new StackPanel { Spacing = 16 };
                wrapper.Children.Add(bodyControl);

                var footer = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                };

                foreach (var (btnContent, onClick, dismissAfter, classes) in buttons)
                {
                    var isAccent = Array.Exists(classes, c =>
                        c.Equals("Accent", StringComparison.OrdinalIgnoreCase) ||
                        c.Equals("Primary", StringComparison.OrdinalIgnoreCase));
                    var isBasic = Array.Exists(classes, c =>
                        c.Equals("Basic", StringComparison.OrdinalIgnoreCase));

                    var btn = new VelaButton
                    {
                        Variant = isAccent ? VelaButtonVariant.Primary : (isBasic ? VelaButtonVariant.Ghost : VelaButtonVariant.Secondary),
                        Size = VelaControlSize.Small,
                        Content = btnContent,
                        MinWidth = 76,
                    };
                    foreach (var cls in classes)
                    {
                        if (!string.IsNullOrWhiteSpace(cls))
                            btn.Classes.Add(cls);
                    }

                    btn.Click += (_, _) =>
                    {
                        onClick(dialog);
                        if (dismissAfter)
                            _host?.Close();
                    };

                    footer.Children.Add(btn);
                }

                wrapper.Children.Add(footer);
                bodyControl = wrapper;
            }

            var tone = type switch
            {
                NotificationType.Success => VelaDialogTone.Success,
                NotificationType.Warning => VelaDialogTone.Warning,
                NotificationType.Error => VelaDialogTone.Danger,
                NotificationType.Information => VelaDialogTone.Info,
                _ => VelaDialogTone.Neutral,
            };

            var isCustomControl = resolved is Control;
            var options = new VelaDialogOptions
            {
                Title = title,
                MaxWidth = isCustomControl ? double.NaN : 440,
                ShowCloseButton = !string.IsNullOrWhiteSpace(title),
                CloseOnBackdropClick = false,
                Material = showCardBackground ? VelaSurfaceMaterial.GlassCard : VelaSurfaceMaterial.Solid,
                Style = VelaDialogStyle.Standard,
                Tone = tone,
            };

            _activeDialog = dialog;
            OnDialogShown?.Invoke(this, new KugouDialogManagerEventArgs(dialog));

            var task = _host.ShowAsync<object?>(bodyControl, options);
            _ = task.ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(_activeDialog, dialog))
                        _activeDialog = null;
                    OnDialogDismissed?.Invoke(this, new KugouDialogManagerEventArgs(dialog));
                    onDismissed?.Invoke(dialog);
                });
            });
        }

        if (Dispatcher.UIThread.CheckAccess())
            ShowOnUi();
        else
            Dispatcher.UIThread.Post(ShowOnUi);

        return true;
    }
}
