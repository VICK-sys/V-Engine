# V-Engine Lua API Reference

> Complete reference for Lua game scripting.

## Lua Scripting (MoonSharp)

Lua scripting support via MoonSharp (Lua 5.2 interpreter, pure C#, no native deps).

### Getting Started
```csharp
// In your C# entry point:
var game = new Game("My Game", 800, 480);
game.Start(new LuaScene("scripts/game.lua"));
```

Place `.lua` files in the application output under `Assets/scripts/`.
`Eng.Asset()` resolves script paths from that directory.

### LuaScene (Scripting/LuaScene.cs)
- Loads a `.lua` file and calls Lua lifecycle functions:
  - `create()` -- called once when scene starts
  - `update(dt)` -- called each fixed timestep (after entity updates)
  - `draw()` -- called each frame (after entity draws, for custom drawing)
- All engine API is available as global Lua functions
- Errors are caught and logged to console (won't crash the game)
- Switch between Lua scenes: `switch_scene("scripts/menu.lua")`

### LuaBehavior (Scripting/LuaBehavior.cs)
- Per-entity scripted logic via Lua tables
- Attach from Lua with `add_behavior(entity, table)`
- Table callbacks: `on_attach(self)`, `update(self, dt)`, `on_destroy(self)`
- `self.owner` is the entity the behavior is attached to

### Lua API Reference

**Entity Creation:**
- `sprite(path, x, y)` -- create sprite (auto-detects .json metadata files)
- `sprite_sheet(path, x, y, frameW, frameH)` -- create from spritesheet
- `rect(w, h, x, y, r, g, b, a)` -- colored rectangle (byte 0-255)
- `text(content, x, y, size?)` -- text entity (default font, size default 16)
- `destroy(entity)` -- remove and destroy an entity

**Entity Properties:**
- `get_x(e)` / `get_y(e)` / `set_x(e, v)` / `set_y(e, v)` / `set_pos(e, x, y)`
- `get_vx(e)` / `get_vy(e)` / `set_vx(e, v)` / `set_vy(e, v)` / `set_vel(e, x, y)` -- velocity (KinematicEntity)
- `set_accel(e, ax, ay)` -- acceleration
- `set_scale(e, sx, sy)` / `get_width(e)` / `get_height(e)`
- `get_angle(e)` / `set_angle(e, v)`
- `set_visible(e, bool)` / `set_active(e, bool)` / `is_active(e)`
- `set_layer(e, n)` / `set_zorder(e, v)` / `set_scroll(e, sx, sy)`
- `set_immovable(e, bool)`

**Sprite:**
- `play(sprite, animName, restart?)` -- play animation
- `set_flip_x(s, bool)` / `set_flip_y(s, bool)` / `get_flip_x(s)`
- `anim_finished(s)` / `current_anim(s)` / `set_anim_speed(s, v)`
- `set_color(s, r, g, b, a)` / `set_alpha(s, a)` -- byte 0-255
- `flash(sprite, duration)` -- hit feedback flash
- `set_outline(s, thickness, r, g, b, a)` -- pixel outline
- `set_silhouette(s, on, r, g, b)` -- solid color mode

**Text:**
- `set_text(t, content)` / `set_text_color(t, r, g, b, a)`

**Input (action-based via InputMap):**
- `pressed(action)` / `down(action)` / `released(action)` / `axis(name)`
- `bind(action, keyName)` -- e.g. `bind("jump", "SPACE")`
- `bind_axis(name, posKey, negKey)` -- e.g. `bind_axis("moveX", "D", "A")`

**Input (raw keyboard):**
- `key_down(keyName)` / `key_pressed(keyName)` / `key_released(keyName)`
- Key names: `SPACE`, `LEFT`, `RIGHT`, `UP`, `DOWN`, `A`-`Z`, `RETURN`, `ESCAPE`, etc.

**Mouse:**
- `mouse_x()` / `mouse_y()` / `mouse_world_x()` / `mouse_world_y()`
- `mouse_down(btn)` / `mouse_pressed(btn)` / `mouse_released(btn)` -- btn: 0=left, 1=middle, 2=right

**Gamepad:**
- `gamepad_connected()` / `gamepad_down(btnName)` / `gamepad_pressed(btnName)`
- `left_stick_x()` / `left_stick_y()`

**Audio:**
- `sound(path, volume)` -- play sound effect (returns channel)
- `sound_at(path, worldX, worldY, volume, maxDist)` -- spatial sound
- `music(path, loop, fadeInMs)` / `stop_music(fadeOutMs)`
- `stop_sound(channel)` / `set_volume(v)` / `set_sound_volume(v)` / `set_music_volume(v)`

**Camera:**
- `camera_follow(entity, lerp)` / `camera_unfollow()`
- `camera_focus(x, y)` -- snap to position
- `camera_shake(intensity, duration)` / `camera_zoom(z)` / `get_camera_zoom()`
- `camera_bounds(minX, minY, maxX, maxY)` / `camera_clear_bounds()`
- `get_camera_x()` / `get_camera_y()`

**Timers:**
- `after(delay, function)` -- call once after delay (returns handle)
- `every(interval, function)` -- call repeatedly (returns handle)
- `cancel_timer(handle)` / `cancel_all_timers()`

**Tweens:**
- `tween_x(entity, target, duration, ease?)` -- tween position X
- `tween_y(entity, target, duration, ease?)`
- `tween_scale_x(entity, target, duration, ease?)`
- `tween_scale_y(entity, target, duration, ease?)`
- `tween_angle(entity, target, duration, ease?)`
- `tween_alpha(sprite, target, duration, ease?)` -- alpha 0-255
- `tween_delay(handle, seconds)` / `tween_on_complete(handle, fn)` / `cancel_tween(handle)`
- Ease names: `"linear"`, `"inquad"`, `"outquad"`, `"inoutquad"`, `"incubic"`, `"outcubic"`, `"inoutcubic"`, `"inback"`, `"outback"`, `"outbounce"`, `"outelastic"`, etc.

**Effects:**
- `screen_flash(r, g, b, duration, alpha)` -- byte 0-255
- `screen_freeze(duration)` -- freeze game
- `set_time_scale(s)` / `get_time_scale()`

**Collision:**
- `overlaps(a, b)` -- AABB overlap check (bool)
- `separate(moving, solid)` -- push apart, returns CollisionDir as int
- `separate_oneway(moving, platform)` -- one-way platform (bool)
- `distance(a, b)` / `in_range(a, b, range)`

**Drawing (world-space, call from draw):**
- `draw_line(x1, y1, x2, y2, r, g, b, a)` -- byte 0-255
- `draw_rect(x, y, w, h, r, g, b, a)` / `draw_fill_rect(x, y, w, h, r, g, b, a)`
- `draw_circle(cx, cy, radius, r, g, b, a)` / `draw_fill_circle(cx, cy, radius, r, g, b, a)`
- `draw_screen_rect(x, y, w, h, r, g, b, a)` -- screen-space (HUD)

**Engine:**
- `screen_width()` / `screen_height()`
- `switch_scene(luaPath)` -- switch to another Lua scene
- `quit()` / `screenshot()` / `log(value)`

**Behaviors:**
- `add_behavior(entity, { on_attach=fn, update=fn, on_destroy=fn })`

**GIF:**
- `gif(path, x, y)` -- load and play animated GIF
- `gif_play(g)` / `gif_pause(g)` / `gif_stop(g)` / `gif_set_frame(g, idx)`
- `gif_set_speed(g, speed)` / `gif_set_looped(g, bool)` / `gif_finished(g)` / `gif_frame_count(g)`

**Video:**
- `video(path, x, y)` -- load video (requires video_player.dll + FFmpeg)
- `video_play(v)` / `video_pause(v)` / `video_stop(v)` / `video_seek(v, seconds)`
- `video_set_speed(v, speed)` / `video_set_looped(v, bool)` / `video_set_muted(v, bool)`
- `video_finished(v)` / `video_duration(v)`

**Grid Fluid:**
- `grid_fluid(gridW, gridH, pixelW, pixelH, x, y)` -- cellular automaton water
- `grid_fluid_config(gf, {flow_rate, spread_rate, passes, threshold, ...})`
- `grid_fluid_pour(gf, i, j, radius, amount)` -- add water in a circle
- `grid_fluid_push(gf, i, j, radius, vx, vy)` -- apply velocity (mouse interaction)
- `grid_fluid_remove(gf, i, j, radius)` -- carve through water
- `grid_fluid_set_color(gf, r, g, b, a)` / `grid_fluid_set_deep_color(...)` / `grid_fluid_set_surface_color(...)`
- `grid_fluid_visuals(gf, {wave, wave_amplitude, surface_thickness, splash_amount, ...})`
- `grid_fluid_set_splash_emitter(gf, emitter)` -- attach particle emitter for auto-splashes
- `grid_fluid_pixel_to_grid(gf, px, py)` -> {i, j} / `grid_fluid_total(gf)` / `grid_fluid_clear(gf)`

**AfterImage:**
- `after_image(sprite)` -- create an AfterImage entity tracking a sprite (draws ghost trail)
- `after_image_config(ai, {interval, duration, max_ghosts, start_alpha, layer, tint})` -- configure
- `after_image_clear(ai)` -- remove all ghosts
- `after_image_set_target(ai, sprite)` -- change tracked sprite

**Rain:**
- `rain()` -- create a Rain entity (camera-following rain particles)
- `rain_config(r, {intensity, max_drops, wind_angle, min_speed, max_speed, min_length, max_length, thickness, ground_y, margin, splashes, layer, color_front, color_tail})` -- bulk configure
- `rain_add_water(r, water_body)` -- register a WaterBody so rain disturbs its surface
- `rain_remove_water(r, water_body)` -- unregister a WaterBody
- `rain_add_obstacle(r, entity)` -- rain splashes off entity's collision bounds
- `rain_remove_obstacle(r, entity)` -- unregister an obstacle

**Asset Preloading:**
- `preload_assets()` -- scan Assets/ folder, queue all images/audio/GIFs
- `preload_all({paths})` / `preload_start()` / `preload_process(max)` / `preload_reset()`
- `preload_progress()` -> 0-1 / `preload_done()` -> bool / `preload_remaining()` -> int

**File System:**
- `list_assets(dir, pattern)` -> table of paths (e.g., `list_assets("gifs", "*.gif")`)
- `asset_exists(path)` -> bool (works with absolute paths too)

**Plugins:**
- `plugins_list()` -> table of {name, version, enabled, file}
- `plugins_enable(name, bool)` / `plugins_count()`

### Example Lua Game
```lua
-- scripts/game.lua
local player, ground

function create()
    -- Set up input
    bind("left", "A")
    bind("right", "D")
    bind("jump", "SPACE")

    -- Create ground
    ground = rect(800, 32, 0, 448, 80, 120, 80, 255)
    set_immovable(ground, true)

    -- Create player
    player = sprite("player/idle.json", 100, 200)
    play(player, "idle")

    -- Camera
    camera_follow(player, 0.1)
end

function update(dt)
    -- Movement
    if down("left") then
        set_vx(player, -120)
        set_flip_x(player, true)
        play(player, "run", false)
    elseif down("right") then
        set_vx(player, 120)
        set_flip_x(player, false)
        play(player, "run", false)
    else
        set_vx(player, 0)
        play(player, "idle", false)
    end

    -- Gravity
    set_vy(player, get_vy(player) + 600 * dt)

    -- Jump
    if pressed("jump") then
        set_vy(player, -300)
        sound("sfx/jump.wav", 0.5)
    end

    -- Collision
    separate(player, ground)
end

function draw()
    -- HUD
    draw_screen_rect(10, 10, 100, 20, 40, 40, 40, 200)
end
```

## Plugin System

Plugins are `.lua` files in `Assets/plugins/` (source in `Scripts/plugins/`). Auto-loaded on every scene start.

```lua
local plugin = {}
plugin.name = "My Plugin"
plugin.version = "1.0"

function plugin.on_load() end      -- called once
function plugin.on_update(dt) end  -- called each fixed timestep
function plugin.on_draw() end      -- called each render frame
function plugin.on_destroy() end   -- called on scene switch

return plugin
```

Lua API: `plugins_list()`, `plugins_enable(name, bool)`, `plugins_count()`

## Asset Preloading

`preload_assets()` scans the entire Assets folder and queues all loadable files:
- Images (.png, .jpg, .bmp): decoded on background thread, uploaded to GPU on main thread
- Audio (.wav, .ogg, .mp3): loaded on main thread via SDL_mixer
- GIFs (.gif): decoded on background thread via native decoder, cached for instant GifSprite use
- Videos (.mp4, etc.): verified to exist (decoded on demand, too large to preload)

```lua
preload_assets()          -- scan Assets/ recursively
preload_start()           -- begin background loading
preload_process(4)        -- upload up to 4 per frame (call in update loop)
preload_progress()        -- 0-1
preload_done()            -- true when complete
```
