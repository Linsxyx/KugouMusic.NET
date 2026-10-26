using System.Collections.Generic;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services.LyricsParsing;

internal interface ILyricParser
{
    string Extension { get; }

    List<LyricLineViewModel> Parse(string content);
}
