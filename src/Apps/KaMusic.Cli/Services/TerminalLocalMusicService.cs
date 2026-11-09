using System.Globalization;
using System.Threading.Channels;
using ATL;
using KaMusic.Cli.Models;
using Microsoft.Data.Sqlite;

namespace KaMusic.Cli.Services;

// 终端版与桌面客户端共享的本地音乐库服务：直接复用 kugou-player.db SQLite 表结构与 ATL 音频标签解析逻辑。

internal sealed class TerminalLocalMusicService
{
    public const string PlaylistKindManual = "Manual";
    public const string PlaylistKindImportedFolder = "ImportedFolder";
    public const string PlaylistKindJellyfinLibrary = "JellyfinLibrary";
    public const string SourceTypeLocalFile = "LocalFile";
    public const string SourceTypeJellyfin = "Jellyfin";
    public const string EmbeddedCoverScheme = "kg-embedded-cover";

    private const int LocalImportBatchSize = 32;

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

    private static readonly EnumerationOptions SafeFolderEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

#if KUGOU_SYSTEM_SQLITE
    static TerminalLocalMusicService()
    {
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(
            typeof(SQLitePCL.SQLite3Provider_sqlite3).Assembly,
            (name, assembly, searchPath) => name == "sqlite3"
                ? System.Runtime.InteropServices.NativeLibrary.Load("libsqlite3.so.0", assembly, searchPath)
                : IntPtr.Zero);
        SQLitePCL.Batteries_V2.Init();
    }
#endif

    public static readonly string DefaultDatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "kugou-player.db");

    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public TerminalLocalMusicService(string? databasePath = null)
    {
        _databasePath = string.IsNullOrWhiteSpace(databasePath) ? DefaultDatabasePath : databasePath;
    }

    // 初始化共享 SQLite 数据库表与索引，确保与桌面端 AppDatabase 结构完全一致。
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = await CreateConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS kv_store (
                    scope TEXT NOT NULL,
                    key TEXT NOT NULL,
                    value TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY (scope, key)
                );

                CREATE TABLE IF NOT EXISTS tracks (
                    id INTEGER NOT NULL CONSTRAINT PK_tracks PRIMARY KEY AUTOINCREMENT,
                    source_type TEXT NOT NULL,
                    local_path TEXT NOT NULL,
                    title TEXT NOT NULL,
                    artist TEXT NOT NULL,
                    album TEXT NOT NULL,
                    duration_seconds REAL NOT NULL,
                    cover_path TEXT NULL,
                    remote_url TEXT NULL,
                    file_size INTEGER NOT NULL,
                    last_write_time_utc TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS playlists (
                    id INTEGER NOT NULL CONSTRAINT PK_playlists PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    cover_path TEXT NULL,
                    kind TEXT NOT NULL,
                    source_ref TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS playlist_tracks (
                    playlist_id INTEGER NOT NULL,
                    track_id INTEGER NOT NULL,
                    position INTEGER NOT NULL,
                    added_at TEXT NOT NULL,
                    CONSTRAINT PK_playlist_tracks PRIMARY KEY (playlist_id, track_id),
                    CONSTRAINT FK_playlist_tracks_playlists_playlist_id FOREIGN KEY (playlist_id) REFERENCES playlists (id) ON DELETE CASCADE,
                    CONSTRAINT FK_playlist_tracks_tracks_track_id FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS IX_tracks_local_path ON tracks (local_path);
                CREATE INDEX IF NOT EXISTS IX_playlists_kind_source_ref ON playlists (kind, source_ref);
                CREATE INDEX IF NOT EXISTS IX_playlist_tracks_playlist_id_position ON playlist_tracks (playlist_id, position);
                CREATE INDEX IF NOT EXISTS IX_playlist_tracks_track_id ON playlist_tracks (track_id);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // 读取所有本地歌单摘要并映射为终端歌单列表项。
    public async Task<IReadOnlyList<TerminalPlaylistItem>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.id, p.name, p.cover_path, p.kind, p.source_ref, p.updated_at, COUNT(pt.track_id) AS track_count
            FROM playlists p
            LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
            GROUP BY p.id, p.name, p.cover_path, p.kind, p.source_ref, p.updated_at
            ORDER BY p.updated_at DESC, p.id DESC;
            """;

        var list = new List<TerminalPlaylistItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadPlaylistItem(reader));
        }

        return list;
    }

    // 读取指定本地歌单内的全部曲目并转换为终端歌曲项。
    public async Task<IReadOnlyList<TerminalSongItem>> GetPlaylistTracksAsync(
        long playlistId,
        string? playlistName = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await CreateConnectionAsync(cancellationToken);
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

        var sourceLabel = string.IsNullOrWhiteSpace(playlistName) ? "本地音乐" : $"本地 · {playlistName}";
        var tracks = new List<TerminalSongItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tracks.Add(ReadSongItem(reader, sourceLabel));
        }

        return tracks;
    }

    // 在本地音乐库中按关键词检索歌曲。
    public async Task<IReadOnlyList<TerminalSongItem>> SearchTracksAsync(
        string keyword,
        CancellationToken cancellationToken = default)
    {
        var normalizedKeyword = keyword.Trim();
        if (normalizedKeyword.Length == 0)
        {
            return [];
        }

        await InitializeAsync(cancellationToken);
        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT t.id, t.title, t.artist, t.album, t.duration_seconds, t.source_type, t.local_path, t.cover_path, t.remote_url
            FROM tracks t
            LEFT JOIN playlist_tracks pt ON pt.track_id = t.id
            LEFT JOIN playlists p ON p.id = pt.playlist_id
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
                t.id
            LIMIT 100;
            """;

        var escaped = EscapeLikePattern(normalizedKeyword);
        command.Parameters.AddWithValue("$keyword", normalizedKeyword);
        command.Parameters.AddWithValue("$prefix", escaped + "%");
        command.Parameters.AddWithValue("$pattern", "%" + escaped + "%");

        var results = new List<TerminalSongItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadSongItem(reader, "本地搜索"));
        }

        return results;
    }

    // 新建手动管理的本地歌单。
    public async Task<TerminalPlaylistItem> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await CreateConnectionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new LocalPlaylistEntity
        {
            Name = string.IsNullOrWhiteSpace(name) ? "新建本地歌单" : name.Trim(),
            Kind = PlaylistKindManual,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        entity.Id = await InsertPlaylistAsync(connection, null, entity, cancellationToken);
        return await GetPlaylistSummaryAsync(connection, null, entity.Id, cancellationToken);
    }

    // 导入文件夹作为本地歌单（与桌面客户端 ImportFolderAsync 保持一致的扫描、标签提取和去重更新逻辑）。
    public async Task<TerminalPlaylistItem> ImportFolderAsync(string rawFolderPath, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var folderPath = NormalizeInputPath(rawFolderPath);
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"找不到目录：{folderPath}");
        }

        folderPath = Path.GetFullPath(folderPath);

        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var playlist = await FindPlaylistAsync(
            connection,
            transaction,
            PlaylistKindImportedFolder,
            folderPath,
            cancellationToken);

        if (playlist is null)
        {
            var now = DateTime.UtcNow;
            var dirInfo = new DirectoryInfo(folderPath);
            var dirName = string.IsNullOrWhiteSpace(dirInfo.Name) || dirInfo.Name == Path.DirectorySeparatorChar.ToString()
                ? folderPath
                : dirInfo.Name;

            playlist = new LocalPlaylistEntity
            {
                Name = dirName,
                Kind = PlaylistKindImportedFolder,
                SourceRef = folderPath,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            playlist.Id = await InsertPlaylistAsync(connection, transaction, playlist, cancellationToken);
        }

        await ReplacePlaylistTracksFromFolderAsync(connection, transaction, playlist.Id, folderPath, cancellationToken);
        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await UpdatePlaylistAsync(connection, transaction, playlist, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetPlaylistSummaryAsync(connection, null, playlist.Id, cancellationToken);
    }

    // 智能导入路径：支持直接传入音频文件夹路径或单个音频文件路径，可导入为独立文件夹歌单或追加至现有本地歌单。
    public async Task<(TerminalPlaylistItem Playlist, int AddedCount)> ImportPathAsync(
        string rawPath,
        long? targetPlaylistId = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var normalized = NormalizeInputPath(rawPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("请输入有效的文件夹或音频文件路径。");
        }

        if (Directory.Exists(normalized))
        {
            if (targetPlaylistId is null or <= 0)
            {
                var summary = await ImportFolderAsync(normalized, cancellationToken);
                return (summary, summary.Count);
            }

            var folderFiles = EnumerateSupportedAudioFileBatches(Path.GetFullPath(normalized), LocalImportBatchSize, cancellationToken)
                .SelectMany(batch => batch)
                .ToList();
            var added = await AddFilesToPlaylistAsync(targetPlaylistId.Value, folderFiles, cancellationToken);
            await using var conn = await CreateConnectionAsync(cancellationToken);
            var targetSummary = await GetPlaylistSummaryAsync(conn, null, targetPlaylistId.Value, cancellationToken);
            return (targetSummary, added);
        }

        if (File.Exists(normalized))
        {
            var fullFile = Path.GetFullPath(normalized);
            if (!IsSupportedAudioFile(fullFile))
            {
                throw new InvalidOperationException($"不支持的音频格式：{Path.GetExtension(fullFile)}");
            }

            await using var conn = await CreateConnectionAsync(cancellationToken);
            long playlistId;
            if (targetPlaylistId is > 0)
            {
                playlistId = targetPlaylistId.Value;
            }
            else
            {
                var defaultManual = await FindPlaylistByNameAsync(conn, null, PlaylistKindManual, "本地导入单曲", cancellationToken);
                if (defaultManual is null)
                {
                    var created = await CreatePlaylistAsync("本地导入单曲", cancellationToken);
                    playlistId = created.LocalPlaylistId;
                }
                else
                {
                    playlistId = defaultManual.Id;
                }
            }

            var added = await AddFilesToPlaylistAsync(playlistId, [fullFile], cancellationToken);
            var summary = await GetPlaylistSummaryAsync(conn, null, playlistId, cancellationToken);
            return (summary, added);
        }

        throw new FileNotFoundException($"路径不存在：{normalized}");
    }

    // 向指定本地歌单批量添加音频文件并提取音频元数据。
    public async Task<int> AddFilesToPlaylistAsync(
        long playlistId,
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var files = filePaths
            .Select(NormalizeInputPath)
            .Where(IsSupportedAudioFile)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            return 0;
        }

        await using var connection = await CreateConnectionAsync(cancellationToken);
        var playlist = await GetPlaylistAsync(connection, null, playlistId, cancellationToken);
        if (playlist is null)
        {
            return 0;
        }

        await using var transaction = connection.BeginTransaction();
        var existingTrackIds = await GetPlaylistTrackIdsAsync(connection, transaction, playlist.Id, cancellationToken);
        var position = await GetNextPlaylistPositionAsync(connection, transaction, playlist.Id, cancellationToken);
        var added = 0;

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imported = await ReadTrackImportDataAsync(files[i], cancellationToken);
            var track = await UpsertTrackAsync(connection, transaction, imported, cancellationToken);
            if (existingTrackIds.Contains(track.Id))
            {
                continue;
            }

            await InsertPlaylistTrackAsync(
                connection,
                transaction,
                playlist.Id,
                track.Id,
                position + added,
                DateTime.UtcNow,
                cancellationToken);
            existingTrackIds.Add(track.Id);
            added++;
        }

        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await UpdatePlaylistAsync(connection, transaction, playlist, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return added;
    }

    // 刷新所有已导入的本地文件夹歌单。
    public async Task<IReadOnlyList<TerminalPlaylistItem>> RefreshImportedFoldersAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await CreateConnectionAsync(cancellationToken);
        var folderPaths = await GetImportedFolderPathsAsync(connection, cancellationToken);

        var refreshed = new List<TerminalPlaylistItem>();
        foreach (var folderPath in folderPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(folderPath))
            {
                continue;
            }

            refreshed.Add(await ImportFolderAsync(folderPath, cancellationToken));
        }

        return refreshed;
    }

    public async Task DeletePlaylistAsync(long playlistId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM playlists WHERE id = $playlistId;";
        command.Parameters.AddWithValue("$playlistId", playlistId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveTrackFromPlaylistAsync(long playlistId, long trackId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await CreateConnectionAsync(cancellationToken);
        var playlist = await GetPlaylistAsync(connection, null, playlistId, cancellationToken);
        if (playlist is null)
        {
            return;
        }

        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM playlist_tracks
            WHERE playlist_id = $playlistId AND track_id = $trackId;
            """;
        command.Parameters.AddWithValue("$playlistId", playlistId);
        command.Parameters.AddWithValue("$trackId", trackId);
        var removed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (removed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        playlist.UpdatedAtUtc = DateTime.UtcNow;
        await UpdatePlaylistAsync(connection, transaction, playlist, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // 规范化用户输入的路径：去除首尾空白与引号，并展开 ~ 主目录。
    public static string NormalizeInputPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return "";
        }

        var path = rawPath.Trim();
        if ((path.StartsWith('"') && path.EndsWith('"') && path.Length >= 2) ||
            (path.StartsWith('\'') && path.EndsWith('\'') && path.Length >= 2))
        {
            path = path[1..^1].Trim();
        }

        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..]);
        }

        return path;
    }

    public static string BuildEmbeddedCoverSource(string audioFilePath)
    {
        var fullPath = Path.GetFullPath(audioFilePath);
        return $"{EmbeddedCoverScheme}://track?path={Uri.EscapeDataString(fullPath)}";
    }

    public static bool TryParseEmbeddedCoverSource(string? source, out string audioFilePath)
    {
        audioFilePath = "";
        if (string.IsNullOrWhiteSpace(source) ||
            !Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, EmbeddedCoverScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = uri.Query.TrimStart('?');
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], "path", StringComparison.OrdinalIgnoreCase))
            {
                var decoded = Uri.UnescapeDataString(parts[1]);
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    audioFilePath = decoded;
                    return true;
                }
            }
        }

        return false;
    }

    // 使用生产者-消费者通道并发扫描并提取文件夹内所有支持格式的音频标签，再批量写入 SQLite。
    private async Task ReplacePlaylistTracksFromFolderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long playlistId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = $playlistId;";
            deleteCmd.Parameters.AddWithValue("$playlistId", playlistId);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var channel = Channel.CreateBounded<IReadOnlyList<LocalTrackImportData>>(
            new BoundedChannelOptions(4)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producerToken = producerCancellation.Token;

        var producer = Task.Run(
            async () =>
            {
                try
                {
                    foreach (var fileBatch in EnumerateSupportedAudioFileBatches(folderPath, LocalImportBatchSize, producerToken))
                    {
                        var tasks = new Task<LocalTrackImportData>[fileBatch.Count];
                        for (var i = 0; i < fileBatch.Count; i++)
                        {
                            tasks[i] = ReadTrackImportDataAsync(fileBatch[i], producerToken);
                        }

                        var importedBatch = await Task.WhenAll(tasks);
                        await channel.Writer.WriteAsync(importedBatch, producerToken);
                    }

                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            },
            producerToken);

        var position = 0;
        try
        {
            await foreach (var importedBatch in channel.Reader.ReadAllAsync(cancellationToken))
            {
                foreach (var importedTrack in importedBatch)
                {
                    var track = await UpsertTrackAsync(connection, transaction, importedTrack, cancellationToken);
                    await InsertPlaylistTrackAsync(
                        connection,
                        transaction,
                        playlistId,
                        track.Id,
                        position++,
                        DateTime.UtcNow,
                        cancellationToken);
                }
            }
        }
        finally
        {
            await producerCancellation.CancelAsync();
            await producer;
        }
    }

    private static IEnumerable<IReadOnlyList<string>> EnumerateSupportedAudioFileBatches(
        string folderPath,
        int batchSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var batch = new List<string>(batchSize);
        foreach (var filePath in Directory.EnumerateFiles(folderPath, "*.*", SafeFolderEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSupportedAudioFile(filePath))
            {
                continue;
            }

            batch.Add(filePath);
            if (batch.Count < batchSize)
            {
                continue;
            }

            yield return batch;
            batch = new List<string>(batchSize);
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private static Task<LocalTrackImportData> ReadTrackImportDataAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                var metadata = ReadMetadata(filePath);
                var lastWriteTime = File.GetLastWriteTimeUtc(filePath);
                var fileSize = new FileInfo(filePath).Length;
                return new LocalTrackImportData(filePath, fileSize, lastWriteTime, metadata);
            },
            cancellationToken);
    }

    // 通过 ATL.Track 读取音频文件的标题、艺术家、专辑、时长与内嵌封面 URI。
    private static LocalTrackMetadata ReadMetadata(string filePath)
    {
        var title = Path.GetFileNameWithoutExtension(filePath);
        var artist = "未知艺术家";
        var album = "";
        double duration = 0;
        string? coverPath = null;

        try
        {
            if (new FileInfo(filePath).Length >= 44)
            {
                var track = new Track(filePath);
                title = string.IsNullOrWhiteSpace(track.Title) ? title : track.Title;
                artist = NormalizeAtlArtists(track.Artist, artist);
                album = track.Album ?? "";
                duration = track.DurationMs > 0 ? track.DurationMs / 1000d : track.Duration;
                coverPath = GetEmbeddedSongCoverSource(filePath, track);
            }
        }
        catch
        {
            // 标签损坏或无法识别时降级使用文件名。
        }

        return new LocalTrackMetadata(title, artist, album, duration, coverPath);
    }

    private static string NormalizeAtlArtists(string? artistValue, string fallbackArtist)
    {
        if (string.IsNullOrWhiteSpace(artistValue))
        {
            return fallbackArtist;
        }

        var artists = artistValue.Split(
            Settings.DisplayValueSeparator,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return artists.Length > 0 ? string.Join(", ", artists) : fallbackArtist;
    }

    private static string? GetEmbeddedSongCoverSource(string songPath, Track track)
    {
        var picture =
            track.EmbeddedPictures?.FirstOrDefault(x => x.PicType == PictureInfo.PIC_TYPE.Front && x.PictureData.Length > 0) ??
            track.EmbeddedPictures?.FirstOrDefault(x => x.PictureData.Length > 0);

        return picture is null ? null : BuildEmbeddedCoverSource(songPath);
    }

    public static bool IsSupportedAudioFile(string? filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) &&
               SupportedLocalAudioExtensions.Contains(Path.GetExtension(filePath)) &&
               File.Exists(filePath);
    }

    private async Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<LocalTrackEntity> UpsertTrackAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalTrackImportData importedTrack,
        CancellationToken cancellationToken)
    {
        var metadata = importedTrack.Metadata;
        var now = DateTime.UtcNow;
        var track = await GetTrackByLocalPathAsync(connection, transaction, importedTrack.FilePath, cancellationToken) ??
                    new LocalTrackEntity
                    {
                        SourceType = SourceTypeLocalFile,
                        LocalPath = importedTrack.FilePath,
                        CreatedAtUtc = now
                    };

        track.Title = metadata.Title;
        track.Artist = metadata.Artist;
        track.Album = metadata.Album;
        track.DurationSeconds = metadata.DurationSeconds;
        track.CoverPath = string.IsNullOrWhiteSpace(metadata.CoverPath) ? track.CoverPath : metadata.CoverPath;
        track.RemoteUrl = null;
        track.FileSize = importedTrack.FileSize;
        track.LastWriteTimeUtc = importedTrack.LastWriteTimeUtc;
        track.UpdatedAtUtc = now;

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
        command.Parameters.AddWithValue("$sourceType", track.SourceType);
        command.Parameters.AddWithValue("$localPath", track.LocalPath);
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", track.Artist);
        command.Parameters.AddWithValue("$album", track.Album);
        command.Parameters.AddWithValue("$durationSeconds", track.DurationSeconds);
        command.Parameters.AddWithValue("$coverPath", DbValue(track.CoverPath));
        command.Parameters.AddWithValue("$remoteUrl", DbValue(track.RemoteUrl));
        command.Parameters.AddWithValue("$fileSize", track.FileSize);
        command.Parameters.AddWithValue("$lastWriteTimeUtc", FormatDateTime(track.LastWriteTimeUtc));
        command.Parameters.AddWithValue("$createdAt", FormatDateTime(track.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAt", FormatDateTime(track.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (track.Id == 0)
        {
            await using var idCmd = connection.CreateCommand();
            idCmd.Transaction = transaction;
            idCmd.CommandText = "SELECT id FROM tracks WHERE local_path = $localPath LIMIT 1;";
            idCmd.Parameters.AddWithValue("$localPath", track.LocalPath);
            track.Id = (long)(await idCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        return track;
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
        command.Parameters.AddWithValue("$addedAt", FormatDateTime(addedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        {
            ids.Add(reader.GetInt64(0));
        }

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

        var paths = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPlaylistEntity(reader) : null;
    }

    private static async Task<LocalPlaylistEntity?> FindPlaylistByNameAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string kind,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, name, cover_path, kind, source_ref, created_at, updated_at
            FROM playlists
            WHERE kind = $kind AND name = $name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPlaylistEntity(reader) : null;
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPlaylistEntity(reader) : null;
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

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
            LastWriteTimeUtc = ReadDateTime(reader, "last_write_time_utc"),
            CreatedAtUtc = ReadDateTime(reader, "created_at"),
            UpdatedAtUtc = ReadDateTime(reader, "updated_at")
        };
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

        await using var idCmd = connection.CreateCommand();
        idCmd.Transaction = transaction;
        idCmd.CommandText = "SELECT last_insert_rowid();";
        var id = (long)(await idCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
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

    private static async Task<TerminalPlaylistItem> GetPlaylistSummaryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long playlistId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT p.id, p.name, p.cover_path, p.kind, p.source_ref, p.updated_at, COUNT(pt.track_id) AS track_count
            FROM playlists p
            LEFT JOIN playlist_tracks pt ON pt.playlist_id = p.id
            WHERE p.id = $playlistId
            GROUP BY p.id, p.name, p.cover_path, p.kind, p.source_ref, p.updated_at
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$playlistId", playlistId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadPlaylistItem(reader);
        }

        throw new InvalidOperationException($"找不到本地歌单：{playlistId}");
    }

    private static TerminalPlaylistItem ReadPlaylistItem(SqliteDataReader reader)
    {
        var id = reader.GetInt64(reader.GetOrdinal("id"));
        var name = reader.GetString(reader.GetOrdinal("name"));
        var coverPath = GetNullableString(reader, "cover_path");
        var kind = GetNullableString(reader, "kind") ?? PlaylistKindManual;
        var sourceRef = GetNullableString(reader, "source_ref");
        var count = Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("track_count")));

        var kindLabel = kind switch
        {
            PlaylistKindImportedFolder => "本地文件夹",
            PlaylistKindJellyfinLibrary => "Jellyfin 专辑",
            _ => "自建本地歌单"
        };
        var subtitle = !string.IsNullOrWhiteSpace(sourceRef) && kind == PlaylistKindImportedFolder
            ? $"{kindLabel} · {count} 首 · {sourceRef}"
            : $"{kindLabel} · {count} 首";

        return new TerminalPlaylistItem
        {
            Id = $"local:{id}",
            ListId = id.ToString(CultureInfo.InvariantCulture),
            LocalPlaylistId = id,
            Name = name,
            Subtitle = subtitle,
            Cover = coverPath,
            Count = count,
            Kind = TerminalPlaylistKind.Local,
            LocalKind = kind,
            SourceRef = sourceRef
        };
    }

    private static TerminalSongItem ReadSongItem(SqliteDataReader reader, string sourceLabel)
    {
        var id = reader.GetInt64(reader.GetOrdinal("id"));
        var title = reader.GetString(reader.GetOrdinal("title"));
        var artist = reader.GetString(reader.GetOrdinal("artist"));
        var album = reader.GetString(reader.GetOrdinal("album"));
        var durationSeconds = reader.GetDouble(reader.GetOrdinal("duration_seconds"));
        var sourceType = reader.GetString(reader.GetOrdinal("source_type"));
        var localPath = reader.GetString(reader.GetOrdinal("local_path"));
        var coverPath = GetNullableString(reader, "cover_path");
        var remoteUrl = GetNullableString(reader, "remote_url");

        return new TerminalSongItem
        {
            Name = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(localPath) : title,
            Singer = string.IsNullOrWhiteSpace(artist) ? "未知艺术家" : artist,
            AlbumName = album,
            Hash = $"local-{id}",
            Cover = coverPath,
            DurationSeconds = durationSeconds,
            Source = string.IsNullOrWhiteSpace(album) ? sourceLabel : $"{sourceLabel} · {album}",
            LocalFilePath = string.Equals(sourceType, SourceTypeLocalFile, StringComparison.OrdinalIgnoreCase) ? localPath : null,
            RemoteUrl = remoteUrl,
            LocalSourceType = sourceType,
            LocalTrackId = id
        };
    }

    private static LocalPlaylistEntity ReadPlaylistEntity(SqliteDataReader reader)
    {
        return new LocalPlaylistEntity
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            CoverPath = GetNullableString(reader, "cover_path"),
            Kind = reader.GetString(reader.GetOrdinal("kind")),
            SourceRef = GetNullableString(reader, "source_ref"),
            CreatedAtUtc = ReadDateTime(reader, "created_at"),
            UpdatedAtUtc = ReadDateTime(reader, "updated_at")
        };
    }

    private static void AddPlaylistParameters(SqliteCommand command, LocalPlaylistEntity playlist)
    {
        command.Parameters.AddWithValue("$name", playlist.Name);
        command.Parameters.AddWithValue("$coverPath", DbValue(playlist.CoverPath));
        command.Parameters.AddWithValue("$kind", playlist.Kind);
        command.Parameters.AddWithValue("$sourceRef", DbValue(playlist.SourceRef));
        command.Parameters.AddWithValue("$createdAt", FormatDateTime(playlist.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAt", FormatDateTime(playlist.UpdatedAtUtc));
    }

    private static string FormatDateTime(DateTime value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime ReadDateTime(SqliteDataReader reader, string name)
    {
        var value = reader.GetString(reader.GetOrdinal(name));
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTime.MinValue;
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

    private sealed class LocalTrackEntity
    {
        public long Id { get; set; }
        public string SourceType { get; set; } = "";
        public string LocalPath { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public double DurationSeconds { get; set; }
        public string? CoverPath { get; set; }
        public string? RemoteUrl { get; set; }
        public long FileSize { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    private sealed class LocalPlaylistEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? CoverPath { get; set; }
        public string Kind { get; set; } = "";
        public string? SourceRef { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
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
}
