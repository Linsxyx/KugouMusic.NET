using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Converters;

public class PlaylistSelectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PlaylistItem selected || parameter is not PlaylistItem candidate)
            return false;

        if (selected.Type != candidate.Type)
            return false;

        return selected.Type switch
        {
            PlaylistType.Local => SameText(selected.LocalPath, candidate.LocalPath),
            PlaylistType.Online or PlaylistType.Album => SameOnlinePlaylist(selected, candidate),
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }

    private static bool SameOnlinePlaylist(PlaylistItem selected, PlaylistItem candidate)
    {
        if (selected.ListId > 0 && candidate.ListId > 0)
            return selected.ListId == candidate.ListId;

        return SameText(selected.Id, candidate.Id);
    }

    private static bool SameText(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
