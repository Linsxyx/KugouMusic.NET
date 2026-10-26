using System;
using System.IO;

namespace KugouAvaloniaPlayer.Services;

public static class AppSqliteStore
{
    private static readonly object SyncRoot = new();

    public static string? LoadValue(string scope, string key)
    {
        lock (SyncRoot)
        {
            EnsureCreated();
            using var connection = AppDatabase.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM kv_store WHERE scope = $scope AND key = $key LIMIT 1;";
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        }
    }

    public static void SaveValue(string scope, string key, string value)
    {
        lock (SyncRoot)
        {
            EnsureCreated();
            using var connection = AppDatabase.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO kv_store (scope, key, value, updated_at)
                VALUES ($scope, $key, $value, $updatedAt)
                ON CONFLICT(scope, key) DO UPDATE SET
                    value = excluded.value,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$updatedAt", AppDatabase.FormatDateTime(DateTime.UtcNow));
            command.ExecuteNonQuery();
        }
    }

    public static void DeleteValue(string scope, string key)
    {
        lock (SyncRoot)
        {
            EnsureCreated();
            using var connection = AppDatabase.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM kv_store WHERE scope = $scope AND key = $key;";
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$key", key);
            command.ExecuteNonQuery();
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
        AppDatabase.EnsureDatabaseCreated();
        RestrictFileAccess(AppDatabase.DatabasePath);
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
