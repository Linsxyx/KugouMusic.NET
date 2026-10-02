namespace KugouAvaloniaPlayer.Models;

public sealed record SettingsCategory(string Id, string Title, string Symbol)
{
    public override string ToString() => Title;
}
public static class SettingsCatalog
{
    public static SettingsCategory[] Categories { get; } =
    [
        new("appearance", "外观", "◐"),
        new("playback", "播放与音效", "♫"),
        new("lyrics", "歌词与播放画面", "≋"),
        new("shortcuts", "快捷键", "⌘"),
        new("general", "通用", "⚙"),
        new("about", "更新与关于", "↗"),
        new("account", "账户", "○")
    ];
}
