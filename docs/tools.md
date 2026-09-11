# V-Engine Tools & Asset System

> CLI commands, browser-based editors, and asset pipeline.

## CLI Commands (vengine.bat)

| Command | Description |
|---------|-------------|
| `vengine build` | Build the solution |
| `vengine clean` | Remove bin/obj build artifacts (`dotnet clean`) |
| `vengine release` | Build the Release configuration |
| `vengine test` | Run engine tests |
| `vengine editor` | Open the sprite editor in your browser |
| `vengine levels` | Open the level editor in your browser |
| `vengine flowchart` | Open the engine flowchart in your browser |

Run `vengine` with no arguments to see this list.

## Level Editor Tilemap Support

The level editor (`vengine levels`) supports tilemap painting:

### Tilemap Workflow
1. Open the level editor and click the **Tile** tool (or press 6)
2. Click **Load Tileset** and select a tileset PNG spritesheet
3. Set the tile size (default 16px, syncs with grid)
4. Click tiles in the palette to select them
5. Left-click/drag on the canvas to paint tiles
6. Right-click/drag to erase tiles
7. Toggle "Paint Solid" checkbox for collision tiles vs decorative
8. Export -- tilemap data is included in the JSON automatically

### Exported Tilemap JSON
```json
{
  "tilemap": {
    "image": "tileset.png",
    "tileSize": 16,
    "width": 150,
    "height": 30,
    "tiles": [0, 1, -1, ...],
    "solid": [1, 1, 0, ...]
  }
}
```

### Per-Tile Collision Editing
- Select a tile in the palette to see its collision rect
- Visual editor with zoomed tile preview and green rect overlay
- X/Y/W/H number inputs for precise values
- Presets: Full, None, Half Top, Half Bottom

## Sprite Editor (Tools/sprite-editor.html)

Browser-based tool for defining sprite animations. Open with `vengine editor`.

### Workflow
1. Drag & drop a spritesheet PNG (or click Load Image)
2. Adjust frame width/height -- grid overlay shows frame boundaries
3. Create named animations, click frames to add them
4. Preview animations in real-time
5. Export a `.json` metadata file
6. In code: `sprite.LoadFromMeta("player.json")`

### Exported JSON Format
```json
{
  "image": "idle.png",
  "frameWidth": 46,
  "frameHeight": 55,
  "animations": {
    "idle": { "frames": [0,1,2,3,4,5,6,7,8,9], "fps": 10, "looped": true },
    "walk": { "frames": [0,1,2,3], "fps": 12, "looped": true, "pingPong": true },
    "attack": {
      "frames": [0,1,2], "fps": 8, "looped": false,
      "hitbox": { "x": 5, "y": 3, "w": 36, "h": 50 },
      "origin": { "x": 23, "y": 55 },
      "damageBox": { "x": 30, "y": 10, "w": 20, "h": 30 }
    }
  }
}
```

### Editor Features
- Multi-sheet support with tabs and batch export
- Auto-detect grid size via pixel alpha analysis
- Hitbox editor with visual handles and copy/paste
- Origin point editor with copy/paste/apply-all
- Ping-pong, preview background, onion skinning
- Undo/Redo (Ctrl+Z / Ctrl+Y, 50 levels)
- Auto-save to localStorage
- Keyboard shortcuts: Space (play/pause), arrows (step), +/- (zoom), ? (help)
- Touch/mobile support (touch events mapped to mouse events)

Both sprite-editor.html and level-editor.html support touch input for use on tablets and mobile devices.

## Asset System

Place assets in the application output under `Assets/`.
Configure the application project to copy its assets during builds.
Provide `Assets/ui/default-font.ttf` for default text and UI widgets.
In code, reference by filename: `LoadGraphic("idle.png")`.
Subfolders work: `LoadGraphic("sprites/hero.png")`.
The engine resolves paths relative to the exe via `Eng.Asset()`.

Distribute the application executable, runtime libraries, and `Assets/` folder together.
Publish the application project with `dotnet publish`.
