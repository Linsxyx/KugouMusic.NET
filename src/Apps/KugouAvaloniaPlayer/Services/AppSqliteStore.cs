using System;
using System.IO;
using System.Linq;

namespace KugouAvaloniaPlayer.Services;

public static class AppSqliteStore
{
    private static readonly object SyncRoot = new();

    public static string? LoadValue(string scope, string key)
    {
        lock (SyncRoot)
        {
            EnsureCreated();
            using var db = AppDbContext.Create();
            return db.KeyValues
                .Where(x => x.Scope == scope && x.Key == key)
                .Select(x => x.Value)
                .FirstOrDefault();
        }
    }

    public static void SaveValue(string scope, string key, string value)
    {
        lock (SyncRoot)
        {
            EnsureCreated();
            using var db = AppDbContext.Create();
            var entity = db.KeyValues.FirstOrDefault(x => x.Scope == scope && x.Key == key);
            if (entity == null)
            {
                db.KeyValues.Add(new AppKeyValueEntity
                {
                    Scope = scope,
                    Key = key,
                    Value = value,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                entity.Value = value;
                entity.UpdatedAt = DateTime.UtcNow;
            }

            db.SaveChanges();
        }
    }

    public static void DeleteValue(string scope, string key)
    {
        lock (SyncRoot)
        {
            EnsureCreated();
            using var db = AppDbContext.Create();
            var entity = db.KeyValues.FirstOrDefault(x => x.Scope == scope && x.Key == key);
            if (entity == null)
                return;

            db.KeyValues.Remove(entity);
            db.SaveChanges();
        }
    }

    public static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup for migrated legacy JSON files.
        }
    }

    private static void EnsureCreated()
    {
        AppDbContext.EnsureDatabaseCreated();
        RestrictFileAccess(AppDbContext.DatabasePath);
    }

    private static void RestrictFileAccess(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best effort: older file systems may not support Unix file modes.
        }
    }
}
