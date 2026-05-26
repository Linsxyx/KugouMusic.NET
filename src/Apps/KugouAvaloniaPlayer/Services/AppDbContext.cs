using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace KugouAvaloniaPlayer.Services;

[method: UnconditionalSuppressMessage(
    "Trimming",
    "IL2026",
    Justification = "The desktop app intentionally uses EF Core for local SQLite persistence.")]
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public static readonly string DatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "kugou",
        "kugou-player.db");

    public DbSet<AppKeyValueEntity> KeyValues => Set<AppKeyValueEntity>();
    public DbSet<LocalPlaylistEntity> LocalPlaylists => Set<LocalPlaylistEntity>();
    public DbSet<LocalPlaylistTrackEntity> LocalPlaylistTracks => Set<LocalPlaylistTrackEntity>();
    public DbSet<LocalTrackEntity> LocalTracks => Set<LocalTrackEntity>();

    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .Options;

        return new AppDbContext(options);
    }

    public static void EnsureDatabaseCreated()
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        using var db = Create();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw(
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
            """);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var keyValue = modelBuilder.Entity<AppKeyValueEntity>();
        keyValue.ToTable("kv_store");
        keyValue.HasKey(x => new { x.Scope, x.Key });
        keyValue.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(128);
        keyValue.Property(x => x.Key).HasColumnName("key").HasMaxLength(256);
        keyValue.Property(x => x.Value).HasColumnName("value").IsRequired();
        keyValue.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        var track = modelBuilder.Entity<LocalTrackEntity>();
        track.ToTable("tracks");
        track.HasKey(x => x.Id);
        track.Property(x => x.Id).HasColumnName("id");
        track.Property(x => x.SourceType).HasColumnName("source_type").IsRequired();
        track.Property(x => x.LocalPath).HasColumnName("local_path").IsRequired();
        track.HasIndex(x => x.LocalPath).IsUnique();
        track.Property(x => x.Title).HasColumnName("title").IsRequired();
        track.Property(x => x.Artist).HasColumnName("artist").IsRequired();
        track.Property(x => x.Album).HasColumnName("album").IsRequired();
        track.Property(x => x.DurationSeconds).HasColumnName("duration_seconds");
        track.Property(x => x.CoverPath).HasColumnName("cover_path");
        track.Property(x => x.FileSize).HasColumnName("file_size");
        track.Property(x => x.LastWriteTimeUtc).HasColumnName("last_write_time_utc").IsRequired();
        track.Property(x => x.CreatedAtUtc).HasColumnName("created_at").IsRequired();
        track.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at").IsRequired();

        var playlist = modelBuilder.Entity<LocalPlaylistEntity>();
        playlist.ToTable("playlists");
        playlist.HasKey(x => x.Id);
        playlist.Property(x => x.Id).HasColumnName("id");
        playlist.Property(x => x.Name).HasColumnName("name").IsRequired();
        playlist.Property(x => x.CoverPath).HasColumnName("cover_path");
        playlist.Property(x => x.Kind).HasColumnName("kind").IsRequired();
        playlist.Property(x => x.SourceRef).HasColumnName("source_ref");
        playlist.Property(x => x.CreatedAtUtc).HasColumnName("created_at").IsRequired();
        playlist.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at").IsRequired();
        playlist.HasIndex(x => new { x.Kind, x.SourceRef });
        playlist.HasMany(x => x.Tracks)
            .WithOne(x => x.Playlist)
            .HasForeignKey(x => x.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        var playlistTrack = modelBuilder.Entity<LocalPlaylistTrackEntity>();
        playlistTrack.ToTable("playlist_tracks");
        playlistTrack.HasKey(x => new { x.PlaylistId, x.TrackId });
        playlistTrack.Property(x => x.PlaylistId).HasColumnName("playlist_id");
        playlistTrack.Property(x => x.TrackId).HasColumnName("track_id");
        playlistTrack.Property(x => x.Position).HasColumnName("position");
        playlistTrack.Property(x => x.AddedAtUtc).HasColumnName("added_at").IsRequired();
        playlistTrack.HasIndex(x => new { x.PlaylistId, x.Position });
        playlistTrack.HasOne(x => x.Track)
            .WithMany(x => x.Playlists)
            .HasForeignKey(x => x.TrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AppKeyValueEntity
{
    public string Scope { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
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
    public long FileSize { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<LocalPlaylistTrackEntity> Playlists { get; set; } = new();
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
    public List<LocalPlaylistTrackEntity> Tracks { get; set; } = new();
}

public sealed class LocalPlaylistTrackEntity
{
    public long PlaylistId { get; set; }
    public long TrackId { get; set; }
    public int Position { get; set; }
    public DateTime AddedAtUtc { get; set; }
    public LocalPlaylistEntity Playlist { get; set; } = null!;
    public LocalTrackEntity Track { get; set; } = null!;
}
