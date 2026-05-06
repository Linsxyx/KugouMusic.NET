using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services.GlobalShortcutService;

public interface IGlobalShortcutService
{
    bool IsSupported { get; }
    IReadOnlyDictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult> CurrentResults { get; }
    event Action<IReadOnlyDictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult>>? RegistrationChanged;
    void Initialize(Window mainWindow);
    GlobalShortcutApplyResult LoadFromSettings(GlobalShortcutSettings settings);
    GlobalShortcutApplyResult TryApplySettings(GlobalShortcutSettings settings);
    void UnregisterAll();
}

public static class GlobalShortcutParser
{
    private static readonly Dictionary<string, Key> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = Key.Space,
        ["Left"] = Key.Left,
        ["Right"] = Key.Right,
        ["Up"] = Key.Up,
        ["Down"] = Key.Down,
        ["Plus"] = Key.OemPlus,
        ["Minus"] = Key.OemMinus
    };

    public static bool TryParse(string? text, out GlobalShortcutGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        var modifiers = GlobalShortcutModifiers.None;
        Key? mainKey = null;
        foreach (var part in parts)
        {
            if (TryParseModifier(part, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (mainKey != null)
                return false;

            if (!TryParseKey(part, out var key))
                return false;

            mainKey = key;
        }

        if (modifiers == GlobalShortcutModifiers.None || mainKey == null)
            return false;

        gesture = new GlobalShortcutGesture(modifiers, mainKey.Value);
        return true;
    }

    public static bool TryCreateFromKeyEvent(KeyEventArgs args, out GlobalShortcutGesture gesture, out string? error)
    {
        gesture = default;
        error = null;

        if (args.Key == Key.Escape)
        {
            error = "已取消录制。";
            return false;
        }

        if (args.Key is Key.Back or Key.Delete)
        {
            error = "请使用右侧“清空”按钮移除快捷键。";
            return false;
        }

        var normalizedKey = NormalizeKey(args.Key);
        if (normalizedKey == null)
        {
            error = "请按下一个有效的主键。";
            return false;
        }

        var modifiers = NormalizeModifiers(args.KeyModifiers);
        if (modifiers == GlobalShortcutModifiers.None)
        {
            error = "快捷键至少需要一个修饰键。";
            return false;
        }

        gesture = new GlobalShortcutGesture(modifiers, normalizedKey.Value);
        return true;
    }

    public static bool IsModifierOnlyKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or Key.LeftShift or Key.RightShift;
    }

    public static string FormatModifiers(KeyModifiers modifiers)
    {
        var normalized = NormalizeModifiers(modifiers);
        if (normalized == GlobalShortcutModifiers.None)
            return string.Empty;

        var parts = new List<string>(4);
        if (normalized.HasFlag(GlobalShortcutModifiers.Control))
            parts.Add("Ctrl");
        if (normalized.HasFlag(GlobalShortcutModifiers.Alt))
            parts.Add("Alt");
        if (normalized.HasFlag(GlobalShortcutModifiers.Shift))
            parts.Add("Shift");
        if (normalized.HasFlag(GlobalShortcutModifiers.Meta))
            parts.Add("Win");
        return string.Join("+", parts);
    }

    public static string Format(GlobalShortcutGesture gesture)
    {
        var parts = new List<string>(5);
        if (gesture.Modifiers.HasFlag(GlobalShortcutModifiers.Control))
            parts.Add("Ctrl");
        if (gesture.Modifiers.HasFlag(GlobalShortcutModifiers.Alt))
            parts.Add("Alt");
        if (gesture.Modifiers.HasFlag(GlobalShortcutModifiers.Shift))
            parts.Add("Shift");
        if (gesture.Modifiers.HasFlag(GlobalShortcutModifiers.Meta))
            parts.Add("Win");

        parts.Add(FormatKey(gesture.Key));
        return string.Join("+", parts);
    }

    public static string NormalizeText(string? text)
    {
        return TryParse(text, out var gesture) ? Format(gesture) : string.Empty;
    }

    private static bool TryParseModifier(string text, out GlobalShortcutModifiers modifier)
    {
        modifier = text.ToLowerInvariant() switch
        {
            "ctrl" or "control" => GlobalShortcutModifiers.Control,
            "alt" => GlobalShortcutModifiers.Alt,
            "shift" => GlobalShortcutModifiers.Shift,
            "win" or "windows" or "meta" or "super" or "cmd" or "command" => GlobalShortcutModifiers.Meta,
            _ => GlobalShortcutModifiers.None
        };

        return modifier != GlobalShortcutModifiers.None;
    }

    private static bool TryParseKey(string text, out Key key)
    {
        if (KeyAliases.TryGetValue(text, out key))
            return true;

        if (text.Length == 1)
        {
            var ch = char.ToUpperInvariant(text[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                key = Enum.Parse<Key>(ch.ToString());
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                key = Enum.Parse<Key>($"D{ch}");
                return true;
            }
        }

        return Enum.TryParse(text, true, out key);
    }

    private static GlobalShortcutModifiers NormalizeModifiers(KeyModifiers modifiers)
    {
        var normalized = GlobalShortcutModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control))
            normalized |= GlobalShortcutModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            normalized |= GlobalShortcutModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            normalized |= GlobalShortcutModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta))
            normalized |= GlobalShortcutModifiers.Meta;
        return normalized;
    }

    private static Key? NormalizeKey(Key key)
    {
        return key switch
        {
            Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or
                Key.LeftShift or Key.RightShift => null,
            _ => key
        };
    }

    private static string FormatKey(Key key)
    {
        return key switch
        {
            Key.Space => "Space",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            >= Key.A and <= Key.Z => key.ToString().ToUpperInvariant(),
            >= Key.D0 and <= Key.D9 => key.ToString()[1..],
            _ => key.ToString()
        };
    }
}

public sealed partial class GlobalShortcutService(
    PlayerViewModel playerViewModel,
    IDesktopLyricWindowService desktopLyricWindowService,
    IMainWindowService mainWindowService,
    IUiDispatcherService uiDispatcher,
    ILogger<GlobalShortcutService> logger)
    : IGlobalShortcutService
{
    private readonly Dictionary<GlobalShortcutAction, GlobalShortcutGesture> _activeGestures = new();

    private readonly Dictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult> _currentResults =
        CreateEmptyResults();

    private readonly ILogger<GlobalShortcutService> _logger = logger;
    private Window? _mainWindow;

    public bool IsSupported => GetPlatformSupport();

    public IReadOnlyDictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult> CurrentResults =>
        new ReadOnlyDictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult>(_currentResults);

    public event Action<IReadOnlyDictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult>>?
        RegistrationChanged;

    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;
        InitializePlatform(mainWindow);
    }

    public GlobalShortcutApplyResult LoadFromSettings(GlobalShortcutSettings settings)
    {
        var desired = ParseSettings(settings, out var parseResults);
        ApplyDesiredGestures(desired, parseResults, true);
        return new GlobalShortcutApplyResult(
            parseResults.Values.All(x => x.IsRegistered || x.FailureKind == GlobalShortcutRegistrationFailureKind.None),
            CurrentResults);
    }

    public GlobalShortcutApplyResult TryApplySettings(GlobalShortcutSettings settings)
    {
        var desired = ParseSettings(settings, out var parseResults);
        if (parseResults.Values.Any(x => x.FailureKind == GlobalShortcutRegistrationFailureKind.InvalidGesture))
            return new GlobalShortcutApplyResult(false, parseResults);

        if (!IsSupported)
        {
            ApplyUnsupportedResults(desired);
            return new GlobalShortcutApplyResult(true, CurrentResults);
        }

        var previous = _activeGestures.ToDictionary(x => x.Key, x => x.Value);
        var success = ApplyDesiredGestures(desired, parseResults, false);
        if (success)
            return new GlobalShortcutApplyResult(true, CurrentResults);

        ApplyDesiredGestures(previous, CreateEmptyResults(), true);
        return new GlobalShortcutApplyResult(false, parseResults);
    }

    public void UnregisterAll()
    {
        foreach (var action in _activeGestures.Keys.ToArray())
            UnregisterPlatformShortcut(action);

        _activeGestures.Clear();
        foreach (var action in Enum.GetValues<GlobalShortcutAction>())
            _currentResults[action] = new GlobalShortcutRegistrationResult(false);
        NotifyRegistrationChanged();
    }

    private bool ApplyDesiredGestures(
        IReadOnlyDictionary<GlobalShortcutAction, GlobalShortcutGesture> desired,
        IDictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult> results,
        bool bestEffort)
    {
        UnregisterAll();

        if (!IsSupported)
        {
            ApplyUnsupportedResults(desired);
            return true;
        }

        var allSucceeded = true;
        foreach (var action in Enum.GetValues<GlobalShortcutAction>())
        {
            if (!desired.TryGetValue(action, out var gesture))
            {
                results[action] = new GlobalShortcutRegistrationResult(false);
                _currentResults[action] = results[action];
                continue;
            }

            if (TryRegisterPlatformShortcut(action, gesture, out var errorMessage))
            {
                _activeGestures[action] = gesture;
                results[action] = new GlobalShortcutRegistrationResult(true);
                _currentResults[action] = results[action];
                continue;
            }

            allSucceeded = false;
            var result = new GlobalShortcutRegistrationResult(false, errorMessage,
                GlobalShortcutRegistrationFailureKind.Conflict);
            results[action] = result;
            _currentResults[action] = result;

            if (!bestEffort)
                break;
        }

        NotifyRegistrationChanged();
        return allSucceeded;
    }

    private void ApplyUnsupportedResults(IReadOnlyDictionary<GlobalShortcutAction, GlobalShortcutGesture> desired)
    {
        _activeGestures.Clear();
        foreach (var action in Enum.GetValues<GlobalShortcutAction>())
            _currentResults[action] = desired.ContainsKey(action)
                ? new GlobalShortcutRegistrationResult(false, "当前平台暂不支持全局快捷键",
                    GlobalShortcutRegistrationFailureKind.UnsupportedPlatform)
                : new GlobalShortcutRegistrationResult(false);

        NotifyRegistrationChanged();
    }

    private Dictionary<GlobalShortcutAction, GlobalShortcutGesture> ParseSettings(
        GlobalShortcutSettings settings,
        out Dictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult> results)
    {
        results = CreateEmptyResults();
        var desired = new Dictionary<GlobalShortcutAction, GlobalShortcutGesture>();
        if (!settings.EnableGlobalShortcuts)
            return desired;

        foreach (var action in Enum.GetValues<GlobalShortcutAction>())
        {
            var text = settings.GetShortcut(action);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (!GlobalShortcutParser.TryParse(text, out var gesture))
            {
                results[action] = new GlobalShortcutRegistrationResult(false, "快捷键格式无效",
                    GlobalShortcutRegistrationFailureKind.InvalidGesture);
                continue;
            }

            desired[action] = gesture;
        }

        return desired;
    }

    private void NotifyRegistrationChanged()
    {
        RegistrationChanged?.Invoke(CurrentResults);
    }

    private static Dictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult> CreateEmptyResults()
    {
        var results = new Dictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult>();
        foreach (var action in Enum.GetValues<GlobalShortcutAction>())
            results[action] = new GlobalShortcutRegistrationResult(false);
        return results;
    }

    private void DispatchAction(GlobalShortcutAction action)
    {
        uiDispatcher.RunOrPost(() =>
        {
            switch (action)
            {
                case GlobalShortcutAction.PlayPause:
                    if (playerViewModel.TogglePlayPauseCommand.CanExecute(null))
                        playerViewModel.TogglePlayPauseCommand.Execute(null);
                    break;
                case GlobalShortcutAction.PreviousTrack:
                    if (playerViewModel.PlayPreviousCommand.CanExecute(null))
                        playerViewModel.PlayPreviousCommand.Execute(null);
                    break;
                case GlobalShortcutAction.NextTrack:
                    if (playerViewModel.PlayNextCommand.CanExecute(null))
                        playerViewModel.PlayNextCommand.Execute(null);
                    break;
                case GlobalShortcutAction.ShowMainWindow:
                    mainWindowService.ShowMainWindow();
                    break;
                case GlobalShortcutAction.ToggleDesktopLyric:
                    desktopLyricWindowService.Toggle();
                    break;
            }
        });
    }

    private partial bool GetPlatformSupport();
    private partial void InitializePlatform(Window mainWindow);

    private partial bool TryRegisterPlatformShortcut(GlobalShortcutAction action, GlobalShortcutGesture gesture,
        out string? errorMessage);

    private partial void UnregisterPlatformShortcut(GlobalShortcutAction action);
}
