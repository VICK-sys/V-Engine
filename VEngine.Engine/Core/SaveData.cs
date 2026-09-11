using System;
using System.IO;
using System.Text.Json;

namespace VEngine.Engine.Core;

/// <summary>
/// Simple save/load utility. Serializes any object to JSON in a saves folder.
/// Uses System.Text.Json with field support enabled.
/// </summary>
public static class SaveData
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    /// <summary>
    /// Path to the saves folder. Default: "Saves" next to executable.
    /// </summary>
    public static string SavesPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "Saves");

    /// <summary>Save an object as JSON to the saves folder.</summary>
    public static void Save<T>(string filename, T data)
    {
        Directory.CreateDirectory(SavesPath);
        var path = ResolvePath(filename);
        var json = JsonSerializer.Serialize(data, _options);
        File.WriteAllText(path, json);
    }

    /// <summary>Load an object from JSON in the saves folder. Returns default if not found.</summary>
    public static T? Load<T>(string filename)
    {
        var path = ResolvePath(filename);
        if (!File.Exists(path)) return default;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, _options);
    }

    /// <summary>Check if a save file exists.</summary>
    public static bool Exists(string filename)
    {
        return File.Exists(ResolvePath(filename));
    }

    /// <summary>Delete a save file.</summary>
    public static void Delete(string filename)
    {
        var path = ResolvePath(filename);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Resolve and validate a save filename. Prevents path traversal attacks.</summary>
    private static string ResolvePath(string filename)
    {
        var full = Path.GetFullPath(Path.Combine(SavesPath, filename));
        var savesRoot = Path.GetFullPath(SavesPath);
        if (!full.StartsWith(savesRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Save path escapes saves folder: {filename}");
        return full;
    }

    /// <summary>List all save files matching a pattern. Returns filenames only.</summary>
    public static string[] List(string pattern = "*.json")
    {
        if (!Directory.Exists(SavesPath)) return Array.Empty<string>();
        var files = Directory.GetFiles(SavesPath, pattern);
        for (int i = 0; i < files.Length; i++)
            files[i] = Path.GetFileName(files[i]);
        return files;
    }
}
