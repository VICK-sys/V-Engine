using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VEngine.Engine.Core;

/// <summary>
/// String table localization. Loads JSON files per locale from Assets/locales/,
/// keyed lookups via Eng.Tr("menu.play") (or Eng.Locale.Text(key)), with {0}, {1} placeholder substitution
/// and fallback to the key itself if translation is missing.
///
/// Usage:
///   Eng.Locale.Load("en");                     // loads Assets/locales/en.json
///   var title = Eng.Locale.Text("menu.title"); // "Play"
///   var msg = Eng.Locale.Text("hello", "Alice"); // "Hello, Alice!"
/// </summary>
public class Localization
{
    private readonly Dictionary<string, string> _strings = new();
    private string _currentLocale = "";
    private string _folder = "locales";

    /// <summary>Current locale code (e.g. "en", "fr"). Empty string if none loaded.</summary>
    public string Current => _currentLocale;

    /// <summary>Number of loaded strings.</summary>
    public int Count => _strings.Count;

    /// <summary>Subfolder under Assets/ to look for locale JSON files. Default "locales".</summary>
    public string Folder
    {
        get => _folder;
        set => _folder = value;
    }

    /// <summary>
    /// Load a locale JSON file from Assets/{Folder}/{locale}.json.
    /// The file should be a flat object of key-value pairs: {"menu.play": "Play", ...}.
    /// Replaces any previously loaded strings.
    /// </summary>
    public bool Load(string locale)
    {
        _strings.Clear();
        _currentLocale = "";

        string path = Path.Combine(Eng.AssetsPath, _folder, $"{locale}.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[Localization] Locale file not found: {path}");
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    _strings[prop.Name] = prop.Value.GetString() ?? "";
            }
            _currentLocale = locale;
            Console.WriteLine($"[Localization] Loaded {_strings.Count} strings from {locale}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Localization] Failed to parse {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Look up a translated string by key. If missing, returns the key itself.
    /// Optional args are substituted via string.Format: {0}, {1}, etc.
    /// </summary>
    public string Text(string key, params object[] args)
    {
        if (!_strings.TryGetValue(key, out var value))
            return key;

        if (args == null || args.Length == 0)
            return value;

        try { return string.Format(value, args); }
        catch (FormatException) { return value; }
    }

    /// <summary>True if a translation exists for this key.</summary>
    public bool HasKey(string key) => _strings.ContainsKey(key);

    /// <summary>Set or override a single string. Useful for runtime edits.</summary>
    public void Set(string key, string value) => _strings[key] = value;

    /// <summary>Get all loaded keys.</summary>
    public IEnumerable<string> Keys => _strings.Keys;

    /// <summary>List all available locales by scanning Assets/{Folder}/*.json.</summary>
    public string[] Available()
    {
        var dir = Path.Combine(Eng.AssetsPath, _folder);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        var files = Directory.GetFiles(dir, "*.json");
        var result = new string[files.Length];
        for (int i = 0; i < files.Length; i++)
            result[i] = Path.GetFileNameWithoutExtension(files[i]);
        return result;
    }

    /// <summary>Clear all loaded strings.</summary>
    public void Clear()
    {
        _strings.Clear();
        _currentLocale = "";
    }
}
