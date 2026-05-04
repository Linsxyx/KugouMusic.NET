using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace KugouAvaloniaPlayer.Models;

[Flags]
public enum GlobalShortcutModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8
}

public enum GlobalShortcutRegistrationFailureKind
{
    None,
    InvalidGesture,
    Conflict,
    UnsupportedPlatform,
    PlatformError
}

public readonly record struct GlobalShortcutGesture(GlobalShortcutModifiers Modifiers, Key Key);

public sealed record GlobalShortcutRegistrationResult(
    bool IsRegistered,
    string? ErrorMessage = null,
    GlobalShortcutRegistrationFailureKind FailureKind = GlobalShortcutRegistrationFailureKind.None);

public sealed record GlobalShortcutApplyResult(
    bool Success,
    IReadOnlyDictionary<GlobalShortcutAction, GlobalShortcutRegistrationResult> Results);