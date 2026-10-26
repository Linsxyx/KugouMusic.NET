using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace KugouAvaloniaPlayer.Services;

public static class AppDatabase
{
#if KUGOU_SYSTEM_SQLITE
    static AppDatabase()
    {
        // Runtime packages provide libsqlite3.so.0; libsqlite3.so usually needs a development package.
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(
            typeof(SQLitePCL.SQLite3Provider_sqlite3).Assembly,
            (name, assembly, searchPath) => name == "sqlite3"
                ? System.Runtime.InteropServices.NativeLibrary.Load("libsqlite3.so.0", assembly, searchPath)
                : IntPtr.Zero);
        SQLitePCL.Batteries_V2.Init();
    }
#endif

    public static readonly string DatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "kugou-player.db");

    public static SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        EnableForeignKeys(connection);
        return connection;
    }

    public static async Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        await EnableForeignKeysAsync(connection, cancellationToken);
        return connection;
    }

    public static void EnsureDatabaseCreated()
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
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
        command.ExecuteNonQuery();
    }

    public static string FormatDateTime(DateTime value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    public static DateTime ReadDateTime(SqliteDataReader reader, string name)
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

    private static void EnableForeignKeys(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
    }

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class LocalTrackEntity
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

public sealed class LocalPlaylistEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? CoverPath { get; set; }
    public string Kind { get; set; } = "";
    public string? SourceRef { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
