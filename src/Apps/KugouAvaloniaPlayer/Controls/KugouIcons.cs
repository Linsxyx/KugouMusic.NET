using Avalonia.Media;

namespace KugouAvaloniaPlayer.Controls;

/// <summary>
/// Precompiled vector icon geometries replacing SukiUI.Content.Icons.
/// </summary>
public static class KugouIcons
{
    public static Geometry ChevronLeft { get; } = StreamGeometry.Parse(
        "M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z");

    public static Geometry ChevronRight { get; } = StreamGeometry.Parse(
        "M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z");

    public static Geometry ChevronDown { get; } = StreamGeometry.Parse(
        "M7.41 8.59L12 13.17l4.59-4.58L18 10l-6 6-6-6 1.41-1.41z");

    public static Geometry Search { get; } = StreamGeometry.Parse(
        "M15.5 14h-.79l-.28-.27A6.471 6.471 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z");

    public static Geometry Refresh { get; } = StreamGeometry.Parse(
        "M17.65 6.35A7.958 7.958 0 0 0 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08A5.99 5.99 0 0 1 12 18c-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z");

    public static Geometry Star { get; } = StreamGeometry.Parse(
        "M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z");

    public static Geometry Cross { get; } = StreamGeometry.Parse(
        "M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z");

    public static Geometry WindowClose { get; } = StreamGeometry.Parse(
        "M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z");

    public static Geometry WindowMinimize { get; } = StreamGeometry.Parse(
        "M4 11h16v2H4z");

    public static Geometry WindowMaximize { get; } = StreamGeometry.Parse(
        "M4 4h16v16H4V4zm2 2v12h12V6H6z");

    public static Geometry WindowRestore { get; } = StreamGeometry.Parse(
        "M8 4h12v12h-2V6H8V4zM4 8h12v12H4V8zm2 2v8h8v-8H6z");

    public static Geometry WindowFullScreen { get; } = StreamGeometry.Parse(
        "M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z");

    public static Geometry WindowFullScreenOff { get; } = StreamGeometry.Parse(
        "M5 16h3v3h2v-5H5v2zm3-8H5v2h5V5H8v3zm6 11h2v-3h3v-2h-5v5zm2-11V5h-2v5h5V8h-3z");

    public static Geometry Logout { get; } = StreamGeometry.Parse(
        "M17 7l-1.41 1.41L18.17 11H8v2h10.17l-2.58 2.58L17 17l5-5zM4 5h8V3H4c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h8v-2H4V5z");

    public static Geometry NavDailyRecommend { get; } = StreamGeometry.Parse(
        "M3.24 6.15C2.51 6.43 2 7.17 2 8v12a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V8c0-1.11-.89-2-2-2H8.3l8.26-3.34L15.88 1 3.24 6.15zM7 20c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm13-8h-2v-2h-2v2H4V8h16v4z");

    public static Geometry NavHistory { get; } = StreamGeometry.Parse(
        "M13 3a9 9 0 0 0-9 9H1l3.89 3.89.07.14L9 12H6c0-3.87 3.13-7 7-7s7 3.13 7 7-3.13 7-7 7c-1.93 0-3.68-.79-4.94-2.06l-1.42 1.42A8.954 8.954 0 0 0 13 21a9 9 0 0 0 0-18zm-1 5v5l4.28 2.54.72-1.21-3.5-2.08V8H12z");

    public static Geometry NavLocalLibrary { get; } = StreamGeometry.Parse(
        "M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-5 3v6.5a2.5 2.5 0 1 1-1.5-2.29V10h3.5v1.5H15z");

    public static Geometry NavDiscover { get; } = StreamGeometry.Parse(
        "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm2.19 12.19L6 18l3.81-8.19L18 6l-3.81 8.19zM12 10.9c-.61 0-1.1.49-1.1 1.1s.49 1.1 1.1 1.1 1.1-.49 1.1-1.1-.49-1.1-1.1-1.1z");

    public static Geometry NavCloud { get; } = StreamGeometry.Parse(
        "M19.35 10.04A7.49 7.49 0 0 0 12 4C9.11 4 6.6 5.64 5.35 8.04A5.994 5.994 0 0 0 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM17 13l-5 5-5-5h3V9h4v4h3z");

    public static Geometry NavMusic { get; } = StreamGeometry.Parse(
        "M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z");

    public static Geometry Play { get; } = StreamGeometry.Parse(
        "M8 5.14v14l11-7-11-7z");

    public static Geometry Pause { get; } = StreamGeometry.Parse(
        "M6 5h4v14H6V5zm8 0h4v14h-4V5z");

    public static Geometry SkipPrevious { get; } = StreamGeometry.Parse(
        "M6 6h2v12H6V6zm3.5 6l8.5 6V6l-8.5 6z");

    public static Geometry SkipNext { get; } = StreamGeometry.Parse(
        "M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z");

    public static Geometry QueueNext { get; } = StreamGeometry.Parse(
        "M3 10h11v2H3v-2zm0-4h11v2H3V6zm0 8h7v2H3v-2zm13-1v8l6-4-6-4z");

    public static Geometry PlaylistAdd { get; } = StreamGeometry.Parse(
        "M14 10H3v2h11v-2zm0-4H3v2h11V6zm4 8v-4h-2v4h-4v2h4v4h2v-4h4v-2h-4zM3 16h7v-2H3v2z");

    public static Geometry Singer { get; } = StreamGeometry.Parse(
        "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z");

    public static Geometry Edit { get; } = StreamGeometry.Parse(
        "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04a.996.996 0 0 0 0-1.41l-2.34-2.34a.996.996 0 0 0-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z");

    public static Geometry Trash { get; } = StreamGeometry.Parse(
        "M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z");

    public static Geometry Image { get; } = StreamGeometry.Parse(
        "M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z");

    public static Geometry Lyrics { get; } = StreamGeometry.Parse(
        "M4 6h16v2H4V6zm0 5h16v2H4v-2zm0 5h10v2H4v-2z");

    public static Geometry Link { get; } = StreamGeometry.Parse(
        "M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z");

    public static Geometry Home { get; } = StreamGeometry.Parse(
        "M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8h5z");

    public static Geometry ResolveNavIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
            return NavMusic;

        if (iconPath.Contains("Radio", System.StringComparison.OrdinalIgnoreCase))
            return NavDailyRecommend;
        if (iconPath.Contains("history", System.StringComparison.OrdinalIgnoreCase))
            return NavHistory;
        if (iconPath.Contains("folder", System.StringComparison.OrdinalIgnoreCase))
            return NavLocalLibrary;
        if (iconPath.Contains("tag", System.StringComparison.OrdinalIgnoreCase))
            return NavDiscover;
        if (iconPath.Contains("cloud", System.StringComparison.OrdinalIgnoreCase))
            return NavCloud;

        return NavMusic;
    }
}
