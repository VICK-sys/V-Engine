using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using VEngine.Engine.Core;
using VEngine.Engine.Graphics;
using VEngine.Engine.Graphics.GL;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Fluid;
using VEngine.Engine.UI;

namespace VEngine.Engine.Scripting;

/// <summary>
/// Registers V-Engine API functions into a MoonSharp Script instance.
/// Provides a simplified, Lua-friendly interface to all engine subsystems.
/// </summary>
public static class LuaBindings
{
    /// <summary>
    /// Register all engine bindings into the given script.
    /// The scene parameter is used for entity management (add/remove).
    /// </summary>
    public static void Register(Script script, LuaScene scene)
    {
        RegisterEntityCreation(script, scene);
        RegisterEntityAccess(script);
        RegisterInput(script);
        RegisterAudio(script);
        RegisterCamera(script);
        RegisterTimers(script);
        RegisterTweens(script);
        RegisterEffects(script);
        RegisterCollision(script);
        RegisterDraw(script);
        RegisterEngine(script, scene);
        RegisterGroups(script, scene);
        RegisterFluid(script, scene);
        RegisterParticles(script, scene);
        RegisterRain(script, scene);
        RegisterAfterImage(script, scene);
        RegisterLevel(script, scene);
        RegisterDialogue(script, scene);
        RegisterSpriteAdvanced(script);
        RegisterAI(script);
        RegisterGridFluid(script, scene);
        RegisterMedia(script, scene);
        RegisterAssetLoader(script);
        LuaBehavior.RegisterBinding(script);
    }

    // ── Entity Creation ─────────────────────────────────────────

    private static void RegisterEntityCreation(Script script, LuaScene scene)
    {
        // sprite(path, x, y) or sprite(metaPath, x, y)
        script.Globals["sprite"] = (Func<string, double, double, Sprite>)((path, x, y) =>
        {
            var s = new Sprite((float)x, (float)y);
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                s.LoadFromMeta(path);
            else if (System.IO.Path.IsPathRooted(path))
                s.LoadGraphic(path);
            else
                s.LoadGraphic(Eng.Asset(path));
            scene.AddEntity(s);
            return s;
        });

        // sprite_sheet(path, x, y, frameW, frameH)
        script.Globals["sprite_sheet"] = (Func<string, double, double, int, int, Sprite>)((path, x, y, fw, fh) =>
        {
            var s = new Sprite((float)x, (float)y);
            s.LoadGraphic(Eng.Asset(path), fw, fh);
            scene.AddEntity(s);
            return s;
        });

        // rect(w, h, x, y, r, g, b, a?) -> colored rectangle sprite
        script.Globals["rect"] = (Func<int, int, double, double, int, int, int, int, Sprite>)((w, h, x, y, r, g, b, a) =>
        {
            var s = new Sprite((float)x, (float)y);
            s.MakeGraphic(w, h, B(r), B(g), B(b), B(a));
            scene.AddEntity(s);
            return s;
        });

        // text(content, x, y, size?) -> Text entity
        script.Globals["text"] = (Func<string, double, double, int, Text>)((content, x, y, size) =>
        {
            var t = new Text(content, (float)x, (float)y);
            t.SetDefaultFont(size > 0 ? size : 16);
            scene.AddEntity(t);
            return t;
        });

        // destroy(entity)
        script.Globals["destroy"] = (Action<Entity>)((entity) =>
        {
            scene.RemoveEntity(entity);
        });
    }

    // ── Entity Property Access ──────────────────────────────────

    private static void RegisterEntityAccess(Script script)
    {
        // Position
        script.Globals["get_x"] = (Func<Entity, double>)(e => e.Position.X);
        script.Globals["get_y"] = (Func<Entity, double>)(e => e.Position.Y);
        script.Globals["set_x"] = (Action<Entity, double>)((e, v) => e.Position.X = (float)v);
        script.Globals["set_y"] = (Action<Entity, double>)((e, v) => e.Position.Y = (float)v);
        script.Globals["set_pos"] = (Action<Entity, double, double>)((e, x, y) =>
        {
            e.Position.X = (float)x;
            e.Position.Y = (float)y;
        });

        // Velocity (KinematicEntity)
        script.Globals["get_vx"] = (Func<KinematicEntity, double>)(e => e.Velocity.X);
        script.Globals["get_vy"] = (Func<KinematicEntity, double>)(e => e.Velocity.Y);
        script.Globals["set_vx"] = (Action<KinematicEntity, double>)((e, v) => e.Velocity.X = (float)v);
        script.Globals["set_vy"] = (Action<KinematicEntity, double>)((e, v) => e.Velocity.Y = (float)v);
        script.Globals["set_vel"] = (Action<KinematicEntity, double, double>)((e, x, y) =>
        {
            e.Velocity.X = (float)x;
            e.Velocity.Y = (float)y;
        });

        // Acceleration
        script.Globals["set_accel"] = (Action<KinematicEntity, double, double>)((e, x, y) =>
        {
            e.Acceleration.X = (float)x;
            e.Acceleration.Y = (float)y;
        });

        // Scale
        script.Globals["set_scale"] = (Action<Entity, double, double>)((e, sx, sy) =>
        {
            e.ScaleX = (float)sx;
            e.ScaleY = (float)sy;
        });
        script.Globals["get_width"] = (Func<Entity, double>)(e => e.Width);
        script.Globals["get_height"] = (Func<Entity, double>)(e => e.Height);

        // Angle
        script.Globals["get_angle"] = (Func<Entity, double>)(e => e.Angle);
        script.Globals["set_angle"] = (Action<Entity, double>)((e, v) => e.Angle = (float)v);

        // Visibility / Active
        script.Globals["set_visible"] = (Action<Entity, bool>)((e, v) => e.Visible = v);
        script.Globals["set_active"] = (Action<Entity, bool>)((e, v) => e.Active = v);
        script.Globals["is_active"] = (Func<Entity, bool>)(e => e.Active);

        // Layer / ZOrder
        script.Globals["set_layer"] = (Action<Entity, int>)((e, v) => e.Layer = v);
        script.Globals["set_zorder"] = (Action<Entity, double>)((e, v) => e.ZOrder = (float)v);

        // Scroll factor (0,0 = HUD, 1,1 = world)
        script.Globals["set_scroll"] = (Action<Entity, double, double>)((e, sx, sy) =>
            e.ScrollFactor = new Vec2((float)sx, (float)sy));

        // Immovable
        script.Globals["set_immovable"] = (Action<Entity, bool>)((e, v) => e.Immovable = v);

        // Sprite-specific
        script.Globals["play"] = (Action<Sprite, string, bool>)((s, name, restart) => s.Play(name, restart));
        script.Globals["set_flip_x"] = (Action<Sprite, bool>)((s, v) => s.FlipX = v);
        script.Globals["set_flip_y"] = (Action<Sprite, bool>)((s, v) => s.FlipY = v);
        script.Globals["get_flip_x"] = (Func<Sprite, bool>)(s => s.FlipX);
        script.Globals["anim_finished"] = (Func<Sprite, bool>)(s => s.AnimationFinished);
        script.Globals["current_anim"] = (Func<Sprite, string?>)(s => s.CurrentAnimationName);
        script.Globals["set_anim_speed"] = (Action<Sprite, double>)((s, v) => s.AnimationSpeed = (float)v);

        // Sprite color
        script.Globals["set_color"] = (Action<Sprite, int, int, int, int>)((s, r, g, b, a) =>
            s.Color = new Color(B(r), B(g), B(b), B(a)));
        script.Globals["set_alpha"] = (Action<Sprite, int>)((s, a) =>
            s.Color = s.Color.WithAlpha(B(a)));

        // Sprite effects
        script.Globals["flash"] = (Action<Sprite, double>)((s, dur) => s.Flash((float)dur));
        script.Globals["set_outline"] = (Action<Sprite, double, int, int, int, int>)((s, thickness, r, g, b, a) =>
        {
            s.OutlineThickness = (float)thickness;
            s.OutlineColor = new Color(B(r), B(g), B(b), B(a));
        });
        script.Globals["set_silhouette"] = (Action<Sprite, bool, int, int, int>)((s, on, r, g, b) =>
        {
            s.Silhouette = on;
            s.SilhouetteColor = new Color(B(r), B(g), B(b), 255);
        });

        // Text-specific
        script.Globals["set_text"] = (Action<Text, string>)((t, content) => t.Content = content);
        script.Globals["set_text_color"] = (Action<Text, int, int, int, int>)((t, r, g, b, a) =>
            t.Color = new Color(B(r), B(g), B(b), B(a)));
    }

    // ── Input ───────────────────────────────────────────────────

    private static void RegisterInput(Script script)
    {
        // Action-based input (uses InputMap)
        script.Globals["pressed"] = (Func<string, bool>)(action => Eng.Actions.Pressed(action));
        script.Globals["down"] = (Func<string, bool>)(action => Eng.Actions.Down(action));
        script.Globals["released"] = (Func<string, bool>)(action => Eng.Actions.Released(action));
        script.Globals["axis"] = (Func<string, double>)(name => Eng.Actions.Axis(name));

        // Bind actions from Lua
        script.Globals["bind"] = (Action<string, string>)((action, scancodeName) =>
        {
            if (Enum.TryParse<SDL2.SDL.SDL_Scancode>("SDL_SCANCODE_" + scancodeName.ToUpperInvariant(), out var sc))
                Eng.Actions.Bind(action, sc);
        });
        script.Globals["bind_axis"] = (Action<string, string, string>)((name, posKey, negKey) =>
        {
            if (Enum.TryParse<SDL2.SDL.SDL_Scancode>("SDL_SCANCODE_" + posKey.ToUpperInvariant(), out var pos) &&
                Enum.TryParse<SDL2.SDL.SDL_Scancode>("SDL_SCANCODE_" + negKey.ToUpperInvariant(), out var neg))
                Eng.Actions.BindAxis(name, pos, neg);
        });

        // Raw keyboard
        script.Globals["key_down"] = (Func<string, bool>)(keyName =>
        {
            if (Enum.TryParse<SDL2.SDL.SDL_Scancode>("SDL_SCANCODE_" + keyName.ToUpperInvariant(), out var sc))
                return Eng.Input.IsDown(sc);
            return false;
        });
        script.Globals["key_pressed"] = (Func<string, bool>)(keyName =>
        {
            if (Enum.TryParse<SDL2.SDL.SDL_Scancode>("SDL_SCANCODE_" + keyName.ToUpperInvariant(), out var sc))
                return Eng.Input.IsPressed(sc);
            return false;
        });
        script.Globals["key_released"] = (Func<string, bool>)(keyName =>
        {
            if (Enum.TryParse<SDL2.SDL.SDL_Scancode>("SDL_SCANCODE_" + keyName.ToUpperInvariant(), out var sc))
                return Eng.Input.IsReleased(sc);
            return false;
        });

        // Mouse
        script.Globals["mouse_x"] = (Func<double>)(() => Eng.Mouse.X);
        script.Globals["mouse_y"] = (Func<double>)(() => Eng.Mouse.Y);
        script.Globals["mouse_world_x"] = (Func<double>)(() => Eng.Mouse.WorldPosition.X);
        script.Globals["mouse_world_y"] = (Func<double>)(() => Eng.Mouse.WorldPosition.Y);
        // Lua: 0=left, 1=middle, 2=right. SDL MouseButton enum: Left=1, Middle=2, Right=3.
        script.Globals["mouse_down"] = (Func<int, bool>)(btn =>
            Eng.Mouse.IsDown((Input.MouseButton)(btn + 1)));
        script.Globals["mouse_pressed"] = (Func<int, bool>)(btn =>
            Eng.Mouse.IsPressed((Input.MouseButton)(btn + 1)));
        script.Globals["mouse_released"] = (Func<int, bool>)(btn =>
            Eng.Mouse.IsReleased((Input.MouseButton)(btn + 1)));

        // Gamepad
        script.Globals["gamepad_connected"] = (Func<bool>)(() => Eng.Gamepad.Connected);
        script.Globals["gamepad_down"] = (Func<string, bool>)(btnName =>
        {
            if (Enum.TryParse<SDL2.SDL.SDL_GameControllerButton>("SDL_CONTROLLER_BUTTON_" + btnName.ToUpperInvariant(), out var btn))
                return Eng.Gamepad.IsDown(btn);
            return false;
        });
        script.Globals["gamepad_pressed"] = (Func<string, bool>)(btnName =>
        {
            if (Enum.TryParse<SDL2.SDL.SDL_GameControllerButton>("SDL_CONTROLLER_BUTTON_" + btnName.ToUpperInvariant(), out var btn))
                return Eng.Gamepad.IsPressed(btn);
            return false;
        });
        script.Globals["left_stick_x"] = (Func<double>)(() => Eng.Gamepad.LeftStick.X);
        script.Globals["left_stick_y"] = (Func<double>)(() => Eng.Gamepad.LeftStick.Y);
    }

    // ── Audio ───────────────────────────────────────────────────

    private static void RegisterAudio(Script script)
    {
        script.Globals["sound"] = (Func<string, double, int>)((path, vol) =>
            Eng.Audio.PlaySound(Eng.Asset(path), (float)vol));

        script.Globals["sound_at"] = (Func<string, double, double, double, double, int>)((path, wx, wy, vol, maxDist) =>
            Eng.Audio.PlaySoundAt(Eng.Asset(path), (float)wx, (float)wy, (float)vol, (float)maxDist));

        script.Globals["music"] = (Action<string, bool, int>)((path, loop, fadeIn) =>
            Eng.Audio.PlayMusic(Eng.Asset(path), loop, fadeIn));

        script.Globals["stop_music"] = (Action<int>)((fadeOut) =>
            Eng.Audio.StopMusic(fadeOut));

        script.Globals["stop_sound"] = (Action<int>)((channel) =>
            Eng.Audio.StopChannel(channel));

        script.Globals["set_volume"] = (Action<double>)((v) =>
            Eng.Audio.MasterVolume = (float)v);

        script.Globals["set_sound_volume"] = (Action<double>)((v) =>
            Eng.Audio.SoundVolume = (float)v);

        script.Globals["set_music_volume"] = (Action<double>)((v) =>
            Eng.Audio.MusicVolume = (float)v);
    }

    // ── Camera ──────────────────────────────────────────────────

    private static void RegisterCamera(Script script)
    {
        script.Globals["camera_follow"] = (Action<Entity, double>)((e, lerp) =>
            Eng.Camera.Follow(e, (float)lerp));

        script.Globals["camera_unfollow"] = (Action)(() => Eng.Camera.Unfollow());

        script.Globals["camera_focus"] = (Action<double, double>)((x, y) =>
            Eng.Camera.FocusOn((float)x, (float)y));

        script.Globals["camera_shake"] = (Action<double, double>)((intensity, duration) =>
            Eng.Camera.Shake((float)intensity, (float)duration));

        script.Globals["camera_zoom"] = (Action<double>)((z) => Eng.Camera.Zoom = (float)z);
        script.Globals["get_camera_zoom"] = (Func<double>)(() => Eng.Camera.Zoom);

        script.Globals["camera_bounds"] = (Action<double, double, double, double>)((minX, minY, maxX, maxY) =>
            Eng.Camera.SetBounds((float)minX, (float)minY, (float)maxX, (float)maxY));

        script.Globals["camera_clear_bounds"] = (Action)(() => Eng.Camera.ClearBounds());

        script.Globals["get_camera_x"] = (Func<double>)(() => Eng.Camera.Position.X);
        script.Globals["get_camera_y"] = (Func<double>)(() => Eng.Camera.Position.Y);
    }

    // ── Timers ──────────────────────────────────────────────────

    private static void RegisterTimers(Script script)
    {
        script.Globals["after"] = (Func<double, Closure, TimerHandle>)((delay, fn) =>
            Eng.Timers.After((float)delay, () => fn.Call()));

        script.Globals["every"] = (Func<double, Closure, TimerHandle>)((interval, fn) =>
            Eng.Timers.Every((float)interval, () => fn.Call()));

        script.Globals["cancel_timer"] = (Action<TimerHandle>)((handle) => handle.Cancel());
        script.Globals["cancel_all_timers"] = (Action)(() => Eng.Timers.CancelAll());
    }

    // ── Tweens ──────────────────────────────────────────────────

    private static void RegisterTweens(Script script)
    {
        // tween_x(entity, target, duration, ease?)
        script.Globals["tween_x"] = (Func<Entity, double, double, string, TweenHandle>)((e, target, dur, easeName) =>
        {
            var ease = ParseEase(easeName);
            return Eng.Tweens.To(() => e.Position.X, v => e.Position.X = v, (float)target, (float)dur, ease);
        });

        script.Globals["tween_y"] = (Func<Entity, double, double, string, TweenHandle>)((e, target, dur, easeName) =>
        {
            var ease = ParseEase(easeName);
            return Eng.Tweens.To(() => e.Position.Y, v => e.Position.Y = v, (float)target, (float)dur, ease);
        });

        script.Globals["tween_scale_x"] = (Func<Entity, double, double, string, TweenHandle>)((e, target, dur, easeName) =>
        {
            var ease = ParseEase(easeName);
            return Eng.Tweens.To(() => e.ScaleX, v => e.ScaleX = v, (float)target, (float)dur, ease);
        });

        script.Globals["tween_scale_y"] = (Func<Entity, double, double, string, TweenHandle>)((e, target, dur, easeName) =>
        {
            var ease = ParseEase(easeName);
            return Eng.Tweens.To(() => e.ScaleY, v => e.ScaleY = v, (float)target, (float)dur, ease);
        });

        script.Globals["tween_angle"] = (Func<Entity, double, double, string, TweenHandle>)((e, target, dur, easeName) =>
        {
            var ease = ParseEase(easeName);
            return Eng.Tweens.To(() => e.Angle, v => e.Angle = v, (float)target, (float)dur, ease);
        });

        script.Globals["tween_alpha"] = (Func<Sprite, double, double, string, TweenHandle>)((s, target, dur, easeName) =>
        {
            var ease = ParseEase(easeName);
            return Eng.Tweens.To(
                () => (float)s.Color.A,
                v => s.Color = s.Color.WithAlpha((byte)System.Math.Clamp((int)v, 0, 255)),
                (float)target, (float)dur, ease);
        });

        script.Globals["tween_delay"] = (Func<TweenHandle, double, TweenHandle>)((h, delay) => h.Delay((float)delay));
        script.Globals["tween_on_complete"] = (Action<TweenHandle, Closure>)((h, fn) => h.OnComplete(() => fn.Call()));
        script.Globals["cancel_tween"] = (Action<TweenHandle>)((h) => h.Cancel());
        script.Globals["cancel_all_tweens"] = (Action)(() => Eng.Tweens.CancelAll());
    }

    // ── Effects ─────────────────────────────────────────────────

    private static void RegisterEffects(Script script)
    {
        script.Globals["screen_flash"] = (Action<int, int, int, double, int>)((r, g, b, dur, alpha) =>
            Eng.Effects.Flash(B(r), B(g), B(b), (float)dur, B(alpha)));

        script.Globals["screen_freeze"] = (Action<double>)((dur) =>
            Eng.Effects.Freeze((float)dur));

        script.Globals["set_time_scale"] = (Action<double>)((s) => Eng.TimeScale = (float)s);
        script.Globals["get_time_scale"] = (Func<double>)(() => Eng.TimeScale);
    }

    // ── Collision ───────────────────────────────────────────────

    private static void RegisterCollision(Script script)
    {
        script.Globals["overlaps"] = (Func<Entity, Entity, bool>)((a, b) => a.Overlaps(b));

        script.Globals["separate"] = (Func<Entity, Entity, int>)((moving, solid) =>
            (int)Collision.Separate(moving, solid));

        script.Globals["separate_oneway"] = (Func<Entity, Entity, bool>)((moving, platform) =>
            Collision.SeparateOneway(moving, platform));

        script.Globals["distance"] = (Func<Entity, Entity, double>)((a, b) =>
            Vec2.Distance(a.Position, b.Position));

        script.Globals["in_range"] = (Func<Entity, Entity, double, bool>)((a, b, range) =>
            AI.InRange(a, b, (float)range));

        // CollisionDir bit helpers (Lua 5.2 has no bitwise ops)
        // Top=1, Bottom=2, Left=4, Right=8
        script.Globals["hit_top"] = (Func<int, bool>)(dir => (dir & 1) != 0);
        script.Globals["hit_bottom"] = (Func<int, bool>)(dir => (dir & 2) != 0);
        script.Globals["hit_left"] = (Func<int, bool>)(dir => (dir & 4) != 0);
        script.Globals["hit_right"] = (Func<int, bool>)(dir => (dir & 8) != 0);
    }

    // ── Draw Helpers ────────────────────────────────────────────

    private static void RegisterDraw(Script script)
    {
        script.Globals["draw_line"] = (Action<double, double, double, double, int, int, int, int>)(
            (x1, y1, x2, y2, r, g, b, a) =>
                Draw.Line((float)x1, (float)y1, (float)x2, (float)y2, B(r), B(g), B(b), B(a)));

        script.Globals["draw_rect"] = (Action<double, double, double, double, int, int, int, int>)(
            (x, y, w, h, r, g, b, a) =>
                Draw.Rect((float)x, (float)y, (float)w, (float)h, B(r), B(g), B(b), B(a)));

        script.Globals["draw_fill_rect"] = (Action<double, double, double, double, int, int, int, int>)(
            (x, y, w, h, r, g, b, a) =>
                Draw.FillRect((float)x, (float)y, (float)w, (float)h, B(r), B(g), B(b), B(a)));

        script.Globals["draw_circle"] = (Action<double, double, double, int, int, int, int>)(
            (cx, cy, radius, r, g, b, a) =>
                Draw.Circle((float)cx, (float)cy, (float)radius, B(r), B(g), B(b), B(a)));

        script.Globals["draw_fill_circle"] = (Action<double, double, double, int, int, int, int>)(
            (cx, cy, radius, r, g, b, a) =>
                Draw.FillCircle((float)cx, (float)cy, (float)radius, B(r), B(g), B(b), B(a)));

        // Screen-space draw (HUD)
        script.Globals["draw_screen_rect"] = (Action<double, double, double, double, int, int, int, int>)(
            (x, y, w, h, r, g, b, a) =>
                Draw.ScreenFillRect((float)x, (float)y, (float)w, (float)h, B(r), B(g), B(b), B(a)));

        script.Globals["draw_screen_circle"] = (Action<double, double, double, int, int, int, int>)(
            (cx, cy, radius, r, g, b, a) =>
                Eng.GL.FillCircle((float)cx, (float)cy, (float)radius, r / 255f, g / 255f, b / 255f, a / 255f));
    }

    // ── Engine / Scene ──────────────────────────────────────────

    private static void RegisterEngine(Script script, LuaScene scene)
    {
        script.Globals["screen_width"] = (Func<int>)(() => Eng.Width);
        script.Globals["screen_height"] = (Func<int>)(() => Eng.Height);

        script.Globals["set_clear_color"] = (Action<int, int, int>)((r, g, b) =>
        {
            Eng.GL.ClearR = B(r);
            Eng.GL.ClearG = B(g);
            Eng.GL.ClearB = B(b);
        });

        script.Globals["switch_scene"] = (Action<string>)((luaPath) =>
            Eng.SwitchScene(new LuaScene(luaPath)));

        script.Globals["quit"] = (Action)(() => Eng.Quit());

        script.Globals["screenshot"] = (Func<string>)(() => Eng.CaptureScreen());

        script.Globals["trace_start"] = (Action)(() => Eng.Debug.StartTrace());
        script.Globals["trace_stop"] = (Action)(() => Eng.Debug.StopTrace());
        script.Globals["trace_active"] = (Func<bool>)(() => Eng.Debug.Tracing);

        // Cursor hover hint — call when something clickable is under the mouse
        script.Globals["cursor_hoverable"] = (Action)Eng.MarkCursorHoverable;

        // Portal effect
        script.Globals["portal"] = (Func<double, double, double, double, PortalEffect>)((x, y, w, h) =>
        {
            var p = new PortalEffect((float)x, (float)y, (float)w, (float)h);
            scene.AddEntity(p);
            return p;
        });
        script.Globals["portal_config"] = (Action<PortalEffect, int, int, int, int, int, int, double>)(
            (p, r1, g1, b1, r2, g2, b2, speed) =>
            {
                p.ColorA = new Color(B(r1), B(g1), B(b1));
                p.ColorB = new Color(B(r2), B(g2), B(b2));
                p.Speed = (float)speed;
            });
        script.Globals["portal_set_alpha"] = (Action<PortalEffect, int>)((p, a) =>
        {
            p.ColorA = p.ColorA.WithAlpha(B(a));
            p.ColorB = p.ColorB.WithAlpha(B(a));
        });

        // List files in an Assets subdirectory matching a pattern
        script.Globals["list_assets"] = (Func<string, string, Table>)((dir, pattern) =>
        {
            var t = new Table(script);
            var fullDir = System.IO.Path.Combine(Eng.AssetsPath, dir);
            if (System.IO.Directory.Exists(fullDir))
            {
                var files = System.IO.Directory.GetFiles(fullDir, pattern, System.IO.SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                int idx = 1;
                foreach (var f in files)
                {
                    // Return path relative to Assets/
                    var relative = dir + "/" + System.IO.Path.GetFileName(f);
                    t[idx++] = DynValue.NewString(relative);
                }
            }
            return t;
        });

        // Check if a file exists (asset-relative or absolute path)
        script.Globals["asset_exists"] = (Func<string, bool>)((name) =>
            System.IO.Path.IsPathRooted(name) ? System.IO.File.Exists(name) : Eng.TryAsset(name, out _));

        // Print to console (debug)
        script.Globals["log"] = (Action<DynValue>)((val) =>
            Console.WriteLine($"[Lua] {val.ToDebugPrintString()}"));
    }

    // ── Groups ──────────────────────────────────────────────────

    private static void RegisterGroups(Script script, LuaScene scene)
    {
        // Create a group and add it to the scene
        script.Globals["group"] = (Func<Group>)(() =>
        {
            var g = new Group();
            scene.AddEntity(g);
            return g;
        });

        script.Globals["group_add"] = (Action<Group, Entity>)((g, e) => g.Add(e));
        script.Globals["group_remove"] = (Action<Group, Entity, bool>)((g, e, destroy) => g.Remove(e, destroy));

        // Entity that is not a sprite (for hitboxes, triggers, etc.)
        script.Globals["entity"] = (Func<double, double, double, double, Entity>)((x, y, w, h) =>
        {
            var e = new Entity((float)x, (float)y) { BaseWidth = (float)w, BaseHeight = (float)h };
            scene.AddEntity(e);
            return e;
        });

        // Separate against group
        script.Globals["separate_group"] = (Func<Entity, Group, bool, int>)((moving, group, oneWay) =>
            (int)Collision.SeparateGroup(moving, group, oneWay));
    }

    // ── Particles ───────────────────────────────────────────────

    private static void RegisterParticles(Script script, LuaScene scene)
    {
        script.Globals["emitter"] = (Func<double, double, ParticleEmitter>)((x, y) =>
        {
            var e = new ParticleEmitter((float)x, (float)y);
            scene.AddEntity(e);
            return e;
        });

        script.Globals["emit"] = (Action<ParticleEmitter, int>)((e, count) => e.Emit(count));

        script.Globals["emitter_load_texture"] = (Action<ParticleEmitter, string, int, int>)((e, path, fw, fh) =>
        {
            if (fw > 0 && fh > 0)
                e.LoadTexture(Eng.Asset(path), fw, fh);
            else
                e.LoadTexture(Eng.Asset(path));
        });

        // Bulk configure emitter with a table
        script.Globals["emitter_config"] = (Action<ParticleEmitter, Table>)((e, t) =>
        {
            if (t.Get("min_speed_x").Type == DataType.Number) e.MinSpeedX = (float)t.Get("min_speed_x").Number;
            if (t.Get("max_speed_x").Type == DataType.Number) e.MaxSpeedX = (float)t.Get("max_speed_x").Number;
            if (t.Get("min_speed_y").Type == DataType.Number) e.MinSpeedY = (float)t.Get("min_speed_y").Number;
            if (t.Get("max_speed_y").Type == DataType.Number) e.MaxSpeedY = (float)t.Get("max_speed_y").Number;
            if (t.Get("gravity_y").Type == DataType.Number) e.GravityY = (float)t.Get("gravity_y").Number;
            if (t.Get("min_life").Type == DataType.Number) e.MinLife = (float)t.Get("min_life").Number;
            if (t.Get("max_life").Type == DataType.Number) e.MaxLife = (float)t.Get("max_life").Number;
            if (t.Get("min_size").Type == DataType.Number) e.MinSize = (int)t.Get("min_size").Number;
            if (t.Get("max_size").Type == DataType.Number) e.MaxSize = (int)t.Get("max_size").Number;
            if (t.Get("scale_start").Type == DataType.Number) e.ScaleStart = (float)t.Get("scale_start").Number;
            if (t.Get("scale_end").Type == DataType.Number) e.ScaleEnd = (float)t.Get("scale_end").Number;
            if (t.Get("spawn_width").Type == DataType.Number) e.SpawnWidth = (float)t.Get("spawn_width").Number;
            if (t.Get("spawn_height").Type == DataType.Number) e.SpawnHeight = (float)t.Get("spawn_height").Number;
            if (t.Get("min_angular_vel").Type == DataType.Number) e.MinAngularVelocity = (float)t.Get("min_angular_vel").Number;
            if (t.Get("max_angular_vel").Type == DataType.Number) e.MaxAngularVelocity = (float)t.Get("max_angular_vel").Number;
            if (t.Get("blend_additive").Type == DataType.Boolean && t.Get("blend_additive").Boolean)
                e.BlendMode = BlendMode.Additive;

            // Color as {r,g,b,a} tables
            var cmin = t.Get("color_min");
            if (cmin.Type == DataType.Table && cmin.Table.Length >= 3)
            {
                var ct = cmin.Table;
                e.ColorMin = new Color(
                    B(ct.Get(1).Number), B(ct.Get(2).Number),
                    B(ct.Get(3).Number), ct.Length >= 4 ? B(ct.Get(4).Number) : (byte)255);
            }
            var cmax = t.Get("color_max");
            if (cmax.Type == DataType.Table && cmax.Table.Length >= 3)
            {
                var ct = cmax.Table;
                e.ColorMax = new Color(
                    B(ct.Get(1).Number), B(ct.Get(2).Number),
                    B(ct.Get(3).Number), ct.Length >= 4 ? B(ct.Get(4).Number) : (byte)255);
            }
        });
    }

    // ── Rain ────────────────────────────────────────────────────

    private static void RegisterRain(Script script, LuaScene scene)
    {
        script.Globals["rain"] = (Func<Rain>)(() =>
        {
            var r = new Rain();
            scene.AddEntity(r);
            return r;
        });

        script.Globals["rain_config"] = (Action<Rain, Table>)((r, t) =>
        {
            if (t.Get("intensity").Type == DataType.Number) r.Intensity = (float)t.Get("intensity").Number;
            if (t.Get("max_drops").Type == DataType.Number) r.SetMaxDrops((int)t.Get("max_drops").Number);
            if (t.Get("wind_angle").Type == DataType.Number) r.WindAngle = (float)t.Get("wind_angle").Number;
            if (t.Get("min_speed").Type == DataType.Number) r.MinSpeed = (float)t.Get("min_speed").Number;
            if (t.Get("max_speed").Type == DataType.Number) r.MaxSpeed = (float)t.Get("max_speed").Number;
            if (t.Get("min_length").Type == DataType.Number) r.MinLength = (float)t.Get("min_length").Number;
            if (t.Get("max_length").Type == DataType.Number) r.MaxLength = (float)t.Get("max_length").Number;
            if (t.Get("thickness").Type == DataType.Number) r.Thickness = (float)t.Get("thickness").Number;
            if (t.Get("ground_y").Type == DataType.Number) r.GroundY = (float)t.Get("ground_y").Number;
            if (t.Get("margin").Type == DataType.Number) r.Margin = (float)t.Get("margin").Number;
            if (t.Get("splashes").Type == DataType.Boolean) r.EnableSplashes = t.Get("splashes").Boolean;
            if (t.Get("layer").Type == DataType.Number) r.Layer = (int)t.Get("layer").Number;

            var cf = t.Get("color_front");
            if (cf.Type == DataType.Table && cf.Table.Length >= 3)
            {
                var ct = cf.Table;
                r.ColorFront = new Color(
                    B(ct.Get(1).Number), B(ct.Get(2).Number),
                    B(ct.Get(3).Number), ct.Length >= 4 ? B(ct.Get(4).Number) : (byte)160);
            }
            var cb = t.Get("color_tail");
            if (cb.Type == DataType.Table && cb.Table.Length >= 3)
            {
                var ct = cb.Table;
                r.ColorTail = new Color(
                    B(ct.Get(1).Number), B(ct.Get(2).Number),
                    B(ct.Get(3).Number), ct.Length >= 4 ? B(ct.Get(4).Number) : (byte)40);
            }
        });

        script.Globals["rain_add_water"] = (Action<Rain, Physics.WaterBody>)((r, w) => r.AddWaterBody(w));
        script.Globals["rain_remove_water"] = (Action<Rain, Physics.WaterBody>)((r, w) => r.RemoveWaterBody(w));
        script.Globals["rain_add_obstacle"] = (Action<Rain, Entity>)((r, e) => r.AddObstacle(e));
        script.Globals["rain_remove_obstacle"] = (Action<Rain, Entity>)((r, e) => r.RemoveObstacle(e));
    }

    // ── AfterImage ─────────────────────────────────────────────

    private static void RegisterAfterImage(Script script, LuaScene scene)
    {
        script.Globals["after_image"] = (Func<Sprite, AfterImage>)((target) =>
        {
            var ai = new AfterImage();
            ai.SetTarget(target);
            ai.Layer = target.Layer - 1; // draw behind target
            scene.AddEntity(ai);
            return ai;
        });

        script.Globals["after_image_config"] = (Action<AfterImage, Table>)((ai, t) =>
        {
            if (t.Get("interval").Type == DataType.Number) ai.Interval = (float)t.Get("interval").Number;
            if (t.Get("duration").Type == DataType.Number) ai.Duration = (float)t.Get("duration").Number;
            if (t.Get("max_ghosts").Type == DataType.Number)
            {
                ai.MaxGhosts = (int)t.Get("max_ghosts").Number;
            }
            if (t.Get("start_alpha").Type == DataType.Number) ai.StartAlpha = (float)t.Get("start_alpha").Number;
            if (t.Get("layer").Type == DataType.Number) ai.Layer = (int)t.Get("layer").Number;

            var ct = t.Get("tint");
            if (ct.Type == DataType.Table && ct.Table.Length >= 3)
            {
                var c = ct.Table;
                ai.Tint = new Color(
                    B(c.Get(1).Number), B(c.Get(2).Number),
                    B(c.Get(3).Number), c.Length >= 4 ? B(c.Get(4).Number) : (byte)255);
            }
        });

        script.Globals["after_image_clear"] = (Action<AfterImage>)((ai) => ai.Clear());
        script.Globals["after_image_set_target"] = (Action<AfterImage, Sprite>)((ai, s) => ai.SetTarget(s));
    }

    // ── Level Loading ───────────────────────────────────────────

    private static void RegisterLevel(Script script, LuaScene scene)
    {
        // level_load(path) -> spawn_x, spawn_y, solids_group, platforms_group, tilemap, level_width
        script.Globals["level_load"] = (Func<string, DynValue>)((path) =>
        {
            var spawn = LevelLoader.Load(scene, path, out var solids, out var platforms, out var tilemap);
            var (levelW, _) = LevelLoader.GetSize(path);

            // Return as a table with named fields
            var lua = script;
            var result = new Table(lua);
            result["spawn_x"] = DynValue.NewNumber(spawn.X);
            result["spawn_y"] = DynValue.NewNumber(spawn.Y);
            result["solids"] = DynValue.FromObject(lua, solids);
            result["platforms"] = DynValue.FromObject(lua, platforms);
            if (tilemap != null)
                result["tilemap"] = DynValue.FromObject(lua, tilemap);
            result["width"] = DynValue.NewNumber(levelW);
            return DynValue.NewTable(result);
        });

        // Tilemap collision
        script.Globals["tilemap_collide"] = (Func<Tilemap, Entity, int>)((tm, e) =>
            (int)tm.CollideEntity(e));

        script.Globals["tilemap_collide_oneway"] = (Func<Tilemap, Entity, bool>)((tm, e) =>
            tm.CollideEntityOneway(e));

        // Level size without loading
        script.Globals["level_size"] = (Func<string, Table>)((path) =>
        {
            var (w, h) = LevelLoader.GetSize(path);
            var t = new Table(script);
            t["width"] = DynValue.NewNumber(w);
            t["height"] = DynValue.NewNumber(h);
            return t;
        });
    }

    // ── Dialogue / Sequences ────────────────────────────────────

    private static void RegisterDialogue(Script script, LuaScene scene)
    {
        // dialogue() -> creates a new Dialogue builder
        script.Globals["dialogue"] = (Func<Dialogue>)(() => new Dialogue());
        script.Globals["dialogue_say"] = (Func<Dialogue, string, string, Dialogue>)(
            (d, text, speaker) => d.Say(text, speaker));
        script.Globals["dialogue_ask"] = (Func<Dialogue, string, string, Table, Dialogue>)(
            (d, text, speaker, choices) =>
            {
                var choiceList = new List<(string, string)>();
                if (choices != null)
                {
                    foreach (var pair in choices.Pairs)
                    {
                        if (pair.Key.Type == DataType.String && pair.Value.Type == DataType.String)
                            choiceList.Add((pair.Key.String, pair.Value.String));
                    }
                }
                return d.Ask(text, speaker, choiceList.ToArray());
            });
        script.Globals["dialogue_label"] = (Func<Dialogue, string, Dialogue>)(
            (d, name) => d.Label(name));
        script.Globals["dialogue_goto"] = (Func<Dialogue, string, Dialogue>)(
            (d, label) => d.Goto(label));
        script.Globals["dialogue_call"] = (Func<Dialogue, Closure, Dialogue>)(
            (d, fn) => d.Call(() => fn.Call()));

        // DialogueBox
        script.Globals["dialogue_box"] = (Func<DialogueBox>)(() =>
        {
            var box = new DialogueBox();
            scene.AddEntity(box);
            return box;
        });
        script.Globals["dialogue_box_start"] = (Action<DialogueBox, Dialogue>)(
            (box, d) => box.Start(d));
        script.Globals["dialogue_box_stop"] = (Action<DialogueBox>)(box => box.Stop());
        script.Globals["dialogue_box_active"] = (Func<DialogueBox, bool>)(box => box.IsActive);
        script.Globals["dialogue_box_config"] = (Action<DialogueBox, Table>)((box, t) =>
        {
            if (t.Get("height").Type == DataType.Number) box.BoxHeight = (int)t.Get("height").Number;
            if (t.Get("font_size").Type == DataType.Number) box.FontSize = (int)t.Get("font_size").Number;
            if (t.Get("typing_speed").Type == DataType.Number) box.TypingSpeed = (float)t.Get("typing_speed").Number;
        });
        script.Globals["dialogue_box_on_complete"] = (Action<DialogueBox, Closure>)(
            (box, fn) => box.OnComplete = () => fn.Call());

        // Sequence
        script.Globals["sequence"] = (Func<Sequence>)(() => new Sequence());
        script.Globals["seq_call"] = (Func<Sequence, Closure, Sequence>)(
            (s, fn) => s.Call(() => fn.Call()));
        script.Globals["seq_wait"] = (Func<Sequence, double, Sequence>)(
            (s, dur) => s.Wait((float)dur));
        script.Globals["seq_tween_float"] = (Func<Sequence, Closure, Closure, double, double, string, Sequence>)(
            (s, getter, setter, target, dur, ease) =>
                s.TweenFloat(
                    () => (float)getter.Call().Number,
                    v => setter.Call(DynValue.NewNumber(v)),
                    (float)target, (float)dur, ParseEase(ease)));
        script.Globals["seq_start"] = (Func<Sequence, Sequence>)(s => s.Start());
        script.Globals["seq_cancel"] = (Action<Sequence>)(s => s.Cancel());
        script.Globals["seq_on_complete"] = (Action<Sequence, Closure>)(
            (s, fn) => s.OnComplete(() => fn.Call()));
    }

    // ── Advanced Sprite ─────────────────────────────────────────

    private static void RegisterSpriteAdvanced(Script script)
    {
        // Load new spritesheet/animation on existing sprite
        script.Globals["load_meta"] = (Action<Sprite, string>)((s, path) => s.LoadFromMeta(path));
        script.Globals["load_graphic"] = (Action<Sprite, string>)((s, path) => s.LoadGraphic(Eng.Asset(path)));

        // Frame control
        script.Globals["set_frame"] = (Action<Sprite, int, bool>)((s, idx, pause) => s.SetFrame(idx, pause));
        script.Globals["get_frame_index"] = (Func<Sprite, int>)(s => s.CurrentFrameIndex);
        script.Globals["get_frame_count"] = (Func<Sprite, int>)(s => s.CurrentFrameCount);
        script.Globals["tick_animation"] = (Action<Sprite, double>)((s, dt) => s.TickAnimation((float)dt));

        // Collision bounds
        script.Globals["get_bounds"] = (Func<Entity, Table>)((e) =>
        {
            var b = e.GetCollisionBounds();
            var t = new Table(script);
            t["x"] = DynValue.NewNumber(b.X);
            t["y"] = DynValue.NewNumber(b.Y);
            t["w"] = DynValue.NewNumber(b.W);
            t["h"] = DynValue.NewNumber(b.H);
            return t;
        });

        // Damage box
        script.Globals["get_damage_box"] = (Func<Sprite, DynValue>)((s) =>
        {
            var db = s.GetDamageBox();
            if (!db.HasValue) return DynValue.Nil;
            var t = new Table(script);
            t["x"] = DynValue.NewNumber(db.Value.X);
            t["y"] = DynValue.NewNumber(db.Value.Y);
            t["w"] = DynValue.NewNumber(db.Value.W);
            t["h"] = DynValue.NewNumber(db.Value.H);
            return DynValue.NewTable(t);
        });

        // Base dimensions (for plain entities like hitboxes)
        script.Globals["set_base_size"] = (Action<Entity, double, double>)((e, w, h) =>
        {
            e.BaseWidth = (float)w;
            e.BaseHeight = (float)h;
        });

        // Clip masking (for cutscenes)
        script.Globals["set_clip"] = (Action<Sprite, double, double>)((s, clipX, clipDir) =>
        {
            s.ClipX = (float)clipX;
            s.ClipDir = (float)clipDir;
        });

        // Flash with color
        script.Globals["flash_color"] = (Action<Sprite, double, int, int, int>)((s, dur, r, g, b) =>
            s.Flash((float)dur, new Color(B(r), B(g), B(b))));

        // On-frame callback
        script.Globals["on_frame"] = (Action<Sprite, string, int, Closure>)((s, anim, frame, fn) =>
            s.OnFrame(anim, frame, () => fn.Call()));

        // Angular velocity (KinematicEntity)
        script.Globals["set_angular_vel"] = (Action<KinematicEntity, double>)((e, v) =>
            e.AngularVelocity = (float)v);
    }

    // ── Fluid System ────────────────────────────────────────────

    private static void RegisterFluid(Script script, LuaScene scene)
    {
        // fluid_system(capacity?) -> create fluid simulation
        script.Globals["fluid_system"] = (Func<int, FluidSystem>)((capacity) =>
        {
            var fs = new FluidSystem(capacity > 0 ? capacity : 512);
            scene.AddEntity(fs);
            return fs;
        });

        // Bounds
        script.Globals["fluid_set_bounds"] = (Action<FluidSystem, double, double, double, double>)(
            (fs, left, top, right, bottom) =>
            {
                fs.BoundsLeft = (float)left;
                fs.BoundsTop = (float)top;
                fs.BoundsRight = (float)right;
                fs.BoundsBottom = (float)bottom;
            });

        // Pouring
        script.Globals["fluid_pour"] = (Action<FluidSystem, bool, double, double>)(
            (fs, enabled, x, y) =>
            {
                fs.Pouring = enabled;
                fs.PourPosition = new Vec2((float)x, (float)y);
            });

        script.Globals["fluid_pour_config"] = (Action<FluidSystem, Table>)((fs, t) =>
        {
            if (t.Get("rate").Type == DataType.Number) fs.PourRate = (float)t.Get("rate").Number;
            if (t.Get("spread").Type == DataType.Number) fs.PourSpread = (float)t.Get("spread").Number;
            if (t.Get("vel_x").Type == DataType.Number) fs.PourVelocity = new Vec2((float)t.Get("vel_x").Number, fs.PourVelocity.Y);
            if (t.Get("vel_y").Type == DataType.Number) fs.PourVelocity = new Vec2(fs.PourVelocity.X, (float)t.Get("vel_y").Number);
        });

        // Emit particles manually
        script.Globals["fluid_emit"] = (Action<FluidSystem, double, double, double, double>)(
            (fs, x, y, vx, vy) => fs.Emit(new Vec2((float)x, (float)y), new Vec2((float)vx, (float)vy)));

        script.Globals["fluid_emit_burst"] = (Action<FluidSystem, double, double, double, double, int, double>)(
            (fs, x, y, vx, vy, count, spread) =>
                fs.Emit(new Vec2((float)x, (float)y), new Vec2((float)vx, (float)vy), count, (float)spread));

        // Clear all particles
        script.Globals["fluid_clear"] = (Action<FluidSystem>)(fs => fs.ClearParticles());

        // Query
        script.Globals["fluid_count"] = (Func<FluidSystem, int>)(fs => fs.ActiveCount);

        // Physics parameters
        script.Globals["fluid_config"] = (Action<FluidSystem, Table>)((fs, t) =>
        {
            if (t.Get("gravity_x").Type == DataType.Number) fs.Gravity = new Vec2((float)t.Get("gravity_x").Number, fs.Gravity.Y);
            if (t.Get("gravity_y").Type == DataType.Number) fs.Gravity = new Vec2(fs.Gravity.X, (float)t.Get("gravity_y").Number);
            if (t.Get("max_speed").Type == DataType.Number) fs.MaxSpeed = (float)t.Get("max_speed").Number;
            if (t.Get("damping").Type == DataType.Number) fs.BoundaryDamping = (float)t.Get("damping").Number;
            if (t.Get("viscosity").Type == DataType.Number) fs.XSPHViscosity = (float)t.Get("viscosity").Number;
            if (t.Get("surface_tension").Type == DataType.Number) fs.SurfaceTensionK = (float)t.Get("surface_tension").Number;
            if (t.Get("substeps").Type == DataType.Number) fs.SubSteps = (int)t.Get("substeps").Number;
            if (t.Get("solver_iters").Type == DataType.Number) fs.SolverIterations = (int)t.Get("solver_iters").Number;
            if (t.Get("particle_budget").Type == DataType.Number) fs.ParticleBudget = (int)t.Get("particle_budget").Number;
            if (t.Get("native").Type == DataType.Boolean) fs.UseNativeSolver = t.Get("native").Boolean;
            if (t.Get("particle_mass").Type == DataType.Number) fs.ParticleMass = (float)t.Get("particle_mass").Number;
            if (t.Get("wcsph").Type == DataType.Boolean) fs.UseWCSPH = t.Get("wcsph").Boolean;
            if (t.Get("gas_constant").Type == DataType.Number) fs.GasConstant = (float)t.Get("gas_constant").Number;
            if (t.Get("wcsph_viscosity").Type == DataType.Number) fs.WCSPHViscosity = (float)t.Get("wcsph_viscosity").Number;
            if (t.Get("wcsph_surface_tension").Type == DataType.Number) fs.WCSPHSurfaceTension = (float)t.Get("wcsph_surface_tension").Number;
            if (t.Get("damping").Type == DataType.Number) fs.Damping = (float)t.Get("damping").Number;
            if (t.Get("rest_density").Type == DataType.Number) fs.RestDensity = (float)t.Get("rest_density").Number;
            if (t.Get("merge_interval").Type == DataType.Number) fs.MergeInterval = (int)t.Get("merge_interval").Number;
            if (t.Get("rest_density").Type == DataType.Number) fs.RestDensity = (float)t.Get("rest_density").Number;
            if (t.Get("relaxation").Type == DataType.Number) fs.Relaxation = (float)t.Get("relaxation").Number;
            if (t.Get("smoothing_radius").Type == DataType.Number)
            {
                float h = (float)t.Get("smoothing_radius").Number;
                fs.SmoothingRadius = h;
                FluidKernels.SetSmoothingRadius(h);
                // Recompute rest density for new radius (unless explicitly set)
                if (t.Get("rest_density").Type != DataType.Number)
                {
                    float spacing = h * 0.5f;
                    fs.RestDensity = fs.ParticleMass * FluidKernels.Poly6(0);
                    fs.RestDensity += 6f * fs.ParticleMass * FluidKernels.Poly6(spacing * spacing);
                }
            }
        });

        // Render appearance
        script.Globals["fluid_set_color"] = (Action<FluidSystem, int, int, int, int>)(
            (fs, r, g, b, a) => fs.Renderer.ParticleColor = new Color(B(r), B(g), B(b), B(a)));

        script.Globals["fluid_set_particle_size"] = (Action<FluidSystem, double>)(
            (fs, size) => fs.Renderer.ParticleRadius = (float)size);
    }

    // ── Grid Fluid (Stam Stable Fluids) ───────────────────────

    private static void RegisterGridFluid(Script script, LuaScene scene)
    {
        // grid_fluid(gridW, gridH, pixelW, pixelH, x?, y?)
        script.Globals["grid_fluid"] = (Func<int, int, double, double, double, double, GridFluidSystem>)(
            (gw, gh, pw, ph, x, y) =>
            {
                var gf = new GridFluidSystem(gw, gh, (float)pw, (float)ph, (float)x, (float)y);
                scene.AddEntity(gf);
                return gf;
            });

        // Add water at a grid cell
        script.Globals["grid_fluid_add"] = (Action<GridFluidSystem, int, int, double>)(
            (gf, i, j, amount) => gf.AddWater(i, j, (float)amount));

        // Add water in a circle
        script.Globals["grid_fluid_pour"] = (Action<GridFluidSystem, int, int, int, double>)(
            (gf, i, j, radius, amount) => gf.AddWaterCircle(i, j, radius, (float)amount));

        // Apply velocity to water (push it around)
        script.Globals["grid_fluid_push"] = (Action<GridFluidSystem, int, int, int, double, double>)(
            (gf, i, j, radius, vx, vy) => gf.AddVelocity(i, j, radius, (float)vx, (float)vy));

        // Remove water in a radius (carve)
        script.Globals["grid_fluid_remove"] = (Action<GridFluidSystem, int, int, int>)(
            (gf, i, j, radius) => gf.RemoveWater(i, j, radius));

        // Convert pixel coords to grid cell
        script.Globals["grid_fluid_pixel_to_grid"] = (Func<GridFluidSystem, double, double, Table>)(
            (gf, px, py) =>
            {
                var (i, j) = gf.PixelToGrid((float)px, (float)py);
                var t = new Table(script);
                t["i"] = DynValue.NewNumber(i);
                t["j"] = DynValue.NewNumber(j);
                return t;
            });

        // Configure
        script.Globals["grid_fluid_config"] = (Action<GridFluidSystem, Table>)((gf, t) =>
        {
            if (t.Get("flow_rate").Type == DataType.Number) gf.FlowRate = (float)t.Get("flow_rate").Number;
            if (t.Get("spread_rate").Type == DataType.Number) gf.SpreadRate = (float)t.Get("spread_rate").Number;
            if (t.Get("max_level").Type == DataType.Number) gf.MaxLevel = (float)t.Get("max_level").Number;
            if (t.Get("passes").Type == DataType.Number) gf.Passes = (int)t.Get("passes").Number;
            if (t.Get("threshold").Type == DataType.Number) gf.RenderThreshold = (float)t.Get("threshold").Number;
            if (t.Get("solid_render").Type == DataType.Boolean) gf.SolidRender = t.Get("solid_render").Boolean;
        });

        // Set colors
        script.Globals["grid_fluid_set_color"] = (Action<GridFluidSystem, int, int, int, int>)(
            (gf, r, g, b, a) => gf.FluidColor = new Color(B(r), B(g), B(b), B(a)));

        script.Globals["grid_fluid_set_deep_color"] = (Action<GridFluidSystem, int, int, int, int>)(
            (gf, r, g, b, a) => gf.DeepColor = new Color(B(r), B(g), B(b), B(a)));

        script.Globals["grid_fluid_set_surface_color"] = (Action<GridFluidSystem, int, int, int, int>)(
            (gf, r, g, b, a) => gf.SurfaceColor = new Color(B(r), B(g), B(b), B(a)));

        script.Globals["grid_fluid_visuals"] = (Action<GridFluidSystem, Table>)((gf, t) =>
        {
            if (t.Get("surface_thickness").Type == DataType.Number) gf.SurfaceThickness = (float)t.Get("surface_thickness").Number;
            if (t.Get("wave").Type == DataType.Boolean) gf.WaveEnabled = t.Get("wave").Boolean;
            if (t.Get("wave_amplitude").Type == DataType.Number) gf.WaveAmplitude = (float)t.Get("wave_amplitude").Number;
            if (t.Get("wave_frequency").Type == DataType.Number) gf.WaveFrequency = (float)t.Get("wave_frequency").Number;
            if (t.Get("splash_amount").Type == DataType.Number) gf.SplashAmount = (int)t.Get("splash_amount").Number;
            if (t.Get("disturbance_decay").Type == DataType.Number) gf.DisturbanceDecay = (float)t.Get("disturbance_decay").Number;
            if (t.Get("disturbance_strength").Type == DataType.Number) gf.DisturbanceStrength = (float)t.Get("disturbance_strength").Number;
        });

        // Attach splash particle emitter to the fluid
        script.Globals["grid_fluid_set_splash_emitter"] = (Action<GridFluidSystem, ParticleEmitter>)(
            (gf, emitter) => gf.SplashEmitter = emitter);

        // Query
        script.Globals["grid_fluid_total"] = (Func<GridFluidSystem, double>)(gf => gf.TotalWater());
        script.Globals["grid_fluid_clear"] = (Action<GridFluidSystem>)(gf => gf.Clear());
    }

    // ── AI Helpers ──────────────────────────────────────────────

    private static void RegisterAI(Script script)
    {
        script.Globals["move_toward"] = (Func<KinematicEntity, double, double, double, double, bool>)(
            (e, tx, ty, speed, arrive) =>
                AI.MoveToward(e, (float)tx, (float)ty, (float)speed, (float)arrive));

        script.Globals["move_toward_entity"] = (Action<KinematicEntity, Entity, double>)(
            (e, target, speed) => AI.MoveToward(e, target, (float)speed));

        script.Globals["move_away"] = (Action<KinematicEntity, double, double, double>)(
            (e, fx, fy, speed) => AI.MoveAway(e, (float)fx, (float)fy, (float)speed));

        script.Globals["face_toward"] = (Action<Sprite, double>)(
            (s, targetX) => AI.FaceToward(s, (float)targetX));

        script.Globals["has_line_of_sight"] = (Func<double, double, double, double, Tilemap, bool>)(
            (x1, y1, x2, y2, tm) =>
                AI.HasLineOfSight((float)x1, (float)y1, (float)x2, (float)y2, tm));
    }

    // ── Media (GIF / Video) ───────────────────────────────────

    private static void RegisterMedia(Script script, LuaScene scene)
    {
        // gif(path, x, y) -> animated GIF entity
        script.Globals["gif"] = (Func<string, double, double, GifSprite>)((path, x, y) =>
        {
            var g = new GifSprite((float)x, (float)y);
            g.LoadGif(path);
            scene.AddEntity(g);
            return g;
        });

        script.Globals["gif_play"] = (Action<GifSprite>)(g => g.Play());
        script.Globals["gif_pause"] = (Action<GifSprite>)(g => g.Pause());
        script.Globals["gif_stop"] = (Action<GifSprite>)(g => g.Stop());
        script.Globals["gif_set_frame"] = (Action<GifSprite, int>)((g, f) => g.SetFrame(f));
        script.Globals["gif_set_speed"] = (Action<GifSprite, double>)((g, s) => g.Speed = (float)s);
        script.Globals["gif_set_looped"] = (Action<GifSprite, bool>)((g, l) => g.Looped = l);
        script.Globals["gif_finished"] = (Func<GifSprite, bool>)(g => g.Finished);
        script.Globals["gif_frame_count"] = (Func<GifSprite, int>)(g => g.FrameCount);

        // video(path, x, y) -> video playback entity (requires video_player.dll + FFmpeg)
        script.Globals["video"] = (Func<string, double, double, VideoSprite>)((path, x, y) =>
        {
            var v = new VideoSprite((float)x, (float)y);
            v.LoadVideo(path);
            scene.AddEntity(v);
            return v;
        });

        script.Globals["video_play"] = (Action<VideoSprite>)(v => v.Play());
        script.Globals["video_pause"] = (Action<VideoSprite>)(v => v.Pause());
        script.Globals["video_stop"] = (Action<VideoSprite>)(v => v.Stop());
        script.Globals["video_seek"] = (Action<VideoSprite, double>)((v, s) => v.Seek(s));
        script.Globals["video_set_speed"] = (Action<VideoSprite, double>)((v, s) => v.Speed = (float)s);
        script.Globals["video_set_looped"] = (Action<VideoSprite, bool>)((v, l) => v.Looped = l);
        script.Globals["video_finished"] = (Func<VideoSprite, bool>)(v => v.Finished);
        script.Globals["video_duration"] = (Func<VideoSprite, double>)(v => v.Duration);
        script.Globals["video_time"] = (Func<VideoSprite, double>)(v => v.CurrentTime);
        script.Globals["video_set_muted"] = (Action<VideoSprite, bool>)((v, m) => v.Muted = m);
    }

    // ── Asset Preloading ──────────────────────────────────────

    private static void RegisterAssetLoader(Script script)
    {
        // Enqueue assets for background loading
        script.Globals["preload"] = (Action<Table>)((paths) =>
        {
            var loader = Eng.Loader;
            for (int i = 1; i <= paths.Length; i++)
            {
                var val = paths.Get(i);
                if (val.Type == DataType.String)
                    loader.Enqueue(val.String);
            }
        });

        // Enqueue with auto type detection
        script.Globals["preload_all"] = (Action<Table>)((paths) =>
        {
            var loader = Eng.Loader;
            for (int i = 1; i <= paths.Length; i++)
            {
                var val = paths.Get(i);
                if (val.Type == DataType.String)
                    loader.EnqueueAll(val.String);
            }
        });

        // Enqueue everything in Assets folder
        script.Globals["preload_assets"] = (Action)(() => Eng.Loader.EnqueueAllAssets());

        // Start background loading
        script.Globals["preload_start"] = (Action)(() => Eng.Loader.Start());

        // Process GPU uploads (call each frame during loading screen)
        script.Globals["preload_process"] = (Func<int, int>)((max) => Eng.Loader.ProcessUploads(max > 0 ? max : 2));

        // Query progress
        script.Globals["preload_progress"] = (Func<double>)(() => Eng.Loader.Progress);
        script.Globals["preload_done"] = (Func<bool>)(() => Eng.Loader.Done);
        script.Globals["preload_remaining"] = (Func<int>)(() => Eng.Loader.Remaining);

        // Reset for reuse
        script.Globals["preload_reset"] = (Action)(() => Eng.Loader.Reset());
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static Ease ParseEase(string? name)
    {
        if (string.IsNullOrEmpty(name)) return Ease.Linear;
        return name.ToLowerInvariant() switch
        {
            "linear" => Ease.Linear,
            "inquad" => Ease.InQuad,
            "outquad" => Ease.OutQuad,
            "inoutquad" => Ease.InOutQuad,
            "incubic" => Ease.InCubic,
            "outcubic" => Ease.OutCubic,
            "inoutcubic" => Ease.InOutCubic,
            "inquart" => Ease.InQuart,
            "outquart" => Ease.OutQuart,
            "inoutquart" => Ease.InOutQuart,
            "inquint" => Ease.InQuint,
            "outquint" => Ease.OutQuint,
            "inoutquint" => Ease.InOutQuint,
            "insine" => Ease.InSine,
            "outsine" => Ease.OutSine,
            "inoutsine" => Ease.InOutSine,
            "inexpo" => Ease.InExpo,
            "outexpo" => Ease.OutExpo,
            "inoutexpo" => Ease.InOutExpo,
            "incirc" => Ease.InCirc,
            "outcirc" => Ease.OutCirc,
            "inoutcirc" => Ease.InOutCirc,
            "inback" => Ease.InBack,
            "outback" => Ease.OutBack,
            "inoutback" => Ease.InOutBack,
            "inbounce" => Ease.InBounce,
            "outbounce" => Ease.OutBounce,
            "inoutbounce" => Ease.InOutBounce,
            "inelastic" => Ease.InElastic,
            "outelastic" => Ease.OutElastic,
            "inoutelastic" => Ease.InOutElastic,
            _ => Ease.Linear,
        };
    }

    /// <summary>Clamp an int to byte range (0-255) for safe color conversion.</summary>
    private static byte B(int v) => (byte)System.Math.Clamp(v, 0, 255);

    /// <summary>Clamp a double to byte range for color values from Lua.</summary>
    private static byte B(double v) => (byte)System.Math.Clamp((int)v, 0, 255);
}
