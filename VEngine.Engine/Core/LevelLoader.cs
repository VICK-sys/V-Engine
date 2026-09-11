using System;
using System.IO;
using System.Text.Json;
using VEngine.Engine.Graphics;
using VEngine.Engine.Math;

namespace VEngine.Engine.Core;

/// <summary>
/// Loads level JSON exported by the level editor.
/// Creates solid and platform entities and adds them to the scene.
/// </summary>
public static class LevelLoader
{
    /// <summary>
    /// Load a level from a JSON file in Assets.
    /// Returns the spawn point. Adds all solids to the solids group and platforms to the platforms group.
    /// </summary>
    public static Vec2 Load(Scene scene, string path, out Group solids, out Group platforms)
    {
        return Load(scene, path, out solids, out platforms, out _);
    }

    /// <summary>
    /// Load a level from a JSON file in Assets, including tilemap if present.
    /// Returns the spawn point. Tilemap is null if the JSON has no tilemap data.
    /// </summary>
    public static Vec2 Load(Scene scene, string path, out Group solids, out Group platforms, out Tilemap? tilemap)
    {
        var fullPath = Path.IsPathRooted(path) ? path : Eng.Asset(path);
        var json = File.ReadAllText(fullPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        solids = new Group { Immovable = true };
        platforms = new Group { Immovable = true };
        scene.Add(solids);
        scene.Add(platforms);

        // Spawn point
        var spawn = Vec2.Zero;
        if (root.TryGetProperty("spawn", out var spawnEl))
        {
            spawn.X = spawnEl.GetProperty("x").GetSingle();
            spawn.Y = spawnEl.GetProperty("y").GetSingle();
        }

        // Entities
        if (root.TryGetProperty("entities", out var entitiesEl))
        {
            foreach (var ent in entitiesEl.EnumerateArray())
            {
                var type = ent.GetProperty("type").GetString() ?? "solid";
                var x = ent.GetProperty("x").GetInt32();
                var y = ent.GetProperty("y").GetInt32();
                var w = ent.GetProperty("w").GetInt32();
                var h = ent.GetProperty("h").GetInt32();

                byte r = 80, g = 80, b = 80;
                if (ent.TryGetProperty("color", out var colorEl) && colorEl.GetArrayLength() >= 3)
                {
                    r = (byte)colorEl[0].GetInt32();
                    g = (byte)colorEl[1].GetInt32();
                    b = (byte)colorEl[2].GetInt32();
                }

                var sprite = new Sprite(x, y);
                sprite.MakeGraphic(w, h, r, g, b);
                sprite.Immovable = true;

                if (type == "platform")
                    platforms.Add(sprite);
                else
                    solids.Add(sprite);
            }
        }

        // Tilemap
        tilemap = null;
        if (root.TryGetProperty("tilemap", out var tmEl))
        {
            var imageName = tmEl.GetProperty("image").GetString()!;
            int tileSize = tmEl.GetProperty("tileSize").GetInt32();
            int gridW = tmEl.GetProperty("width").GetInt32();
            int gridH = tmEl.GetProperty("height").GetInt32();

            var jsonDir = Path.GetDirectoryName(fullPath)!;
            var imageFullPath = Path.Combine(jsonDir, imageName);
            string tilesetPath = File.Exists(imageFullPath) ? imageFullPath : imageName;

            tilemap = new Tilemap(tilesetPath, tileSize, gridW, gridH);

            var tilesArr = tmEl.GetProperty("tiles");
            int i = 0;
            foreach (var t in tilesArr.EnumerateArray())
            {
                if (i >= gridW * gridH) break;
                tilemap.SetTile(i % gridW, i / gridW, t.GetInt32());
                i++;
            }

            if (tmEl.TryGetProperty("solid", out var solidArr))
            {
                i = 0;
                foreach (var s in solidArr.EnumerateArray())
                {
                    if (i >= gridW * gridH) break;
                    tilemap.SetSolid(i % gridW, i / gridW, s.GetInt32() != 0);
                    i++;
                }
            }

            if (tmEl.TryGetProperty("animations", out var animsArr))
            {
                foreach (var a in animsArr.EnumerateArray())
                {
                    int baseTile = a.GetProperty("tile").GetInt32();
                    int fps = a.TryGetProperty("fps", out var fpsEl) ? fpsEl.GetInt32() : 4;
                    var framesEl = a.GetProperty("frames");
                    var frames = new int[framesEl.GetArrayLength()];
                    int j = 0;
                    foreach (var f in framesEl.EnumerateArray())
                        frames[j++] = f.GetInt32();
                    tilemap.AnimateTile(baseTile, frames, fps);
                }
            }

            if (tmEl.TryGetProperty("collisionRects", out var crObj))
            {
                foreach (var prop in crObj.EnumerateObject())
                {
                    if (!int.TryParse(prop.Name, out int tileIdx)) continue;
                    var cr = prop.Value;
                    tilemap.SetTileCollisionRect(tileIdx,
                        cr.GetProperty("x").GetInt32(), cr.GetProperty("y").GetInt32(),
                        cr.GetProperty("w").GetInt32(), cr.GetProperty("h").GetInt32());
                }
            }

            tilemap.Layer = 0; // Behind entities
            scene.Add(tilemap);
        }

        doc.Dispose();
        return spawn;
    }

    /// <summary>
    /// Read the level dimensions from a JSON file.
    /// </summary>
    public static (int Width, int Height) GetSize(string path)
    {
        var fullPath = Path.IsPathRooted(path) ? path : Eng.Asset(path);
        var json = File.ReadAllText(fullPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int w = root.TryGetProperty("width", out var wEl) ? wEl.GetInt32() : 800;
        int h = root.TryGetProperty("height", out var hEl) ? hEl.GetInt32() : 480;

        doc.Dispose();
        return (w, h);
    }
}
