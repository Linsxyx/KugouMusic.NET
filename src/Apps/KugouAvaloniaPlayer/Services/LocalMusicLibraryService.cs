using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KugouAvaloniaPlayer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TagLib;
using File = TagLib.File;

namespace KugouAvaloniaPlayer.Services;

public interface ILocalMusicLibraryService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalPlaylistSummary>> GetPlaylistsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalTrackItem>> GetPlaylistTracksAsync(long playlistId, CancellationToken cancellationToken = default);
    Task<LocalPlaylistSummary> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default);
    Task<LocalPlaylistSummary> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task AddFilesToPlaylistAsync(long playlistId, IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
    Task UpdatePlaylistAsync(long playlistId, string name, string? coverPath, CancellationToken cancellationToken = default);
    Task DeletePlaylistAsync(long playlistId, CancellationToken cancellationToken = default);
    Task SetTrackCoverAsync(long trackId, string coverPath, CancellationToken cancellationToken = default);
}

public sealed record LocalPlaylistSummary(
    long Id,
    string Name,
    string? CoverPath,
    int TrackCount,
    DateTime UpdatedAt);

public sealed record LocalTrackItem(
    long Id,
    string Title,
    string Artist,
    string Album,
    double DurationSeconds,
    string LocalPath,
    string? CoverPath);

public sealed class LocalMusicLibraryService(ILogger<LocalMusicLibraryService> logger) : ILocalMusicLibraryService
{
    private const string PlaylistKindManual = "Manual";
    private const string PlaylistKindImportedFolder = "ImportedFolder";
    private const string SourceTypeLocalFile = "LocalFile";

    private static readonly string LocalSongCoverCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kugou",
        "local-song-covers");

    private static readonly HashSet<string> SupportedLocalAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".flac",
        ".wav",
        ".ogg",
        ".m4a",
        ".aac",
        ".webm",
        ".dsf",
        ".dff"
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            AppDbContext.EnsureDatabaseCreated();
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }

        await ImportLegacyJsonAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LocalPlaylistSummary>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var db = AppDbContext.Create();

        return await db.LocalPlaylists
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new LocalPlaylistSummary(
                x.Id,
                x.Name,
                x.CoverPath,
                x.Tracks.Count,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LocalTrackItem>> GetPlaylistTracksAsync(long playlistId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var db = AppDbContext.Create();

        return await db.LocalPlaylistTracks
            .AsNoTracking()
            .Where(x => x.PlaylistId == playlistId)
            .OrderBy(x => x.Position)
            .ThenBy(x => x.AddedAtUtc)
            .ThenBy(x => x.TrackId)
            .Select(x => new LocalTrackItem(
                x.Track.Id,
                x.Track.Title,
                x.Track.Artist,
                x.Track.Album,
                x.Track.DurationSeconds,
                x.Track.LocalPath,
                x.Track.CoverPath))
            .ToListAsync(cancellationToken);
    }

    public async Task<LocalPlaylistSummary> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var db = AppDbContext.Create();
        var playlist = new LocalPlaylistEntity
        {
            Name = string.IsNullOrWhiteSpace(name) ? "新建本地歌单" : name.Trim(),
            Kind = PlaylistKindManual,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.LocalPlaylists.Add(playlist);
        await db.SaveChangesAsync(cancellationToken);
        return await GetPlaylistSummaryAsync(db, playlist.Id, cancellationToken);
    }

    public async Task<LocalPlaylistSummary> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);

        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(IsSupportedAudioFile)
            .ToList();

        await using var db = AppDbContext.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var playlist = await db.LocalPlaylists
            .Include(x => x.Tracks)
            .FirstOrDefaultAsync(
                x => x.Kind == PlaylistKindImportedFolder && x.SourceRef == folderPath,
                cancellationToken);

        if (playlist == null)
        {
            playlist = new LocalPlaylistEntity
            {
                Name = new DirectoryInfo(folderPath).Name,
                Kind = PlaylistKindImportedFolder,
                SourceRef = folderPath,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.LocalPlaylists.Add(playlist);
            await db.SaveChangesAsync(cancellationToken);
        }

        await ReplacePlaylistTracksAsync(db, playlist, files, null, cancellationToken);
        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetPlaylistSummaryAsync(db, playlist.Id, cancellationToken);
    }

    public async Task AddFilesToPlaylistAsync(long playlistId, IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var files = filePaths.Where(IsSupportedAudioFile).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
            return;

        await using var db = AppDbContext.Create();
        var playlist = await db.LocalPlaylists
            .Include(x => x.Tracks)
            .FirstOrDefaultAsync(x => x.Id == playlistId, cancellationToken);
        if (playlist == null)
            return;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var position = playlist.Tracks.Count == 0 ? 0 : playlist.Tracks.Max(x => x.Position) + 1;
        for (var i = 0; i < files.Count; i++)
        {
            var track = await UpsertTrackAsync(db, files[i], null, cancellationToken);
            if (playlist.Tracks.Any(x => x.TrackId == track.Id))
                continue;

            playlist.Tracks.Add(new LocalPlaylistTrackEntity
            {
                PlaylistId = playlist.Id,
                TrackId = track.Id,
                Position = position + i,
                AddedAtUtc = DateTime.UtcNow
            });
        }

        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdatePlaylistAsync(long playlistId, string name, string? coverPath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var db = AppDbContext.Create();
        var playlist = await db.LocalPlaylists.FirstOrDefaultAsync(x => x.Id == playlistId, cancellationToken);
        if (playlist == null)
            return;

        playlist.Name = string.IsNullOrWhiteSpace(name) ? "本地歌单" : name.Trim();
        playlist.CoverPath = string.IsNullOrWhiteSpace(coverPath) ? null : coverPath;
        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeletePlaylistAsync(long playlistId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var db = AppDbContext.Create();
        var playlist = await db.LocalPlaylists.FirstOrDefaultAsync(x => x.Id == playlistId, cancellationToken);
        if (playlist == null)
            return;

        db.LocalPlaylists.Remove(playlist);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTrackCoverAsync(long trackId, string coverPath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var db = AppDbContext.Create();
        var track = await db.LocalTracks.FirstOrDefaultAsync(x => x.Id == trackId, cancellationToken);
        if (track == null)
            return;

        track.CoverPath = coverPath;
        track.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ImportLegacyJsonAsync(CancellationToken cancellationToken)
    {
        SettingsManager.Settings.LocalMusicFolders ??= new List<string>();
        SettingsManager.Settings.LocalPlaylistMetas ??= new Dictionary<string, LocalPlaylistMeta>();

        var folders = SettingsManager.Settings.LocalMusicFolders.ToList();
        if (folders.Count == 0)
            return;

        var migrated = false;
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(folder))
                continue;

            try
            {
                var meta = SettingsManager.Settings.LocalPlaylistMetas.TryGetValue(folder, out var value) ? value : null;
                await ImportLegacyFolderAsync(folder, meta, cancellationToken);
                SettingsManager.Settings.LocalMusicFolders.Remove(folder);
                SettingsManager.Settings.LocalPlaylistMetas.Remove(folder);
                migrated = true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or CorruptFileException or UnsupportedFormatException)
            {
                logger.LogWarning(ex, "迁移旧本地歌单失败，保留旧 JSON 条目。 folder={Folder}", folder);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "迁移旧本地歌单发生异常，保留旧 JSON 条目。 folder={Folder}", folder);
            }
        }

        if (migrated)
            SettingsManager.Save();
    }

    private async Task ImportLegacyFolderAsync(string folderPath, LocalPlaylistMeta? meta, CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(IsSupportedAudioFile)
            .ToList();

        await using var db = AppDbContext.Create();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var playlist = await db.LocalPlaylists
            .Include(x => x.Tracks)
            .FirstOrDefaultAsync(
                x => x.Kind == PlaylistKindImportedFolder && x.SourceRef == folderPath,
                cancellationToken);

        if (playlist == null)
        {
            playlist = new LocalPlaylistEntity
            {
                Name = string.IsNullOrWhiteSpace(meta?.Name) ? new DirectoryInfo(folderPath).Name : meta!.Name!,
                CoverPath = string.IsNullOrWhiteSpace(meta?.CoverPath) ? null : meta!.CoverPath,
                Kind = PlaylistKindImportedFolder,
                SourceRef = folderPath,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.LocalPlaylists.Add(playlist);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(meta?.Name))
                playlist.Name = meta!.Name!;
            if (!string.IsNullOrWhiteSpace(meta?.CoverPath))
                playlist.CoverPath = meta!.CoverPath;
        }

        await ReplacePlaylistTracksAsync(db, playlist, files, meta?.SongCoverPaths, cancellationToken);
        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReplacePlaylistTracksAsync(
        AppDbContext db,
        LocalPlaylistEntity playlist,
        IReadOnlyList<string> filePaths,
        IReadOnlyDictionary<string, string>? legacySongCovers,
        CancellationToken cancellationToken)
    {
        var existingLinks = await db.LocalPlaylistTracks
            .Where(x => x.PlaylistId == playlist.Id)
            .ToListAsync(cancellationToken);
        db.LocalPlaylistTracks.RemoveRange(existingLinks);
        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < filePaths.Count; i++)
        {
            var track = await UpsertTrackAsync(db, filePaths[i], GetLegacyCover(filePaths[i], legacySongCovers), cancellationToken);
            db.LocalPlaylistTracks.Add(new LocalPlaylistTrackEntity
            {
                PlaylistId = playlist.Id,
                TrackId = track.Id,
                Position = i,
                AddedAtUtc = DateTime.UtcNow
            });
        }
    }

    private async Task<LocalTrackEntity> UpsertTrackAsync(
        AppDbContext db,
        string filePath,
        string? explicitCoverPath,
        CancellationToken cancellationToken)
    {
        var metadata = ReadMetadata(filePath, explicitCoverPath);
        var now = DateTime.UtcNow;
        var lastWriteTime = System.IO.File.GetLastWriteTimeUtc(filePath);
        var fileSize = new FileInfo(filePath).Length;
        var track = await db.LocalTracks.FirstOrDefaultAsync(x => x.LocalPath == filePath, cancellationToken);

        if (track == null)
        {
            track = new LocalTrackEntity
            {
                SourceType = SourceTypeLocalFile,
                LocalPath = filePath,
                CreatedAtUtc = now
            };
            db.LocalTracks.Add(track);
        }

        track.Title = metadata.Title;
        track.Artist = metadata.Artist;
        track.Album = metadata.Album;
        track.DurationSeconds = metadata.DurationSeconds;
        track.CoverPath = string.IsNullOrWhiteSpace(metadata.CoverPath) ? track.CoverPath : metadata.CoverPath;
        track.FileSize = fileSize;
        track.LastWriteTimeUtc = lastWriteTime;
        track.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return track;
    }

    private static async Task<LocalPlaylistSummary> GetPlaylistSummaryAsync(
        AppDbContext db,
        long playlistId,
        CancellationToken cancellationToken)
    {
        var item = await db.LocalPlaylists
            .AsNoTracking()
            .Where(x => x.Id == playlistId)
            .Select(x => new LocalPlaylistSummary(
                x.Id,
                x.Name,
                x.CoverPath,
                x.Tracks.Count,
                x.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return item ?? throw new InvalidOperationException($"Local playlist not found: {playlistId}");
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await InitializeAsync(cancellationToken);
    }

    private LocalTrackMetadata ReadMetadata(string filePath, string? explicitCoverPath)
    {
        var title = Path.GetFileNameWithoutExtension(filePath);
        var artist = "未知艺术家";
        var album = "";
        double duration = 0;
        var coverPath = IsUsableFile(explicitCoverPath) ? explicitCoverPath : null;

        try
        {
            using var tfile = File.Create(filePath);
            title = string.IsNullOrWhiteSpace(tfile.Tag.Title) ? title : tfile.Tag.Title;

            var artists = tfile.Tag.Performers;
            if (artists is { Length: > 0 })
                artist = string.Join(", ", artists.Where(x => !string.IsNullOrWhiteSpace(x)));

            album = tfile.Tag.Album ?? "";
            duration = tfile.Properties?.Duration.TotalSeconds ?? 0;
            coverPath ??= GetEmbeddedSongCoverSource(filePath, tfile);
        }
        catch (UnsupportedFormatException)
        {
            logger.LogWarning("文件格式不支持，已降级为文件名加载 [{File}]", filePath);
        }
        catch (CorruptFileException ex)
        {
            logger.LogWarning("文件头伪装或损坏，已降级为文件名加载 [{File}]: {Message}", filePath, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning("读取标签失败，已降级为文件名加载 [{File}]: {Message}", filePath, ex.Message);
        }

        return new LocalTrackMetadata(title, artist, album, duration, coverPath);
    }

    private static string? GetLegacyCover(string filePath, IReadOnlyDictionary<string, string>? legacySongCovers)
    {
        if (legacySongCovers == null)
            return null;

        return legacySongCovers.TryGetValue(Path.GetFileName(filePath), out var coverPath) && IsUsableFile(coverPath)
            ? coverPath
            : null;
    }

    private static bool IsSupportedAudioFile(string filePath)
    {
        return System.IO.File.Exists(filePath) && SupportedLocalAudioExtensions.Contains(Path.GetExtension(filePath));
    }

    private static bool IsUsableFile(string? filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) && System.IO.File.Exists(filePath);
    }

    private static string? GetEmbeddedSongCoverSource(string songPath, File tagFile)
    {
        var picture = tagFile.Tag.Pictures?
            .FirstOrDefault(x => x.Type == PictureType.FrontCover && x.Data.Count > 0)
            ?? tagFile.Tag.Pictures?.FirstOrDefault(x => x.Data.Count > 0);

        if (picture == null)
            return null;

        try
        {
            Directory.CreateDirectory(LocalSongCoverCacheDirectory);

            var extension = GetPictureExtension(picture.MimeType);
            var cacheKey = GetStableHash($"{songPath}|{System.IO.File.GetLastWriteTimeUtc(songPath).Ticks}|{picture.Data.Count}");
            var cachePath = Path.Combine(LocalSongCoverCacheDirectory, $"{cacheKey}{extension}");

            if (!System.IO.File.Exists(cachePath))
                System.IO.File.WriteAllBytes(cachePath, picture.Data.Data);

            return cachePath;
        }
        catch
        {
            return null;
        }
    }

    private static string GetPictureExtension(string? mimeType)
    {
        return mimeType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
    }

    private static string GetStableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record LocalTrackMetadata(
        string Title,
        string Artist,
        string Album,
        double DurationSeconds,
        string? CoverPath);
}
