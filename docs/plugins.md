# V-Engine Plugins (No Code Required)

> Write plugins without any programming language. Just JSON.

Plugins live in `Assets/plugins/` as `.json` files. Each plugin is a list of **triggers** (when something happens) paired with **actions** (do this).

## Your First Plugin

Create `Assets/plugins/my_plugin.json`:

```json
{
    "name": "My First Plugin",
    "version": "1.0",
    "description": "Press F1 to say hi",
    "triggers": [
        {
            "on": "key_pressed",
            "key": "F1",
            "do": [
                { "toast": "Hello, world!" }
            ]
        }
    ]
}
```

That's it. Launch the game, press F1, and a "Hello, world!" message pops up.

## Anatomy of a Plugin

Every plugin has:

- **name** (required) — display name shown in the plugin list
- **version** — any string like `"1.0"` or `"2.3-beta"`
- **description** — what the plugin does
- **triggers** — a list of things that happen, each with actions to run

## Triggers

A trigger says *when* to do something.

### `key_pressed` — when a key is pressed once

```json
{ "on": "key_pressed", "key": "F1", "do": [...] }
```

Keys: `F1`–`F12`, `A`–`Z`, `0`–`9`, `SPACE`, `RETURN`, `ESCAPE`, `TAB`, `LEFT`, `RIGHT`, `UP`, `DOWN`.

### `key_held` — fires every frame while a key is held

```json
{ "on": "key_held", "key": "SPACE", "do": [...] }
```

### `timer` — fires every N seconds

```json
{ "on": "timer", "every": 5.0, "do": [...] }
```

### `scene_loaded` — fires once when a scene loads

```json
{ "on": "scene_loaded", "do": [{ "log": "New scene loaded!" }] }
```

Optionally filter by scene: `"scene": "scripts/game.lua"`.

### `event` — fires when another plugin (or the game) emits a named event

```json
{ "on": "event", "event": "boss_defeated", "do": [...] }
```

## Actions

An action says *what* to do. Actions can be written as plain strings (no parameter) or as objects (with parameters).

### `toast` — show a temporary message at the bottom of the screen

```json
{ "toast": "Save complete!" }
```

### `log` — print to the developer console

```json
{ "log": "Plugin fired" }
```

### `reload_scene` — reload the current scene (no parameter)

```json
"reload_scene"
```

### `switch_scene` — load a different scene

```json
{ "switch_scene": "scripts/game.lua" }
```

### `cycle_scene` — advance through a list of scenes, one per trigger fire

```json
{
    "cycle_scene": [
        "scripts/level_1.lua",
        "scripts/level_2.lua",
        "scripts/level_3.lua"
    ]
}
```

### `play_sound` — play a sound effect

```json
{ "play_sound": "sfx/coin.wav" }
```

### `play_music` — change the background music (simple form)

```json
{ "play_music": "music/boss.ogg" }
```

With a crossfade:

```json
{ "play_music": { "path": "music/boss.ogg", "crossfade": 2000 } }
```

### `stop_music` — stop music playback

```json
"stop_music"
```

### `set_var` — remember a value for use in `if` conditions

```json
{ "set_var": { "name": "unlocked", "value": true } }
```

### `if` — run different actions based on a variable

```json
{
    "if": {
        "var": "unlocked",
        "equals": true,
        "then": [{ "toast": "Welcome back!" }],
        "else": [{ "toast": "Locked." }]
    }
}
```

### `emit` — broadcast an event other plugins can react to

```json
{ "emit": "boss_defeated" }
```

### `toggle_plugin` — enable or disable another plugin by name

```json
{ "toggle_plugin": "My First Plugin" }
```

### `quit` — exit the game

```json
"quit"
```

### `screenshot` — take a screenshot

```json
"screenshot"
```

## Full Example

A plugin that plays a sound every 10 seconds, but only after you unlock it:

```json
{
    "name": "Heartbeat",
    "version": "1.0",
    "description": "Plays a ticking sound on a timer",
    "triggers": [
        {
            "on": "key_pressed",
            "key": "H",
            "do": [
                { "set_var": { "name": "unlocked", "value": true } },
                { "toast": "Heartbeat enabled" }
            ]
        },
        {
            "on": "timer",
            "every": 10.0,
            "do": [
                {
                    "if": {
                        "var": "unlocked",
                        "equals": true,
                        "then": [{ "play_sound": "sfx/tick.wav" }]
                    }
                }
            ]
        }
    ]
}
```

## Triggers (continued)

### `every_frame` — fires every single frame

```json
{ "on": "every_frame", "do": [...] }
```

Useful for continuous behaviors like trails and overlays.

### `mouse_pressed` — fires when a mouse button is clicked

```json
{ "on": "mouse_pressed", "button": "left", "do": [...] }
```

Buttons: `left`, `right`, `middle`.

### `mouse_held` — fires every frame while a mouse button is held

```json
{ "on": "mouse_held", "button": "left", "do": [...] }
```

## Particle Actions

### `spawn_particles` — compose your own particle effects

This is the building block for all particle effects. Attach it to any trigger to spawn particles anywhere.

```json
{
    "spawn_particles": {
        "at": "mouse",
        "count": 3,
        "color_min": [100, 150, 255, 150],
        "color_max": [200, 220, 255, 220],
        "size": [2, 4],
        "life": [0.3, 0.6],
        "velocity_x": [-10, 10],
        "velocity_y": [-10, 10],
        "gravity_y": 30,
        "layer": 98
    }
}
```

**Parameters:**
- `at` — where to spawn. Options:
  - `"mouse"` — at the cursor (screen-space)
  - `"camera"` — at the camera center
  - `"world:100,200"` — fixed world coordinates
  - `[100, 200]` — same as above, shorthand
- `count` — how many particles (default 1)
- `color` — single color `[r, g, b, a]` or use `color_min`/`color_max` for randomization
- `size` — single size or `[min, max]` for random pixel size
- `life` — how long particles last in seconds, `[min, max]` or single value
- `velocity_x` / `velocity_y` — `[min, max]` velocity randomization
- `gravity_y` — downward acceleration
- `layer` — draw order (99 = on top)

### Example: mouse trail from scratch

```json
{
    "triggers": [
        {
            "on": "every_frame",
            "do": [
                { "spawn_particles": { "at": "mouse", "count": 1, "color_min": [100, 150, 255, 150], "size": [2, 4], "life": [0.3, 0.6] } }
            ]
        },
        {
            "on": "mouse_held",
            "button": "left",
            "do": [
                { "spawn_particles": { "at": "mouse", "count": 2, "color_min": [100, 150, 255, 150], "size": [2, 4], "life": [0.3, 0.6] } }
            ]
        }
    ]
}
```

### Example: explosion on event

```json
{
    "on": "event",
    "event": "boom",
    "do": [
        {
            "spawn_particles": {
                "at": "camera",
                "count": 40,
                "color_min": [255, 200, 80, 255],
                "color_max": [255, 100, 0, 255],
                "size": [3, 8],
                "life": [0.4, 0.9],
                "velocity_x": [-200, 200],
                "velocity_y": [-200, 200],
                "gravity_y": 500
            }
        }
    ]
}
```

## Bundled Actions

For common complex patterns, the engine provides bundled actions that encapsulate multi-step behavior.

### `screenshot_with_notify` — capture and show a sliding notification card

```json
{
    "triggers": [
        {
            "on": "key_pressed",
            "key": "F12",
            "do": ["screenshot_with_notify"]
        }
    ]
}
```

This triggers a screen flash, captures the frame, builds a thumbnail card, slides it in from the bottom, bobs gently for 2 seconds, and slides it out.

## When to use Lua instead

JSON plugins cover the common cases: keybindings, triggers, scene changes, audio, simple conditionals. For anything more complex (custom math, entity spawning, loops, API calls), use Lua plugins (`.lua` files in the same folder). Both run side-by-side.
