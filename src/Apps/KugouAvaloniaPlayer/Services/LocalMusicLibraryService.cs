using System;
using System.Collections.Generic;
using System.IO;
using ZLinq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ATL;
using Avalonia.Media.Imaging;
using KugouAvaloniaPlayer.Converters;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services.Jellyfin;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public interface ILocalMusicLibraryService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalPlaylistSummary>> GetPlaylistsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalTrackItem>> GetPlaylistTracksAsync(long playlistId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalTrackSearchResult>> SearchTracksAsync(
        string keyword,
        CancellationToken cancellationToken = default);
    Task<LocalPlaylistSummary> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default);
    Task<LocalPlaylistSummary> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalPlaylistSummary>> ImportJellyfinLibraryAsync(
        JellyfinConnectionOptions options,
        JellyfinLibrary library,
        IProgress<JellyfinImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalPlaylistSummary>> RefreshImportedLibrariesAsync(CancellationToken cancellationToken = default);
    Task AddFilesToPlaylistAsync(long playlistId, IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
    Task RemoveTrackFromPlaylistAsync(long playlistId, long trackId, CancellationToken cancellationToken = default);
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
    string SourceType,
    string LocalPath,
    string? CoverPath,
    string? RemoteUrl);

public sealed record LocalTrackSearchResult(
    LocalTrackItem Track,
    long PlaylistId,
    string PlaylistName,
    int PlaylistPosition);

public sealed class LocalMusicLibraryService(
    IJellyfinClient jellyfinClient,
    ILogger<LocalMusicLibraryService> logger) : ILocalMusicLibraryService
{
    private const string PlaylistKindManual = "Manual";
    private const string PlaylistKindImportedFolder = "ImportedFolder";
    private const string PlaylistKindJellyfinLibrary = "JellyfinLibrary";
    private const string SourceTypeLocalFile = "LocalFile";
    private const int LocalImportBatchSize = 32;
    private const string EmbeddedCoverCacheVersion = "thumb-v1";
    private const int EmbeddedCoverThumbnailWidth = 512;
    private const int EmbeddedCoverJpegQuality = 86;
    private const long MaxLocalSongCoverCacheBytes = 256L * 1024 * 1024;
    public const string SourceTypeJellyfin = "Jellyfin";

    private static readonly string LocalSongCoverCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kugou",
        "local-song-covers");

    private static readonly HashSet<string> SupportedLocalAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ape",
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

            AppDatabase.EnsureDatabaseCreated();
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
        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        return await GetPlaylistSummariesAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<LocalTrackItem>> GetPlaylistTracksAsync(long playlistId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.id, t.title, t.artist, t.album, t.duration_seconds, t.source_type, t.local_path, t.cover_path, t.remote_url
            FROM playlist_tracks pt
            INNER JOIN tracks t ON t.id = pt.track_id
            WHERE pt.playlist_id = $playlistId
            ORDER BY pt.position, pt.added_at, pt.track_id;
            """;
        command.Parameters.AddWithValue("$playlistId", playlistId);

        var tracks = new List<LocalTrackItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tracks.Add(ReadTrackItem(reader));

        return tracks;
    }

    public async Task<IReadOnlyList<LocalTrackSearchResult>> SearchTracksAsync(
        string keyword,
        CancellationToken cancellationToken = default)
    {
        var normalizedKeyword = keyword.Trim();
        if (normalizedKeyword.Length == 0)
            return [];

        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.id, t.title, t.artist, t.album, t.duration_seconds, t.source_type, t.local_path,
                   t.cover_path, t.remote_url, p.id AS playlist_id, p.name AS playlist_name,
                   pt.position AS playlist_position
            FROM playlist_tracks pt
            INNER JOIN tracks t ON t.id = pt.track_id
            INNER JOIN playlists p ON p.id = pt.playlist_id
            WHERE t.title LIKE $pattern ESCAPE '\'
               OR t.artist LIKE $pattern ESCAPE '\'
               OR t.album LIKE $pattern ESCAPE '\'
               OR p.name LIKE $pattern ESCAPE '\'
            ORDER BY
                CASE
                    WHEN t.title = $keyword COLLATE NOCASE THEN 0
                    WHEN t.title LIKE $prefix ESCAPE '\' THEN 1
                    ELSE 2
                END,
                t.title COLLATE NOCASE,
                p.name COLLATE NOCASE,
                pt.position,
                t.id;
            """;

        var escapedKeyword = EscapeLikePattern(normalizedKeyword);
        command.Parameters.AddWithValue("$keyword", normalizedKeyword);
        command.Parameters.AddWithValue("$prefix", escapedKeyword + "%");
        command.Parameters.AddWithValue("$pattern", "%" + escapedKeyword + "%");

        var results = new List<LocalTrackSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new LocalTrackSearchResult(
                ReadTrackItem(reader),
                reader.GetInt64(reader.GetOrdinal("playlist_id")),
                reader.GetString(reader.GetOrdinal("playlist_name")),
                reader.GetInt32(reader.GetOrdinal("playlist_position"))));
        }

        return results;
    }

    public async Task<LocalPlaylistSummary> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var playlist = new LocalPlaylistEntity
        {
            Name = string.IsNullOrWhiteSpace(name) ? "新建本地歌单" : name.Trim(),
            Kind = PlaylistKindManual,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        playlist.Id = await InsertPlaylistAsync(connection, null, playlist, cancellationToken);
        return await GetPlaylistSummaryAsync(connection, null, playlist.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<LocalPlaylistSummary>> ImportJellyfinLibraryAsync(
        JellyfinConnectionOptions options,
        JellyfinLibrary library,
        IProgress<JellyfinImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var serverFingerprint = jellyfinClient.GetServerFingerprint(options.ServerUrl);
        var audioItems = await jellyfinClient.GetAudioItemsAsync(options, library.Id, progress, cancellationToken);
        var albumGroups = audioItems
            .AsValueEnumerable().GroupBy(GetJellyfinAlbumKey)
            .Select(x => new JellyfinAlbumGroup(
                x.Key,
                GetJellyfinAlbumName(x.AsValueEnumerable().First()),
                x.AsValueEnumerable().ToList()))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var legacyLibrarySourceRef = serverFingerprint + ":" + library.Id;
        var legacyLibraryPlaylist = await FindPlaylistAsync(
            connection,
            transaction,
            PlaylistKindJellyfinLibrary,
            legacyLibrarySourceRef,
            cancellationToken);
        if (legacyLibraryPlaylist != null)
        {
            await DeletePlaylistAsync(connection, transaction, legacyLibraryPlaylist.Id, cancellationToken);
        }

        var importedPlaylistIds = new List<long>();
        var importedSourceRefs = new HashSet<string>(StringComparer.Ordinal);
        var processedSongs = 0;
        foreach (var albumGroup in albumGroups)
        {
            var sourceRef = serverFingerprint + ":" + library.Id + ":" + albumGroup.Key;
            importedSourceRefs.Add(sourceRef);
            var playlist = await FindPlaylistAsync(
                connection,
                transaction,
                PlaylistKindJellyfinLibrary,
                sourceRef,
                cancellationToken);

            if (playlist == null)
            {
                var now = DateTime.UtcNow;
                playlist = new LocalPlaylistEntity
                {
                    Name = albumGroup.Name,
                    Kind = PlaylistKindJellyfinLibrary,
                    SourceRef = sourceRef,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                playlist.Id = await InsertPlaylistAsync(connection, transaction, playlist, cancellationToken);
            }
            else
            {
                playlist.Name = albumGroup.Name;
                playlist.UpdatedAtUtc = DateTime.UtcNow;
                await UpdatePlaylistAsync(connection, transaction, playlist, cancellationToken);
            }

            await DeletePlaylistTracksAsync(connection, transaction, playlist.Id, cancellationToken);

            for (var i = 0; i < albumGroup.Items.Count; i++)
            {
                var track = await UpsertJellyfinTrackAsync(
                    connection,
                    transaction,
                    serverFingerprint,
                    albumGroup.Items[i],
                    cancellationToken);
                await InsertPlaylistTrackAsync(connection, transaction, playlist.Id, track.Id, i, DateTime.UtcNow, cancellationToken);

                processedSongs++;
                progress?.Report(new JellyfinImportProgress
                {
                    Processed = processedSongs,
                    Total = audioItems.Count,
                    Message = $"正在按专辑写入本地音乐库 {processedSongs}/{audioItems.Count}"
                });
            }

            playlist.CoverPath = albumGroup.Items.AsValueEnumerable().FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.CoverUrl))?.CoverUrl;
            playlist.UpdatedAtUtc = DateTime.UtcNow;
            await UpdatePlaylistAsync(connection, transaction, playlist, cancellationToken);
            importedPlaylistIds.Add(playlist.Id);
        }

        var stalePlaylistIds = await GetPlaylistIdsBySourceRefPrefixAsync(
            connection,
            transaction,
            PlaylistKindJellyfinLibrary,
            serverFingerprint + ":" + library.Id + ":",
            importedSourceRefs,
            cancellationToken);
        foreach (var stalePlaylistId in stalePlaylistIds)
            await DeletePlaylistAsync(connection, transaction, stalePlaylistId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var summaries = new List<LocalPlaylistSummary>();
        foreach (var playlistId in importedPlaylistIds)
            summaries.Add(await GetPlaylistSummaryAsync(connection, null, playlistId, cancellationToken));

        return summaries;
    }

    public async Task<IReadOnlyList<LocalPlaylistSummary>> RefreshImportedLibrariesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        var folderPaths = await GetImportedFolderPathsAsync(connection, cancellationToken);
        var jellyfinLibraries = await GetImportedJellyfinLibrariesAsync(connection, cancellationToken);

        var refreshed = new List<LocalPlaylistSummary>();
        foreach (var folderPath in folderPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(folderPath))
            {
                logger.LogWarning("刷新本地音乐库时跳过不存在的文件夹。 folder={Folder}", folderPath);
                continue;
            }

            refreshed.Add(await ImportFolderAsync(folderPath, cancellationToken));
        }

        foreach (var library in jellyfinLibraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SettingsManager.Settings.JellyfinServers.TryGetValue(library.ServerFingerprint, out var settings))
            {
                logger.LogWarning(
                    "刷新 Jellyfin 音乐库时找不到服务器配置。 fingerprint={Fingerprint} libraryId={LibraryId}",
                    library.ServerFingerprint,
                    library.LibraryId);
                continue;
            }

            var options = new JellyfinConnectionOptions(settings.ServerUrl, settings.UserId, settings.ApiKey);
            refreshed.AddRange(await ImportJellyfinLibraryAsync(
                options,
                new JellyfinLibrary(library.LibraryId, "Jellyfin 音乐库"),
                null,
                cancellationToken));
        }

        return refreshed;
    }

    public async Task<LocalPlaylistSummary> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);

        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var playlist = await FindPlaylistAsync(
            connection,
            transaction,
            PlaylistKindImportedFolder,
            folderPath,
            cancellationToken);

        if (playlist == null)
        {
            var now = DateTime.UtcNow;
            playlist = new LocalPlaylistEntity
            {
                Name = new DirectoryInfo(folderPath).Name,
                Kind = PlaylistKindImportedFolder,
                SourceRef = folderPath,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
                playlist.Id = await InsertPlaylistAsync(connection, transaction, playlist, cancellationToken);
        }

        await ReplacePlaylistTracksFromFolderAsync(connection, transaction, playlist.Id, folderPath, null, cancellationToken);
        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await UpdatePlaylistAsync(connection, transaction, playlist, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetPlaylistSummaryAsync(connection, null, playlist.Id, cancellationToken);
    }

    public async Task AddFilesToPlaylistAsync(long playlistId, IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var files = filePaths.AsValueEnumerable().Where(IsSupportedAudioFile).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
            return;

        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        var playlist = await GetPlaylistAsync(connection, null, playlistId, cancellationToken);
        if (playlist == null)
            return;

        await using var transaction = connection.BeginTransaction();
        var existingTrackIds = await GetPlaylistTrackIdsAsync(connection, transaction, playlist.Id, cancellationToken);
        var position = await GetNextPlaylistPositionAsync(connection, transaction, playlist.Id, cancellationToken);
        for (var i = 0; i < files.Count; i++)
        {
            var track = await UpsertTrackAsync(connection, transaction, files[i], null, cancellationToken);
            if (existingTrackIds.Contains(track.Id))
                continue;

            await InsertPlaylistTrackAsync(connection, transaction, playlist.Id, track.Id, position + i, DateTime.UtcNow, cancellationToken);
            existingTrackIds.Add(track.Id);
        }

        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await UpdatePlaylistAsync(connection, transaction, playlist, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveTrackFromPlaylistAsync(long playlistId, long trackId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        var playlist = await GetPlaylistAsync(connection, null, playlistId, cancellationToken);
        if (playlist == null)
            return;

        await using var transaction = connection.BeginTransaction();
        var removed = await DeletePlaylistTrackAsync(connection, transaction, playlistId, trackId, cancellationToken);
        if (removed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await UpdatePlaylistAsync(connection, transaction, playlist, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdatePlaylistAsync(long playlistId, string name, string? coverPath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        var playlist = await GetPlaylistAsync(connection, null, playlistId, cancellationToken);
        if (playlist == null)
            return;

        playlist.Name = string.IsNullOrWhiteSpace(name) ? "本地歌单" : name.Trim();
        playlist.CoverPath = string.IsNullOrWhiteSpace(coverPath) ? null : coverPath;
        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await UpdatePlaylistAsync(connection, null, playlist, cancellationToken);
    }

    public async Task DeletePlaylistAsync(long playlistId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        var playlist = await GetPlaylistAsync(connection, null, playlistId, cancellationToken);
        if (playlist == null)
            return;

        await DeletePlaylistAsync(connection, null, playlist.Id, cancellationToken);
    }

    public async Task SetTrackCoverAsync(long trackId, string coverPath, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        var track = await GetTrackAsync(connection, null, trackId, cancellationToken);
        if (track == null)
            return;

        if (!string.Equals(track.SourceType, SourceTypeLocalFile, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(track.LocalPath) ||
            !System.IO.File.Exists(track.LocalPath))
        {
            throw new InvalidOperationException("只有本地音频文件支持写入嵌入封面。");
        }

        await WriteEmbeddedCoverAsync(track.LocalPath, coverPath, cancellationToken);

        track.CoverPath = LocalImageSourceHelper.BuildEmbeddedCoverSource(track.LocalPath);
        track.LastWriteTimeUtc = System.IO.File.GetLastWriteTimeUtc(track.LocalPath);
        track.UpdatedAtUtc = DateTime.UtcNow;
        await UpdateTrackAsync(connection, null, track, cancellationToken);
    }

    private async Task ImportLegacyJsonAsync(CancellationToken cancellationToken)
    {
        SettingsManager.Settings.LocalMusicFolders ??= new List<string>();
        SettingsManager.Settings.LocalPlaylistMetas ??= new Dictionary<string, LocalPlaylistMeta>();

        var folders = SettingsManager.Settings.LocalMusicFolders.AsValueEnumerable().ToList();
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
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
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
        await using var connection = await AppDatabase.CreateConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var playlist = await FindPlaylistAsync(
            connection,
            transaction,
            PlaylistKindImportedFolder,
            folderPath,
            cancellationToken);

        if (playlist == null)
        {
            var now = DateTime.UtcNow;
            playlist = new LocalPlaylistEntity
            {
                Name = string.IsNullOrWhiteSpace(meta?.Name) ? new DirectoryInfo(folderPath).Name : meta!.Name!,
                CoverPath = string.IsNullOrWhiteSpace(meta?.CoverPath) ? null : meta!.CoverPath,
                Kind = PlaylistKindImportedFolder,
                SourceRef = folderPath,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            playlist.Id = await InsertPlaylistAsync(connection, transaction, playlist, cancellationToken);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(meta?.Name))
                playlist.Name = meta!.Name!;
            if (!string.IsNullOrWhiteSpace(meta?.CoverPath))
                playlist.CoverPath = meta!.CoverPath;
        }

        await ReplacePlaylistTracksFromFolderAsync(connection, transaction, playlist.Id, folderPath, meta?.SongCoverPaths, cancellationToken);
        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await UpdatePlaylistAsync(connection, transaction, playlist, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReplacePlaylistTracksFromFolderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long playlistId,
        string folderPath,
        IReadOnlyDictionary<string, string>? legacySongCovers,
        CancellationToken cancellationToken)
    {
        await DeletePlaylistTracksAsync(connection, transaction, playlistId, cancellationToken);

        var channel = Channel.CreateBounded<IReadOnlyList<LocalTrackImportData>>(new BoundedChannelOptions(4)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                foreach (var fileBatch in EnumerateSupportedAudioFileBatches(folderPath, LocalImportBatchSize))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var importedBatch = await PrepareLocalTrackBatchAsync(fileBatch, legacySongCovers, cancellationToken);
                    await channel.Writer.WriteAsync(importedBatch, cancellationToken);
                }

                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
                throw;
            }
        }, cancellationToken);

        var position = 0;
        await foreach (var importedBatch in channel.Reader.ReadAllAsync(cancellationToken))
        {
            foreach (var importedTrack in importedBatch)
            {
                var track = await UpsertTrackAsync(connection, transaction, importedTrack, cancellationToken);
                await InsertPlaylistTrackAsync(connection, transaction, playlistId, track.Id, position++, DateTime.UtcNow, cancellationToken);
            }
        }

        await producer;
    }

    private async Task<LocalTrackEntity> UpsertTrackAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string filePath,
        string? explicitCoverPath,
        CancellationToken cancellationToken)
    {
        var metadata = ReadMetadata(filePath, explicitCoverPath);
        var now = DateTime.UtcNow;
        var lastWriteTime = System.IO.File.GetLastWriteTimeUtc(filePath);
        var fileSize = new FileInfo(filePath).Length;
        var track = await GetTrackByLocalPathAsync(connection, transaction, filePath, cancellationToken);

        if (track == null)
        {
            track = new LocalTrackEntity
            {
                SourceType = SourceTypeLocalFile,
                LocalPath = filePath,
                CreatedAtUtc = now
            };
        }

        track.Title = metadata.Title;
        track.Artist = metadata.Artist;
        track.Album = metadata.Album;
        track.DurationSeconds = metadata.DurationSeconds;
        track.CoverPath = string.IsNullOrWhiteSpace(metadata.CoverPath) ? track.CoverPath : metadata.CoverPath;
        track.RemoteUrl = null;
        track.FileSize = fileSize;
        track.LastWriteTimeUtc = lastWriteTime;
        track.UpdatedAtUtc = now;
        await UpsertTrackEntityAsync(connection, transaction, track, cancellationToken);
        return track;
    }

    private async Task<LocalTrackEntity> UpsertTrackAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalTrackImportData importedTrack,
        CancellationToken cancellationToken)
    {
        var metadata = importedTrack.Metadata;
        var now = DateTime.UtcNow;
        var track = await GetTrackByLocalPathAsync(connection, transaction, importedTrack.FilePath, cancellationToken);

        if (track == null)
        {
            track = new LocalTrackEntity
            {
                SourceType = SourceTypeLocalFile,
                LocalPath = importedTrack.FilePath,
                CreatedAtUtc = now
            };
        }

        track.Title = metadata.Title;
        track.Artist = metadata.Artist;
        track.Album = metadata.Album;
        track.DurationSeconds = metadata.DurationSeconds;
        track.CoverPath = string.IsNullOrWhiteSpace(metadata.CoverPath) ? track.CoverPath : metadata.CoverPath;
        track.RemoteUrl = null;
        track.FileSize = importedTrack.FileSize;
        track.LastWriteTimeUtc = importedTrack.LastWriteTimeUtc;
        track.UpdatedAtUtc = now;
        await UpsertTrackEntityAsync(connection, transaction, track, cancellationToken);
        return track;
    }

    private async Task<LocalTrackEntity> UpsertJellyfinTrackAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string serverFingerprint,
        JellyfinAudioItem item,
        CancellationToken cancellationToken)
    {
        var pseudoPath = jellyfinClient.BuildPseudoPath(serverFingerprint, item.Id);
        var now = DateTime.UtcNow;
        var track = await GetTrackByLocalPathAsync(connection, transaction, pseudoPath, cancellationToken);

        if (track == null)
        {
            track = new LocalTrackEntity
            {
                SourceType = SourceTypeJellyfin,
                LocalPath = pseudoPath,
                CreatedAtUtc = now
            };
        }

        track.SourceType = SourceTypeJellyfin;
        track.Title = string.IsNullOrWhiteSpace(item.Name) ? "未知歌曲" : item.Name;
        track.Artist = string.IsNullOrWhiteSpace(item.Artist) ? "未知艺术家" : item.Artist;
        track.Album = item.Album ?? string.Empty;
        track.DurationSeconds = item.DurationSeconds;
        track.CoverPath = string.IsNullOrWhiteSpace(item.CoverUrl) ? track.CoverPath : item.CoverUrl;
        track.RemoteUrl = string.IsNullOrWhiteSpace(item.StreamUrl) ? track.RemoteUrl : item.StreamUrl;
        track.FileSize = 0;
        track.LastWriteTimeUtc = now;
        track.UpdatedAtUtc = now;
        await UpsertTrackEntityAsync(connection, transaction, track, cancellationToken);
        return track;
    }

    private static string GetJellyfinAlbumKey(JellyfinAudioItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.AlbumId))
            return $"album:{item.AlbumId}";

        if (!string.IsNullOrWhiteSpace(item.Album))
            return $"name:{GetStableHash(item.Album.Trim().ToUpperInvariant())}";

        return "unknown";
    }

    private static string GetJellyfinAlbumName(JellyfinAudioItem item)
    {
        return string.IsNullOrWhiteSpace(item.Album) ? "未分专辑" : item.Album.Trim();
    }

    private static async Task<LocalPlaylistSummary> GetPlaylistSummaryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long playlistId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT p.id, p.name, p.cover_path, p.updated_at, COUNT(pt.track_id) AS track_count
            FROM playlists p
            LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
            WHERE p.id = $playlistId
            GROUP BY p.id, p.name, p.cover_path, p.updated_at
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$playlistId", playlistId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            return ReadPlaylistSummary(reader);

        throw new InvalidOperationException($"Local playlist not found: {playlistId}");
    }

    private static async Task<List<LocalPlaylistSummary>> GetPlaylistSummariesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.id, p.name, p.cover_path, p.updated_at, COUNT(pt.track_id) AS track_count
            FROM playlists p
            LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
            GROUP BY p.id, p.name, p.cover_path, p.updated_at
            ORDER BY p.updated_at DESC, p.id DESC;
            """;

        var playlists = new List<LocalPlaylistSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            playlists.Add(ReadPlaylistSummary(reader));

        return playlists;
    }

    private static async Task<List<string>> GetImportedFolderPathsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT source_ref
            FROM playlists
            WHERE kind = $kind AND source_ref IS NOT NULL AND source_ref <> ''
            ORDER BY source_ref;
            """;
        command.Parameters.AddWithValue("$kind", PlaylistKindImportedFolder);

        var folderPaths = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            folderPaths.Add(reader.GetString(0));

        return folderPaths;
    }

    private static async Task<List<JellyfinLibraryRef>> GetImportedJellyfinLibrariesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT source_ref
            FROM playlists
            WHERE kind = $kind AND source_ref IS NOT NULL AND source_ref <> ''
            ORDER BY source_ref;
            """;
        command.Parameters.AddWithValue("$kind", PlaylistKindJellyfinLibrary);

        var libraries = new HashSet<JellyfinLibraryRef>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sourceRef = reader.GetString(0);
            var parts = sourceRef.Split(':', 3);
            if (parts.Length >= 2 &&
                !string.IsNullOrWhiteSpace(parts[0]) &&
                !string.IsNullOrWhiteSpace(parts[1]))
            {
                libraries.Add(new JellyfinLibraryRef(parts[0], parts[1]));
            }
        }

        return libraries.AsValueEnumerable().ToList();
    }

    private static async Task<List<long>> GetPlaylistIdsBySourceRefPrefixAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string kind,
        string sourceRefPrefix,
        HashSet<string> exceptSourceRefs,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, source_ref
            FROM playlists
            WHERE kind = $kind AND source_ref LIKE $sourceRefPattern;
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$sourceRefPattern", sourceRefPrefix + "%");

        var playlistIds = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sourceRef = GetNullableString(reader, "source_ref");
            if (string.IsNullOrWhiteSpace(sourceRef) || !exceptSourceRefs.Contains(sourceRef))
                playlistIds.Add(reader.GetInt64(reader.GetOrdinal("id")));
        }

        return playlistIds;
    }

    private static async Task<LocalPlaylistEntity?> FindPlaylistAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string kind,
        string? sourceRef,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, name, cover_path, kind, source_ref, created_at, updated_at
            FROM playlists
            WHERE kind = $kind AND source_ref IS $sourceRef
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$sourceRef", DbValue(sourceRef));
        return await ReadSinglePlaylistAsync(command, cancellationToken);
    }

    private static async Task<LocalPlaylistEntity?> GetPlaylistAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long playlistId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, name, cover_path, kind, source_ref, created_at, updated_at
            FROM playlists
            WHERE id = $playlistId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$playlistId", playlistId);
        return await ReadSinglePlaylistAsync(command, cancellationToken);
    }

    private static async Task<LocalTrackEntity?> GetTrackAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long trackId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, source_type, local_path, title, artist, album, duration_seconds, cover_path, remote_url,
                   file_size, last_write_time_utc, created_at, updated_at
            FROM tracks
            WHERE id = $trackId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$trackId", trackId);
        return await ReadSingleTrackAsync(command, cancellationToken);
    }

    private static async Task<LocalTrackEntity?> GetTrackByLocalPathAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string localPath,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, source_type, local_path, title, artist, album, duration_seconds, cover_path, remote_url,
                   file_size, last_write_time_utc, created_at, updated_at
            FROM tracks
            WHERE local_path = $localPath
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$localPath", localPath);
        return await ReadSingleTrackAsync(command, cancellationToken);
    }

    private static async Task<long> InsertPlaylistAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        LocalPlaylistEntity playlist,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO playlists (name, cover_path, kind, source_ref, created_at, updated_at)
            VALUES ($name, $coverPath, $kind, $sourceRef, $createdAt, $updatedAt);
            """;
        AddPlaylistParameters(command, playlist);
        await command.ExecuteNonQueryAsync(cancellationToken);
        var id = await GetLastInsertRowIdAsync(connection, transaction, cancellationToken);
        playlist.Id = id;
        return id;
    }

    private static async Task UpdatePlaylistAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        LocalPlaylistEntity playlist,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE playlists
            SET name = $name,
                cover_path = $coverPath,
                kind = $kind,
                source_ref = $sourceRef,
                created_at = $createdAt,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", playlist.Id);
        AddPlaylistParameters(command, playlist);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeletePlaylistAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long playlistId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM playlists WHERE id = $playlistId;";
        command.Parameters.AddWithValue("$playlistId", playlistId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeletePlaylistTracksAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long playlistId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = $playlistId;";
        command.Parameters.AddWithValue("$playlistId", playlistId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> DeletePlaylistTrackAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long playlistId,
        long trackId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM playlist_tracks
            WHERE playlist_id = $playlistId AND track_id = $trackId;
            """;
        command.Parameters.AddWithValue("$playlistId", playlistId);
        command.Parameters.AddWithValue("$trackId", trackId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<long>> GetPlaylistTrackIdsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long playlistId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT track_id FROM playlist_tracks WHERE playlist_id = $playlistId;";
        command.Parameters.AddWithValue("$playlistId", playlistId);

        var ids = new HashSet<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(reader.GetInt64(0));

        return ids;
    }

    private static async Task<int> GetNextPlaylistPositionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long playlistId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(position) + 1, 0) FROM playlist_tracks WHERE playlist_id = $playlistId;";
        command.Parameters.AddWithValue("$playlistId", playlistId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task InsertPlaylistTrackAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long playlistId,
        long trackId,
        int position,
        DateTime addedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO playlist_tracks (playlist_id, track_id, position, added_at)
            VALUES ($playlistId, $trackId, $position, $addedAt);
            """;
        command.Parameters.AddWithValue("$playlistId", playlistId);
        command.Parameters.AddWithValue("$trackId", trackId);
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$addedAt", AppDatabase.FormatDateTime(addedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertTrackEntityAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        LocalTrackEntity track,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO tracks (
                source_type, local_path, title, artist, album, duration_seconds, cover_path, remote_url,
                file_size, last_write_time_utc, created_at, updated_at
            )
            VALUES (
                $sourceType, $localPath, $title, $artist, $album, $durationSeconds, $coverPath, $remoteUrl,
                $fileSize, $lastWriteTimeUtc, $createdAt, $updatedAt
            )
            ON CONFLICT(local_path) DO UPDATE SET
                source_type = excluded.source_type,
                title = excluded.title,
                artist = excluded.artist,
                album = excluded.album,
                duration_seconds = excluded.duration_seconds,
                cover_path = excluded.cover_path,
                remote_url = excluded.remote_url,
                file_size = excluded.file_size,
                last_write_time_utc = excluded.last_write_time_utc,
                updated_at = excluded.updated_at;
            """;
        AddTrackParameters(command, track);
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (track.Id == 0)
            track.Id = await GetTrackIdByLocalPathAsync(connection, transaction, track.LocalPath, cancellationToken);
    }

    private static async Task UpdateTrackAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        LocalTrackEntity track,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE tracks
            SET source_type = $sourceType,
                local_path = $localPath,
                title = $title,
                artist = $artist,
                album = $album,
                duration_seconds = $durationSeconds,
                cover_path = $coverPath,
                remote_url = $remoteUrl,
                file_size = $fileSize,
                last_write_time_utc = $lastWriteTimeUtc,
                created_at = $createdAt,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", track.Id);
        AddTrackParameters(command, track);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> GetTrackIdByLocalPathAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string localPath,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM tracks WHERE local_path = $localPath LIMIT 1;";
        command.Parameters.AddWithValue("$localPath", localPath);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private static async Task<long> GetLastInsertRowIdAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT last_insert_rowid();";
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private static async Task<LocalPlaylistEntity?> ReadSinglePlaylistAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPlaylist(reader) : null;
    }

    private static async Task<LocalTrackEntity?> ReadSingleTrackAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTrack(reader) : null;
    }

    private static LocalPlaylistSummary ReadPlaylistSummary(SqliteDataReader reader)
    {
        return new LocalPlaylistSummary(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("name")),
            GetNullableString(reader, "cover_path"),
            Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("track_count"))),
            AppDatabase.ReadDateTime(reader, "updated_at"));
    }

    private static LocalTrackItem ReadTrackItem(SqliteDataReader reader)
    {
        return new LocalTrackItem(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("title")),
            reader.GetString(reader.GetOrdinal("artist")),
            reader.GetString(reader.GetOrdinal("album")),
            reader.GetDouble(reader.GetOrdinal("duration_seconds")),
            reader.GetString(reader.GetOrdinal("source_type")),
            reader.GetString(reader.GetOrdinal("local_path")),
            GetNullableString(reader, "cover_path"),
            GetNullableString(reader, "remote_url"));
    }

    private static LocalPlaylistEntity ReadPlaylist(SqliteDataReader reader)
    {
        return new LocalPlaylistEntity
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            CoverPath = GetNullableString(reader, "cover_path"),
            Kind = reader.GetString(reader.GetOrdinal("kind")),
            SourceRef = GetNullableString(reader, "source_ref"),
            CreatedAtUtc = AppDatabase.ReadDateTime(reader, "created_at"),
            UpdatedAtUtc = AppDatabase.ReadDateTime(reader, "updated_at")
        };
    }

    private static LocalTrackEntity ReadTrack(SqliteDataReader reader)
    {
        return new LocalTrackEntity
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            SourceType = reader.GetString(reader.GetOrdinal("source_type")),
            LocalPath = reader.GetString(reader.GetOrdinal("local_path")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Artist = reader.GetString(reader.GetOrdinal("artist")),
            Album = reader.GetString(reader.GetOrdinal("album")),
            DurationSeconds = reader.GetDouble(reader.GetOrdinal("duration_seconds")),
            CoverPath = GetNullableString(reader, "cover_path"),
            RemoteUrl = GetNullableString(reader, "remote_url"),
            FileSize = reader.GetInt64(reader.GetOrdinal("file_size")),
            LastWriteTimeUtc = AppDatabase.ReadDateTime(reader, "last_write_time_utc"),
            CreatedAtUtc = AppDatabase.ReadDateTime(reader, "created_at"),
            UpdatedAtUtc = AppDatabase.ReadDateTime(reader, "updated_at")
        };
    }

    private static void AddPlaylistParameters(SqliteCommand command, LocalPlaylistEntity playlist)
    {
        command.Parameters.AddWithValue("$name", playlist.Name);
        command.Parameters.AddWithValue("$coverPath", DbValue(playlist.CoverPath));
        command.Parameters.AddWithValue("$kind", playlist.Kind);
        command.Parameters.AddWithValue("$sourceRef", DbValue(playlist.SourceRef));
        command.Parameters.AddWithValue("$createdAt", AppDatabase.FormatDateTime(playlist.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAt", AppDatabase.FormatDateTime(playlist.UpdatedAtUtc));
    }

    private static void AddTrackParameters(SqliteCommand command, LocalTrackEntity track)
    {
        command.Parameters.AddWithValue("$sourceType", track.SourceType);
        command.Parameters.AddWithValue("$localPath", track.LocalPath);
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", track.Artist);
        command.Parameters.AddWithValue("$album", track.Album);
        command.Parameters.AddWithValue("$durationSeconds", track.DurationSeconds);
        command.Parameters.AddWithValue("$coverPath", DbValue(track.CoverPath));
        command.Parameters.AddWithValue("$remoteUrl", DbValue(track.RemoteUrl));
        command.Parameters.AddWithValue("$fileSize", track.FileSize);
        command.Parameters.AddWithValue("$lastWriteTimeUtc", AppDatabase.FormatDateTime(track.LastWriteTimeUtc));
        command.Parameters.AddWithValue("$createdAt", AppDatabase.FormatDateTime(track.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAt", AppDatabase.FormatDateTime(track.UpdatedAtUtc));
    }

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static object DbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await InitializeAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<LocalTrackImportData>> PrepareLocalTrackBatchAsync(
        IReadOnlyList<string> filePaths,
        IReadOnlyDictionary<string, string>? legacySongCovers,
        CancellationToken cancellationToken)
    {
        var tasks = new Task<LocalTrackImportData>[filePaths.Count];
        for (var i = 0; i < filePaths.Count; i++)
            tasks[i] = ReadTrackImportDataAsync(filePaths[i], GetLegacyCover(filePaths[i], legacySongCovers), cancellationToken);

        return await Task.WhenAll(tasks);
    }

    private async Task<LocalTrackImportData> ReadTrackImportDataAsync(
        string filePath,
        string? explicitCoverPath,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var metadata = ReadMetadata(filePath, explicitCoverPath);
            var lastWriteTime = System.IO.File.GetLastWriteTimeUtc(filePath);
            var fileSize = new FileInfo(filePath).Length;
            return new LocalTrackImportData(filePath, fileSize, lastWriteTime, metadata);
        }, cancellationToken);
    }

    private static IEnumerable<IReadOnlyList<string>> EnumerateSupportedAudioFileBatches(string folderPath, int batchSize)
    {
        var batch = new List<string>(batchSize);
        foreach (var filePath in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories))
        {
            if (!IsSupportedAudioFile(filePath))
                continue;

            batch.Add(filePath);
            if (batch.Count < batchSize)
                continue;

            yield return batch;
            batch = new List<string>(batchSize);
        }

        if (batch.Count > 0)
            yield return batch;
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
            var track = new Track(filePath);
            title = string.IsNullOrWhiteSpace(track.Title) ? title : track.Title;
            artist = NormalizeAtlArtists(track.Artist, artist);
            album = track.Album ?? "";
            duration = track.DurationMs > 0 ? track.DurationMs / 1000d : track.Duration;
            coverPath ??= GetEmbeddedSongCoverSource(filePath, track);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取标签失败，已降级为文件名加载。 file={File}", filePath);
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

    private static string NormalizeAtlArtists(string? artistValue, string fallbackArtist)
    {
        if (string.IsNullOrWhiteSpace(artistValue))
            return fallbackArtist;

        var artists = artistValue
            .Split(Settings.DisplayValueSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return artists.Length > 0 ? string.Join(", ", artists) : fallbackArtist;
    }

    private static string? GetEmbeddedSongCoverSource(string songPath, Track track)
    {
        var picture = track.EmbeddedPictures?
                          .AsValueEnumerable().FirstOrDefault(x => x.PicType == PictureInfo.PIC_TYPE.Front && x.PictureData.Length > 0)
            ?? track.EmbeddedPictures?.AsValueEnumerable().FirstOrDefault(x => x.PictureData.Length > 0);

        return picture == null ? null : LocalImageSourceHelper.BuildEmbeddedCoverSource(songPath);
    }

    private static async Task WriteEmbeddedCoverAsync(string audioFilePath, string imagePath, CancellationToken cancellationToken)
    {
        var imageBytes = await System.IO.File.ReadAllBytesAsync(imagePath, cancellationToken);
        await Task.Run(() =>
        {
            var track = new Track(audioFilePath);
            var pictures = track.EmbeddedPictures;
            for (var i = pictures.Count - 1; i >= 0; i--)
            {
                if (pictures[i].PicType == PictureInfo.PIC_TYPE.Front)
                    pictures.RemoveAt(i);
            }

            pictures.Insert(0, PictureInfo.fromBinaryData(imageBytes, PictureInfo.PIC_TYPE.Front));
            if (!track.Save())
                throw new IOException($"写入嵌入封面失败: {audioFilePath}");
        }, cancellationToken);
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

    private sealed record LocalTrackImportData(
        string FilePath,
        long FileSize,
        DateTime LastWriteTimeUtc,
        LocalTrackMetadata Metadata);

    private sealed record JellyfinAlbumGroup(
        string Key,
        string Name,
        List<JellyfinAudioItem> Items);

    private sealed record JellyfinLibraryRef(
        string ServerFingerprint,
        string LibraryId);
}
