# V-Engine Architecture

> Internal architecture reference for engine developers.

## Architecture

### Rendering Pipeline (OpenGL 3.3)
- Rendering uses **OpenGL 3.3 Core Profile** via Silk.NET, with SDL2 providing the window and GL context
- All drawing goes through `GLRenderer` (`Eng.GL`), which manages two internal batchers:
  - **SpriteBatch** - batches textured quads (up to 8192 per draw call). Multi-texture batching: up to 8 textures bound simultaneously per draw call. Only flushes when all 8 slots are full, blend mode changes, or buffer is full
  - **PrimitiveBatch** - batches lines, rectangles, circles, filled quads (arbitrary 4-point polygons for thick lines)
- Built-in GLSL 330 shaders: sprite (textured quad), primitive (colored), post-process (fullscreen)
- **BlendMode** enum: `Alpha` (default), `Additive`, `Multiply`, `None`
- Logical resolution via orthographic projection matrix -- game renders at design resolution, auto-scales with letterboxing on resize
- **Texture cache** -- textures loaded once, shared across all entities. Managed by GLRenderer
- `MakeGraphic()` uses a shared **1x1 white pixel texture** (no render target) -- color comes from vertex data

### Game Loop (Game.cs)
- Fixed timestep (default 60fps, configurable via constructor or `Eng.TargetFps`)
- Accumulator-based: physics runs at consistent rate regardless of render speed
- Frame cap: frameTime capped at 0.25s to prevent spiral of death after long pause
- Flow: `ApplyPendingScene -> PollEvents -> HandleEngineActions -> Effects.Update(realDt) -> [FixedTimestep: Time, Timers, Tweens, OnPreUpdate, Scene.Update, Camera.Update, OnPostUpdate, Transition, VolumeOverlay] -> Render`
- Render order: `BeginFrame -> PostProcess.Begin -> OnPreDraw -> Scene.Draw -> FlushAll -> Effects -> Transition -> Debug -> Volume -> OnPostDraw -> FlushAll -> PostProcess.End -> EndFrame`
- Scene switching is deferred to start of next frame (no mid-frame issues)
- Auto-loads `v-engine-icon.png` from Assets as window icon (optional, won't crash if missing)
- `Start()` wrapped in try-finally so SDL always shuts down cleanly on crash

### Global Access (Eng.cs)
- `Eng.Game` - the Game instance
- `Eng.GL` - GLRenderer (replaces old Eng.Renderer)
- `Eng.Input` - keyboard input
- `Eng.Mouse` - mouse input
- `Eng.Gamepad` - gamepad input
- `Eng.Time` - elapsed, total, frame count
- `Eng.Camera` - camera system
- `Eng.Audio` - audio manager
- `Eng.Timers` - timer manager
- `Eng.Tweens` - tween manager
- `Eng.Effects` - screen flash / freeze
- `Eng.Debug` - debug overlay
- `Eng.PostProcess` - post-processing pipeline
- `Eng.Volume` - volume HUD overlay
- `Eng.Actions` - InputMap for named action/axis bindings
- `Eng.Loader` - AssetLoader for async asset preloading
- `Eng.TargetFps` - get/set fixed timestep rate (default 60)
- `Eng.Width` / `Eng.Height` - logical resolution
- `Eng.AssetsPath` - root path to Assets folder next to exe
- `Eng.Asset("filename")` - resolves path in Assets/ folder, throws if missing
- `Eng.TryAsset("filename", out path)` - resolves path without throwing (returns false if missing)
- `Eng.PixelPerfect` - nearest-neighbor scaling (true, default) or linear filtering (false)
- `Eng.ShowCursor` - show/hide the OS mouse cursor (bool)
- `Eng.VSync` - enable/disable vertical sync at runtime (bool, default true)
- `Eng.Fullscreen` - get/set borderless fullscreen mode
- `Eng.ToggleFullscreen()` - toggle fullscreen (also bound to F11 via InputMap)
- `Eng.SwitchScene(scene, transition?)` - switch scenes with optional transition
- `Eng.PushScene(scene)` - push scene onto stack. Parent scene's Update doesn't run, but its physics bodies / timers / tweens / textures are preserved so pop can resume. Texture loads inside the pushed scene go into their own scope layered above the parent's.
- `Eng.PopScene()` - destroy and unload the pushed scene (its texture scope included); resume the parent with its state intact.
- `Eng.CaptureScreen(path?)` - save screenshot PNG at end of frame (F12 default binding)
- `Eng.Quit()` - exit the game
- `Eng.InitHeadless()` - initialize without renderer for unit testing / CI

### EngineContext (Testability)
- `Eng.Context` holds all subsystem instances (EngineContext class)
- Swap `Eng.Context` in tests to inject mocks -- all `Eng.*` accessors delegate to it
- `InternalsVisibleTo("VEngine.Tests")` for test access to internals

### Lifecycle Hooks (Signals)
- `Eng.OnPreUpdate` - fires each fixed timestep, before scene update. Signal<float> (receives scaledDt)
- `Eng.OnPostUpdate` - fires each fixed timestep, after scene and camera update. Signal<float>
- `Eng.OnPreDraw` - fires each frame, before scene draws. Signal (no args)
- `Eng.OnPostDraw` - fires each frame, after all overlays but before post-process and present. Signal

### Entity (Entity.cs)
- Base class for everything in the game world
- Fields: Position (Vec2), ScaleX, ScaleY, Angle (float)
- **No Velocity/Acceleration** -- use KinematicEntity for moving objects
- `BaseWidth` / `BaseHeight` - unscaled dimensions
- `Width` and `Height` are computed: `BaseWidth * ScaleX`, `BaseHeight * ScaleY`
- `Active` is a **property** with hooks: setting triggers `OnActivated()` / `OnDeactivated()` (virtual)
- `Visible` - whether this entity draws
- `Destroyed` (bool, read-only) - prevents double-destroy
- `Destroy()` calls `OnDestroy()` once, sets `Destroyed = true`
- `OnDestroy()` - virtual, override to clean up resources
- `Layer` (int) - draw layer; lower draws first. 0=background, 1=world, 2=player, 3=foreground, 4=UI
- `ZOrder` (float) - fine draw order within a layer; lower draws first
- `ScrollFactor` (Vec2) - camera parallax: (1,1) = normal, (0,0) = fixed to screen (HUD/UI)
- `Immovable` - if true, collision separation won't push this entity
- `Overlaps(other)` - AABB collision check using `GetCollisionBounds()`
- `GetCollisionBounds()` - virtual; returns (X, Y, W, H). Sprite overrides for per-animation hitbox
- `Update(float dt)` - virtual, empty by default (no physics on base Entity). Auto-updates attached behaviors
- `Draw()` - virtual, empty by default. Takes **no parameters** (uses Eng.GL directly)
- **Behavior Composition:**
  - `entity.AddBehavior(new MyBehavior())` - attach a behavior (calls OnAttach). Returns the behavior
  - `entity.GetBehavior<MyBehavior>()` - get first attached behavior of a type, or null
  - `entity.RemoveBehavior(behavior)` - detach a behavior
  - Behaviors are updated automatically in Entity.Update(). OnDestroy called when entity is destroyed
  - Subclass `Behavior` and override `OnAttach()`, `Update(dt)`, `OnDestroy()`

### KinematicEntity (KinematicEntity.cs, extends Entity)
- Adds `Velocity` (Vec2), `Acceleration` (Vec2), `AngularVelocity` (float)
- `Update(dt)` applies kinematics: vel += accel * dt, pos += vel * dt, angle += angVel * dt
- Use for players, enemies, projectiles -- anything that moves
- Use Entity directly for static objects (tiles, UI, decorations)

### Sprite (Sprite.cs, extends KinematicEntity)
- `LoadFromMeta(jsonPath)` - load spritesheet + all animations from editor-exported JSON
- `LoadGraphic(path)` - single image
- `LoadGraphic(path, frameW, frameH)` - spritesheet with uniform grid
- `MakeGraphic(w, h, r, g, b, a)` - colored rectangle (shared 1x1 white texture, no render target)
- `AddAnimation(name, frames, fps, looped, pingPong, hitbox, origin, damageBox)` - register named animation (chainable)
- `Play(name, restart)` - play animation by name
- `SetFrame(frameIndex, pause)` - jump to a specific frame
- `AnimationFinished` - true when non-looped animation completes
- `CurrentAnimationName` - name of currently playing animation
- `CurrentFrameIndex` / `CurrentFrameCount` - frame position in animation
- `AnimationSpeed` (float) - speed multiplier. 1 = normal, 0.5 = half, 2 = double
- `OnFrame(animName, frameIndex, callback)` - register callback for specific animation frame (footstep sounds, hit activation, etc.)
- Per-animation hitbox: overrides `GetCollisionBounds()` when defined
- Per-animation origin: offsets rendering so Position aligns to origin point (e.g. feet)
- Per-animation damageBox: `GetDamageBox()` returns world-space damage rect, or null if not defined
- `FlipX` / `FlipY` - mirror rendering. Hitbox, origin, and damageBox flip automatically
- `Color` (Math.Color) - tint/alpha modulation
- Ping-pong animations play forward then reverse automatically
- Frustum culling: sprites off-screen are skipped during Draw (expanded by outline thickness)
- Rotation pivot: when origin is defined, rotation uses origin as pivot point
- Textures managed by GLRenderer texture cache (not owned by Sprite)
- **Sprite Effects:**
  - `sprite.Flash(duration, color?)` - flash as solid color for duration (default 0.1s white). Hit feedback
  - `sprite.IsFlashing` - true while a Flash() is active
  - `sprite.Silhouette` (bool) - persistent solid-color mode (texture alpha shape only)
  - `sprite.SilhouetteColor` (Color, White) - color for silhouette rendering
  - `sprite.OutlineThickness` (float, 0) - pixel outline. 0 = off. Draws 8 offset copies
  - `sprite.OutlineColor` (Color, White) - outline color
  - Flash overrides silhouette while active. Both use `uEffect` shader uniform (solid color + texture alpha)
  - Outline + flash/silhouette compose: outline draws first, then main sprite with flash/silhouette
  - Effect sprites flush the batch (isolated draw calls), normal sprites stay fully batched

### Scene (Scene.cs)
- Abstract base class for game screens/levels
- Uses an internal `Group` (_root) for entity management
- Override `Create()`, `Update(dt)`, `Draw()`
- `Draw()` takes **no parameters** -- rendering goes through `Eng.GL`
- `Add(entity)` / `Remove(entity, destroy: true)`
- Remove calls Destroy() by default. Pass `destroy: false` to keep entity alive
- `Update(dt)` iterates **backward** so removals during update don't skip entities
- `Entities` - read-only list for debug iteration
- `SortDrawOrder()` - mark draw order as needing re-sort (auto-called on add/remove)
- `Destroy()` destroys all entities via the root Group

### Scene Stack
- `Eng.PushScene(scene)` - push a new scene on top. The parent scene's `Update` stops running but its state (physics bodies, registered timers, active tweens, loaded textures) is preserved. The pushed scene gets its own texture scope layered above the parent's, so textures loaded inside it are freed on pop without touching the parent's.
- `Eng.PopScene()` - destroy the top scene (its `Destroy` runs, its texture scope unloads) and resume the scene underneath with its subsystem state intact.
- Because `Eng.Timers` / `Eng.Tweens` / `Eng.Physics` are global, **pushed scenes must clean up any timers / tweens / bodies they register inside their own `Destroy()`** — otherwise those artifacts leak into the resumed parent. `SwitchScene` doesn't have this requirement (it calls `CancelAll` + `Clear` itself).
- Note that `Eng.Timers`, `Eng.Tweens`, and `Eng.Physics` continue ticking while a scene is pushed, so timers registered by the parent will keep firing. If you need them genuinely frozen, pause them in the pushed scene's `Create` and resume in its `Destroy`.
- Use for pause menus, inventory screens, dialog overlays.
- `Eng.Game.SceneStackDepth` - number of scenes on the stack (not counting current).

### Group (Group.cs, extends Entity)
- Container for organizing entities hierarchically
- Same Add/Remove API as Scene (Remove destroys by default)
- **O(1) removal** via swap-with-last (draw order re-sorted on next Draw)
- `Update(dt)` iterates **backward** for safe removal during iteration
- `Draw()` sorts by Layer then ZOrder when dirty, then draws forward
- **Cached sort delegate** -- avoids lambda allocation on every sort
- `SortDrawOrder()` marks sort as needed (called automatically on add/remove)
- `Members` - read-only list of children
- `OnDestroy()` destroys all children

### Camera (Camera.cs)
- `Eng.Camera.Follow(entity, lerp)` - smoothly follow with **exponential decay** (framerate-independent)
- `Eng.Camera.Unfollow()` - stop following
- `Eng.Camera.FocusOn(x, y)` - snap camera to center on position instantly
- `Eng.Camera.SetBounds(minX, minY, maxX, maxY)` - clamp camera to world bounds
- `Eng.Camera.ClearBounds()` - remove bounds
- `Eng.Camera.Shake(intensity, duration)` - screen shake with fade-out. **Stacks**: new shake keeps the higher intensity and longer remaining duration
- `Eng.Camera.Zoom` - zoom level (1 = normal, 2 = 2x zoom in, minimum 0.01)
- `Eng.Camera.Position` - camera position in world space (top-left of viewport)
- `Eng.Camera.FollowOffset` - offset from follow target (defaults to center-on-screen)
- `Eng.Camera.FollowLerp` - follow smoothness (0.1 default, 1 = instant snap)
- `Eng.Camera.Transform(worldX, worldY, scrollFactor)` - apply scroll factor + camera in one step
- `Eng.Camera.WorldToScreen(x, y)` - convert world coords to screen (includes shake)
- `Eng.Camera.ScreenToWorld(x, y)` - convert screen coords to world (for mouse picking)
- Follow lerp uses exponential decay: `1 - pow(1 - lerp, dt * 60)` for consistent feel at any framerate
- When viewport is larger than bounds, camera centers instead of oscillating
- Unfollows automatically if target becomes inactive

### Text (Text.cs, extends Entity)
- `new Text("Hello", x, y)` - create a text entity
- `text.SetFont("ui/myfont.ttf", 24)` - load a TTF font at a given size
- `text.SetDefaultFont(size)` - load the application font from `Assets/ui/default-font.ttf`.
- `text.Content = "Score: 100"` - change displayed text (re-renders automatically)
- `text.Color` - text color and alpha (Math.Color)
- `text.FontSize` - change size (reloads font)
- `text.Measure()` - returns (W, H) pixel dimensions without rendering
- `text.UseAtlas = true` - use GlyphAtlas for zero-allocation rendering (better for dynamic text like scores)
- Two rendering modes:
  - **Texture mode** (default): renders entire string to a texture. Better for static text. Supports full Unicode
  - **Atlas mode**: pre-rendered ASCII glyph atlas, one quad per character. Zero allocation. Better for frequently changing text (scores, timers)
- Fonts cached by path+size via FontCache, shared across all Text and UI instances
- Uses `ScrollFactor`, `Layer`, `ZOrder`, `ScaleX/Y` like any Entity
- Set `ScrollFactor = (0,0)` for HUD/UI text that stays on screen

### GlyphAtlas (GlyphAtlas.cs)
- Dynamic glyph atlas with **full Unicode support** -- renders glyphs on demand and packs into a growing texture
- Pre-renders ASCII 32-126 on creation for immediate use; other characters rendered on first use
- Cached per font path + size (automatic, no manual management)
- `DrawText(text, x, y, r, g, b, a, scaleX, scaleY)` - submits one quad per character to SpriteBatch. Returns width in pixels
- `MeasureWidth(text)` - measure text width without drawing
- `LineHeight` - vertical line spacing for the font
- Atlas doubles in height when full (starts at 512x512)
- Used by Text entities when `UseAtlas = true`, and by all new UI widgets

### Audio (AudioManager.cs)
- `Eng.Audio.PlaySound(path, volume, loops)` - play a sound effect (auto-loads and caches). Returns channel number
- `Eng.Audio.LoadSound(path)` - preload a sound without playing
- `Eng.Audio.PlaySoundAt(path, worldX, worldY, volume, maxDistance, loops)` - play with spatial position (volume + stereo pan from camera). Returns channel
- `Eng.Audio.SetSoundPosition(channel, worldX, worldY, maxDistance)` - update position of a playing channel (for looping spatial sounds)
- `Eng.Audio.ClearSoundPosition(channel)` - remove spatial positioning (restore center pan)
- `Eng.Audio.StopChannel(channel)` / `Eng.Audio.StopAllSounds()`
- `Eng.Audio.PlayMusic(path, loop, fadeInMs)` - play background music (one track at a time)
- `Eng.Audio.StopMusic(fadeOutMs)` - stop music with optional fade
- `Eng.Audio.PauseMusic()` / `Eng.Audio.ResumeMusic()`
- `Eng.Audio.IsMusicPlaying` / `Eng.Audio.IsMusicPaused`
- `Eng.Audio.MasterVolume` - overall volume (0.0 - 1.0)
- `Eng.Audio.SoundVolume` - sound effects volume (0.0 - 1.0)
- `Eng.Audio.MusicVolume` - music volume (0.0 - 1.0)
- Supports WAV, OGG, MP3, FLAC (depending on SDL_mixer build)
- 32 simultaneous sound effect channels
- Per-frame cleanup of finished channels (prevents unbounded growth of internal volume tracking)

### Input (KeyboardInput.cs)
- `Eng.Input.IsDown(key)` - held this frame
- `Eng.Input.IsPressed(key)` - first frame pressed
- `Eng.Input.IsReleased(key)` - frame released
- Accepts both SDL_Scancode and SDL_Keycode

### InputMap (InputMap.cs)
- Maps named actions to physical inputs. Decouples game logic from hardware
- Access via `Eng.Actions`
- **Digital Actions:**
  - `Eng.Actions.Bind("jump", SDL_Scancode.SDL_SCANCODE_SPACE)` - bind keyboard key(s)
  - `Eng.Actions.Bind("jump", SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A)` - bind gamepad button(s)
  - `Eng.Actions.Bind("jump", key, button)` - bind keyboard + gamepad in one call
  - `Eng.Actions.Down("jump")` - true if any bound input is held
  - `Eng.Actions.Pressed("jump")` - true on first frame pressed
  - `Eng.Actions.Released("jump")` - true when ALL bound inputs are released
  - `Eng.Actions.AnyReleased("jump")` - true when ANY bound input is released
  - `Eng.Actions.Unbind("jump")` - remove all bindings for an action
- **Analog Axes:**
  - `Eng.Actions.BindAxis("moveX", SDL_SCANCODE_D, SDL_SCANCODE_A)` - positive/negative keys
  - `Eng.Actions.BindAxis("moveX", GamepadAxis.LeftStickX)` - gamepad analog
  - `Eng.Actions.Axis("moveX")` - returns -1 to 1 (keyboard = digital, gamepad = analog, largest magnitude wins)
  - `Eng.Actions.UnbindAxis("moveX")` - remove axis bindings
- **Introspection** (for rebinding UIs):
  - `GetKeys(action)`, `GetButtons(action)`, `GetGamepadAxes(axis)`
  - `HasAction(action)`, `HasAxis(axis)`
  - `ActionNames`, `AxisNames` - enumerate all registered names
- `ClearAll()` - remove all action and axis bindings
- `GamepadAxis` enum: LeftStickX/Y, RightStickX/Y, LeftTrigger, RightTrigger
- Engine binds its own actions with "engine:" prefix (debug, fullscreen, volume_up, volume_down)
- Bindings are additive -- call Bind multiple times to add alternative inputs

### Mouse (MouseInput.cs)
- `Eng.Mouse.X` / `Eng.Mouse.Y` - screen coordinates (pixels)
- `Eng.Mouse.Position` - screen position as Vec2
- `Eng.Mouse.WorldPosition` - world position via camera (accounts for zoom, scroll, shake)
- `Eng.Mouse.IsDown(MouseButton)` / `IsPressed(MouseButton)` / `IsReleased(MouseButton)`
- `Eng.Mouse.ScrollX` / `ScrollY` - scroll wheel delta this frame (positive Y = up)
- `MouseButton.Left` (=1), `MouseButton.Middle` (=2), `MouseButton.Right` (=3) — SDL button IDs, NOT zero-indexed
- Mouse coordinates are transformed through viewport letterboxing automatically
- **Buffered input**: `IsPressed`/`IsReleased` events are buffered across render frames until a fixed timestep update consumes them via `ConsumeBuffered()`. This prevents missed clicks when render FPS > fixed timestep rate (e.g., 144fps render, 60fps fixed update)
- Lua mapping: `mouse_pressed(0)` → `MouseButton.Left` (binding adds +1 to convert from 0-indexed Lua to 1-indexed SDL)

### Gamepad (GamepadInput.cs)
- `Eng.Gamepad.Connected` - whether a gamepad is plugged in
- `Eng.Gamepad.IsDown(button)` / `IsPressed(button)` / `IsReleased(button)`
- `Eng.Gamepad.AnyPressed` - true if any button was pressed this frame
- `Eng.Gamepad.LeftStick` / `RightStick` - Vec2 with deadzone applied (-1 to 1, Y positive = down)
- `Eng.Gamepad.LeftTrigger` / `RightTrigger` - float 0 to 1
- `Eng.Gamepad.DeadZone` - configurable deadzone threshold (default 0.15)
- Auto-connects to the first available controller on startup
- Hot-plug support: connect/disconnect controllers at any time
- Uses SDL2 GameController API (Xbox, PlayStation, Switch Pro, etc.)

### Signal (Signal.cs)
- Lightweight publish-subscribe event system
- Three variants: `Signal` (no args), `Signal<T>` (one arg), `Signal<T1, T2>` (two args)
- `signal.Subscribe(callback)` - returns an `Action` that unsubscribes when called
- `signal.Emit()` / `signal.Emit(value)` / `signal.Emit(v1, v2)` - fire the signal
- `signal.Unsubscribe(callback)` - explicit unsubscribe
- `signal.Clear()` - remove all listeners
- `signal.Count` - number of listeners
- Safe to add, remove, or clear listeners during Emit() (snapshot-based iteration)
- **Zero allocation after warmup** -- cached snapshot array, only rebuilt when listener list changes
- Used by engine lifecycle hooks (OnPreUpdate, OnPostUpdate, OnPreDraw, OnPostDraw)

### Effects (Effects.cs)
- `Eng.Effects.Flash(r, g, b, duration, alpha)` - flash screen with a color that fades out
- `Eng.Effects.Freeze(duration)` - freeze game for duration (sets TimeScale to 0, restores when done)
- `Eng.TimeScale` - global time scale (shortcut for `Eng.Effects.TimeScale`)
  - 1 = normal, 0.5 = half speed, 0 = frozen
  - Affects scene, timers, tweens, camera. Does NOT affect input, transitions, or flash
- Flash and freeze run on real time (not affected by TimeScale)
- Draw order: Scene -> Flash -> Transition -> Debug -> Volume HUD -> PostProcess

### Scene Transitions (Transition.cs)
- `Eng.SwitchScene(new MyScene(), Transition.Fade(0.8f))` - fade to black and back
- `Eng.SwitchScene(new MyScene(), Transition.Fade(0.6f, 255, 255, 255))` - fade to white
- `Eng.SwitchScene(new MyScene(), Transition.Wipe(0.6f, WipeDir.Left))` - wipe from left edge
- `Eng.SwitchScene(new MyScene(), Transition.CircleWipe(0.8f))` - Zelda-style circle wipe (screen center)
- `Eng.SwitchScene(new MyScene(), Transition.CircleWipe(0.8f, px, py))` - circle wipe from custom screen point
- `Eng.SwitchScene(new MyScene(), Transition.DiamondWipe(0.6f))` - diamond shape closes/opens
- `Eng.SwitchScene(new MyScene(), Transition.Pixelate(1f, 8))` - random block dissolve (8px blocks)
- `Eng.SwitchScene(new MyScene())` - no transition (immediate)
- Old scene updates/renders during out phase (0-0.5), new scene during in phase (0.5-1)
- Scene swap (Destroy old, Create new) happens at the midpoint
- Calling SwitchScene during a transition is ignored if a new transition is given
- Subclass `Transition` and override `Draw(float progress)` for custom effects
- WipeDir: Left, Right, Up, Down
- Circle/Diamond wipe use annular geometry (ring of quads) to mask everything outside the shape
- Pixelate uses Fisher-Yates shuffled grid cells with deterministic seed

### Tween (Tween.cs)
- `Eng.Tweens.To(() => sprite.ScaleX, v => sprite.ScaleX = v, 2f, 0.5f, Ease.OutBounce)` - tween a float
- `Eng.Tweens.To(() => sprite.Position, v => sprite.Position = v, target, 1f, Ease.InOutQuad)` - tween a Vec2
- `Eng.Tweens.To(() => sprite.Color, v => sprite.Color = v, Color.Red, 0.5f, Ease.Linear)` - tween a Color
- `Eng.Tweens.To(getter, setter, target, duration, myCustomFunc)` - tween with custom easing function `Func<float, float>`
- `handle.Delay(0.5f)` - add delay before tween starts (chainable)
- `handle.OnComplete(() => { ... })` - chain actions when tween finishes (not called on Cancel)
- `handle.Cancel()` / `handle.Active` / `handle.Progress` (0-1) / `handle.Elapsed` / `handle.Duration`
- `Eng.Tweens.CancelAll()` - cancel all active tweens
- `Eng.Tweens.Count` - number of active tweens
- Start value captured from getter at creation time
- 31 easing curves: Linear, In/Out/InOut for Quad, Cubic, Quart, Quint, Sine, Expo, Circ, Back, Bounce, Elastic
- `Easing.Apply(Ease, float)` - use easing curves standalone
- Tweens snap to exact target value on completion (no drift)
- **Auto-cancelled on scene switch** -- `Eng.Tweens.CancelAll()` is called in `Game.SwapScene()`
- Compose with delay: `Eng.Tweens.To(...).Delay(0.5f)` (no timer needed for simple delays)

### Timer (Timer.cs)
- `Eng.Timers.After(delay, callback)` - call once after delay (seconds). Returns `TimerHandle`
- `Eng.Timers.Every(interval, callback)` - call repeatedly at interval. Returns `TimerHandle`
- `Eng.Timers.CancelAll()` - cancel all active timers
- `Eng.Timers.Count` - number of active timers
- `handle.Cancel()` - stop a specific timer permanently
- `handle.Pause()` - pause the timer (elapsed time stops accumulating)
- `handle.Resume()` - resume a paused timer
- `handle.Active` - whether the timer is still running
- `handle.Paused` - whether the timer is paused
- `handle.Remaining` - seconds until next fire
- `handle.Elapsed` - seconds since last fire
- Timers update before scene update each fixed timestep
- Repeating timers catch up if frames are slow (fires multiple times per update if needed)
- Safe to add/cancel timers from within callbacks
- **Auto-cancelled on scene switch** -- `Eng.Timers.CancelAll()` is called in `Game.SwapScene()`

### Pool (Pool.cs)
- **Composition pattern** -- pool has a Group (`pool.Group`), does not extend Group
- `new Pool<Bullet>(() => new Bullet(), preload: 20)` - create a pool with factory and optional preload
- `pool.AddTo(scene)` - convenience for `scene.Add(pool.Group)`. Chainable
- `pool.Get()` - get an inactive entity or create a new one. Sets Active/Visible to true
- `pool.Get(x, y)` - get and reset position, velocity, angle to defaults
- `pool.Release(entity)` - deactivate and return to pool. Safe to call twice (no-op if already inactive)
- `pool.ReleaseAll()` - release all active entities
- `pool.ActiveCount` / `pool.TotalCount`
- `pool.Members` - read-only list of all entities (active + inactive)
- Inactive entities stay in the scene but skip Update/Draw/Collision (Active=false, Visible=false)
- O(1) Get via internal stack, **O(1) Release via HashSet** membership check (validates entity belongs to pool)
- No allocations during gameplay once pool is warm

### Collision (Collision.cs)
- `Collision.Check(a, b)` - non-modifying overlap check. Returns (CollisionDir, PushX, PushY) for inspection before committing
- `Collision.Separate(moving, solid)` - AABB separation, pushes moving out on shortest axis. Returns `CollisionDir` flags
  - Overloaded for both `Entity` and `KinematicEntity` -- zeros velocity on collision axis for kinematic entities
- `Collision.SeparateOneway(moving, platform)` - one-way platform: only blocks from top, can jump up through
  - Overloaded: KinematicEntity version checks downward velocity
- `Collision.SeparateGroup(moving, group, oneWay)` - collide entity against all members of a Group
- `Collision.SeparateList(moving, list, oneWay)` - collide entity against a list of entities
- `Collision.OverlapGroup(groupA, groupB, callback)` - trigger callback for each overlapping pair
- `Collision.OverlapGroup(entity, group, callback)` - trigger callback for entity vs group overlaps
- **Spatial Hash Accelerated:**
  - `Collision.OverlapHash(entity, hash, callback)` - near O(1) per entity overlap check
  - `Collision.OverlapHash(group, hash, callback)` - group vs spatial hash
- **Circle Collision:**
  - `Collision.OverlapCircles(x1, y1, r1, x2, y2, r2)` - circle-circle overlap
  - `Collision.OverlapCircleRect(cx, cy, radius, rx, ry, rw, rh)` - circle-AABB overlap
  - `Collision.SeparateCircles(a, radiusA, b, radiusB)` - push circle A out of circle B, zero velocity along normal
- **Sweep (Continuous Collision Detection):**
  - `Collision.Sweep(moving, solid, velX, velY, out hitDir)` - sweep AABB along velocity, returns time of impact (0-1)
  - Use for fast-moving projectiles to prevent tunneling through thin walls
  - Returns 1 if no collision within the sweep distance
- `Collision.Skin` - tiny overlap kept after separation (default 0.1) to prevent jitter
- `Collision.OnewayMinPen` / `OnewayMargin` - configurable one-way platform thresholds
- `CollisionDir` flags: `None`, `Top` (landed), `Bottom` (bonked head), `Left`, `Right`

### SpatialHash (SpatialHash.cs)
- Grid-based broad-phase acceleration for overlap queries
- Reduces O(n*m) to near-O(n) for sparse entity distributions
- `new SpatialHash(cellSize)` - create with cell size in pixels (default 64)
- `hash.Clear()` - clear all cells (call once per frame before re-inserting)
- `hash.Insert(entity)` - insert based on collision bounds
- `hash.InsertGroup(group)` - insert all active members of a Group
- `hash.Query(x, y, w, h, results)` - query all entities in an area
- `hash.QueryEntity(entity, results)` - query near an entity
- Entities spanning multiple cells are inserted into all of them
- Internal list pool avoids allocation during Clear/Insert cycles
- Supports negative world coordinates

### PostProcess (PostProcess.cs)
- Composable multi-pass pipeline using ping-pong framebuffers
- `Eng.PostProcess.Enabled = true` - master enable (false = no-op, scene renders directly)
- **Adding passes:**
  - `Eng.PostProcess.AddPass(fragmentSource)` - add pass from GLSL fragment shader string (uses built-in vertex shader)
  - `Eng.PostProcess.AddPass(shaderProgram)` - add pass with pre-compiled shader
  - Returns `PostProcessPass` with `.Enabled` (bool) and `.SetUniforms` callback
- `pass.SetUniforms = (shader, time) => { shader.SetFloat("uFoo", val); }` - set uniforms each frame
- `Eng.PostProcess.RemovePass(pass)` / `Eng.PostProcess.ClearPasses()`
- **Built-in CRT effect:**
  - `Eng.PostProcess.EnableCRT(chromaIntensity, scanlineAlpha, vignetteStrength)` - chromatic aberration + scanlines + vignette
  - `Eng.PostProcess.DisableCRT()`
- **Built-in Glitch effect:**
  - `Eng.PostProcess.EnableGlitch(intensity)` - video glitch (simplex noise displacement, channel shift, interference lines, scanline darkening)
  - `Eng.PostProcess.DisableGlitch()`
- **Built-in ColdbergTV effect:**
  - `Eng.PostProcess.EnableColdbergTV(intensity)` - CRT curvature, scan line shift, frame roll, RGB channel shift, color drift, signal noise, power line noise, gamma tint, scanlines, vignette
  - `Eng.PostProcess.DisableColdbergTV()`
- All built-in shaders use `uIntensity` uniform (0 = clean, 1 = full effect). Composable with each other
- When no passes are enabled, scene renders directly to screen (zero overhead)
- Pipeline: scene renders to ping FBO, passes chain via ping-pong, final pass renders to screen
- FBOs auto-resize on window resize

### GLRenderer Drawing Facade (GLRenderer.cs)
- **Screen-space methods:**
  - `Eng.GL.DrawTexture(tex, srcX, srcY, srcW, srcH, dstX, dstY, dstW, dstH, r, g, b, a, angle, flipX, flipY, blend)`
  - `Eng.GL.FillRect(x, y, w, h, r, g, b, a)`
  - `Eng.GL.DrawRect(x, y, w, h, r, g, b, a)`
  - `Eng.GL.DrawLine(x1, y1, x2, y2, r, g, b, a)`
  - `Eng.GL.DrawCircle(cx, cy, radius, r, g, b, a)`
  - `Eng.GL.FillCircle(cx, cy, radius, r, g, b, a)`
- **World-space methods** (handle scroll factor, camera, zoom automatically):
  - `Eng.GL.DrawTextureWorld(tex, src..., worldX, worldY, w, h, scrollFactor, r, g, b, a, angle, flipX, flipY, blend, pivotX, pivotY)`
  - `Eng.GL.FillRectWorld(worldX, worldY, w, h, scrollFactor, r, g, b, a)`
  - `Eng.GL.DrawRectWorld(worldX, worldY, w, h, scrollFactor, r, g, b, a)`
- `Eng.GL.FlushAll()` / `Eng.GL.Flush()` - flush pending draws to GPU
- `Eng.GL.GetOrCreateTexture(path)` - load/cache a texture
- `Eng.GL.HasTexture(path)` - check if texture is cached
- `Eng.GL.GetOrCreateTextureFromData(path, w, h, data)` - upload pre-decoded RGBA data (used by AssetLoader)
- `GLTexture.FromSurface` handles SDL surface row padding via `GL_UNPACK_ROW_LENGTH` (fixes text rendering with padded surfaces)
- `Eng.GL.UnloadTexture(path)` / `Eng.GL.UnloadAllTextures()`
- `Eng.GL.TextureCacheCount` - number of cached textures
- `Eng.GL.WhitePixelTexture` - shared 1x1 white texture (used by MakeGraphic)
- `Eng.GL.ClearR/G/B` - background clear color (default dark blue-gray)
- Color parameters use float 0-1 (not byte 0-255)

### Draw (Draw.cs) - Static Helpers
- **World-space** (camera-transformed):
  - `Draw.Line(x1, y1, x2, y2, r, g, b, a)` - 1px line between world points
  - `Draw.ThickLine(x1, y1, x2, y2, thickness, r, g, b, a)` - thick line (rendered as rotated quad)
  - `Draw.Polyline(points, thickness, r, g, b, a, closed)` - connected polyline with miter joins. Use for trails, paths, debug visualization
  - `Draw.Rect(x, y, w, h, r, g, b, a)` - rectangle outline in world space
  - `Draw.FillRect(x, y, w, h, r, g, b, a)` - filled rectangle in world space
  - `Draw.Circle(cx, cy, radius, r, g, b, a)` - circle outline
  - `Draw.FillCircle(cx, cy, radius, r, g, b, a)` - filled circle
- **Screen-space** (no camera transform, for HUD/UI):
  - `Draw.ScreenLine(x1, y1, x2, y2, r, g, b, a)`
  - `Draw.ScreenThickLine(x1, y1, x2, y2, thickness, r, g, b, a)` - thick line in screen space
  - `Draw.ScreenPolyline(points, thickness, r, g, b, a, closed)` - polyline in screen space with miter joins
  - `Draw.ScreenRect(x, y, w, h, r, g, b, a)`
  - `Draw.ScreenFillRect(x, y, w, h, r, g, b, a)`
- Color parameters use byte 0-255. Alpha defaults to 255 (opaque)
- Polyline miter joins are clamped to prevent spikes at sharp angles
- Call from `Scene.Draw()` or `Entity.Draw()` overrides

### RenderTexture (RenderTexture.cs)
- `new RenderTexture(width, height)` - create offscreen render target (OpenGL FBO)
- `rt.Begin()` / `rt.End()` - redirect rendering to/from this texture (always pair)
- `rt.Clear(r, g, b, a)` - clear the texture to a color
- `rt.Draw(x, y)` - draw to screen at original size
- `rt.Draw(x, y, w, h)` - draw scaled
- `rt.Draw(srcX, srcY, srcW, srcH, dstX, dstY, dstW, dstH)` - draw sub-region
- `rt.SetColor(r, g, b, a)` - tint/alpha for drawing
- `rt.Destroy()` - free the FBO

### Tilemap (Tilemap.cs, extends Entity)
- `new Tilemap("tileset.png", tileSize, gridWidth, gridHeight)` - create empty tilemap
- `Tilemap.Load("levels/map.json")` - load from JSON
- `tilemap.SetTile(col, row, tileIndex)` - set tile (-1 to clear). Non-empty tiles are solid by default
- `tilemap.GetTile(col, row)` - get tile index (-1 = empty)
- `tilemap.SetSolid(col, row, false)` - override collision
- `tilemap.IsSolid(col, row)` - check collision flag
- `tilemap.WorldToTile(worldX, worldY)` - convert world coords to grid coords
- `tilemap.CollideEntity(entity)` - AABB collision vs solid tiles. Returns CollisionDir flags
- `tilemap.CollideEntityOneway(entity)` - one-way platform collision vs solid tiles
- `tilemap.SetTileCollisionRect(tileIndex, x, y, w, h)` - custom collision rect for a tile type
- `tilemap.GetTileCollisionRect(tileIndex)` / `tilemap.ClearTileCollisionRect(tileIndex)`
- `tilemap.AnimateTile(baseTile, frames[], fps)` - register animated tile (all instances cycle in sync)
- Renders only visible tiles (camera culling by grid position)
- Shares texture cache with Sprite (tileset loaded once, reused)
- Supports tilemap Position offset

### LightRenderer (LightRenderer.cs)
- 2D lighting system with point lights, spot lights, and shadow casting
- Renders all lights to an FBO, then multiplies the light map over the scene
- `var lights = new LightRenderer()` - create a light renderer (not an Entity)
- `lights.AddLight(x, y, radius, color)` - add a point light, returns `Light` for config
- `lights.RemoveLight(light)` / `lights.ClearLights()`
- `lights.Render(tilemap?)` - render light map and composite. Call from Scene.Draw() after game objects
- `lights.Ambient` (Color, 20/20/30) - base ambient light. Black = full darkness, White = no lighting effect
- `lights.Enabled` (bool, true) - master enable
- `lights.LightSegments` (int, 32) - circle smoothness per light
- `lights.Dispose()` - free the FBO. Call in Scene.OnDestroy()
- **Light properties:**
  - `Position` (Vec2) - world-space position
  - `Radius` (float) - maximum range in world pixels
  - `Color` (Color) - light color
  - `Intensity` (float, 1) - brightness multiplier
  - `Active` (bool) - per-light enable
  - `CastShadows` (bool) - shadow casting against tilemap solid tiles
  - `IsSpot` (bool) - spot light mode (directional cone)
  - `Direction` (float) - spot direction in degrees (0 = right, 90 = down)
  - `ConeAngle` (float, 45) - spot half-angle in degrees
- **Shadow casting** uses 2D visibility polygon algorithm:
  - Collects exposed edges of solid tiles within light radius
  - Casts rays toward each edge vertex (+-e for corner handling)
  - Renders the visibility polygon as a triangle fan with radial falloff
- Lights rendered with **additive blending** (accumulate). Light map composited with **multiply blending**
- `BlendMode.Multiply` added to SpriteBatch/PrimitiveBatch (DstColor * Zero)
- FBO auto-resizes to match game resolution

### SpriteStack (SpriteStack.cs, extends KinematicEntity)
- `new SpriteStack(x, y)` - create a sprite stack entity
- `stack.LoadGraphic("slices.png", sliceW, sliceH)` - load spritesheet where each frame is a horizontal slice. Frame 0 = bottom, last = top. Slice count auto-calculated. Chainable
- `stack.SliceCount` - number of slices (read-only, set by LoadGraphic)
- `stack.StackOffset` (float, 1) - world pixels between slices (positive = upward). Scales with ScaleY
- `stack.VisibleSlices` (int, -1) - slices to draw from bottom. -1 = all. Use for construction/destruction reveals
- `stack.Color` (Color, White) - tint and alpha for all slices
- `stack.FlipX` / `stack.FlipY` - mirror all slices
- `stack.Angle` - rotation angle (inherited). Rotates each slice's texture, creating the pseudo-3D spin effect
- Frustum culling accounts for total stack height
- Each slice is a separate SpriteBatch quad (same texture = same batch, efficient)
- Collision bounds use the base slice dimensions (stack height is visual only)
- Textures managed by GLRenderer cache (shared, not owned)

### TrailRenderer (TrailRenderer.cs, extends Entity)
- `new TrailRenderer(maxPoints: 64)` - create a trail renderer (ring buffer capacity)
- `trail.Follow(entity)` - auto-track an entity's center position each frame
- `trail.Unfollow()` - stop tracking (trail fades out naturally)
- `trail.AddPoint(x, y)` - manually add a trail point at a world position
- `trail.Clear()` - remove all trail points immediately
- `trail.Count` - number of active trail points
- `trail.MaxPoints` - ring buffer capacity (set via constructor)
- **Configuration:**
  - `MaxLife` (float, 0.5) - how long each point persists (seconds)
  - `Thickness` (float, 4) - trail width at the head (newest point)
  - `ThicknessEnd` (float, 0) - trail width at the tail (oldest point). 0 = taper to nothing
  - `MinDistance` (float, 2) - minimum world-space distance between consecutive points
  - `ColorStart` / `ColorEnd` (Color, White) - color gradient from head to tail
  - `AlphaStart` (float, 1) / `AlphaEnd` (float, 0) - alpha gradient from head to tail
  - `FollowOffset` (Vec2) - offset from the followed entity's center
- Renders as a strip of quads with per-vertex color/alpha for smooth gradients
- Miter joins at segment boundaries prevent gaps and spikes
- Respects `ScrollFactor`, `Layer`, `ZOrder` like any Entity
- Auto-unfollows destroyed or inactive targets (like Camera)
- Ring buffer: O(1) add, no allocation during gameplay once warm
- Uses `PrimitiveBatch.DrawFilledQuadGradient` for per-vertex interpolation

### ParticleEmitter (ParticleEmitter.cs, extends Entity)
- `new ParticleEmitter(x, y)` - create an emitter entity
- `emitter.Emit(count)` - burst particles from current position
- `emitter.Count` - number of active particles
- `emitter.LoadTexture(path)` - use a sprite texture instead of colored rectangles. Chainable
- `emitter.LoadTexture(path, frameW, frameH)` - use a spritesheet; each particle picks a random frame
- Configurable: SpawnWidth/Height, MinSpeedX/Y, MaxSpeedX/Y, GravityY
- Configurable: MinLife/MaxLife, MinSize/MaxSize, ColorMin/ColorMax (Math.Color)
- Configurable: ScaleStart/ScaleEnd, FadeOut
- `BlendMode` (BlendMode, Alpha) - set to Additive for fire, magic, glow effects
- `MinAngularVelocity` / `MaxAngularVelocity` (float, 0) - particle spin in degrees/sec
- Particles are structs -- no allocations, no extra entities in the scene
- Renders as filled rectangles (default) or textured quads when texture is loaded
- Color tint multiplied with texture. Rotation applied per-particle when spinning
- Respects camera ScrollFactor and Zoom
- Optional random seed for deterministic particles

### AfterImage (AfterImage.cs, extends Entity)
- `new AfterImage(maxGhosts)` - draws fading ghost copies of a target Sprite
- `SetTarget(sprite)` / `Target` - set/get the tracked sprite
- Captures position, frame, flip, and scale at `Interval` seconds apart
- Each ghost fades from `StartAlpha` to 0 over `Duration` seconds
- Configurable: `Interval` (0.04), `Duration` (0.25), `MaxGhosts` (8), `StartAlpha` (0.5)
- `Tint` (Color) - tint color for ghost copies (default blue-white)
- `Clear()` - remove all ghosts immediately
- `ActiveCount` - number of visible ghosts
- Place on a layer below the target sprite so ghosts draw behind it
- Uses `Sprite.DrawGhost()` internally — renders the sprite's texture at a saved state

### Rain (Rain.cs, extends Entity)
- `new Rain(maxDrops)` - camera-following rain particle system
- Spawns streak-shaped drops across the camera viewport, falling with gravity
- Configurable: `Intensity` (0-1), `WindAngle` (degrees from vertical), `MinSpeed`/`MaxSpeed`
- Configurable: `MinLength`/`MaxLength` (streak size), `Thickness`, `ColorFront`/`ColorTail`
- `GroundY` (float) - world Y where drops splash and despawn. `float.MaxValue` = no ground
- `EnableSplashes` (bool) - spawn small particle bursts on ground/water impact
- `AddWaterBody(wb)` / `RemoveWaterBody(wb)` - register WaterBody surfaces; rain drops disturb them on impact via `WaterBody.Disturb()`
- `AddObstacle(entity)` / `RemoveObstacle(entity)` - register entities as rain blockers; drops splash off their collision bounds
- `SetMaxDrops(n)` - resize particle pool at runtime
- `ActiveDrops` - current live drop count; `DropsPerSecond` - spawn rate at current intensity
- Drops rendered as gradient quads (front color → tail color) for natural streaks
- Internal splash emitter (not added to scene) handles impact particles
- Wind shifts the spawn region so drops enter from the correct angle
- `Margin` (float, 60) - extra world-pixel buffer around viewport for seamless coverage

### CharacterController (CharacterController.cs)
- Generic platformer movement controller — reusable across games
- Horizontal movement, gravity, jumping with coyote time and jump buffer
- Variable jump height via `CutJump()` (jump cut multiplier)
- Wall slide and wall jump with horizontal kick lock
- Collision against solids, one-way platforms, and tilemaps
- Public state flags: `OnGround`, `WallSliding`, `IsJumping`, `TouchingWall`
- Events: `Jumped`, `WallJumped`, `Landed`
- Input via `Move(-1/0/1)`, `QueueJump()`, `CutJump()` — no animation/audio hooks

### Localization (Localization.cs)
- String table lookup via `Eng.Tr("menu.play")` or `Eng.Locale.Text(key, args)`
- Loads JSON files from `Assets/locales/{locale}.json`
- Supports `{0}`, `{1}` placeholder substitution via `string.Format`
- Fallback to the key itself when translation is missing
- `Eng.Locale.Available()` scans the folder for available locales

### Debug Console (DebugConsole.cs)
- Quake-style dropdown console — toggle with **`** (backtick) or `Eng.Actions.Pressed("engine:console")`
- Command registration via `Eng.Console.RegisterCommand(name, handler, help)`
- Built-in commands: `help`, `clear`, `quit`, `fps`, `scene <path>`, `echo`
- Features: command history (up/down arrows), autocomplete (tab), cursor editing (home/end/left/right)
- Log output via `Eng.Console.Log(message)`
- Renders with cached font textures to avoid per-frame re-rendering

### Debug Overlay (DebugOverlay.cs)
- Press **F3** to toggle (or `Eng.Debug.Enabled = true` from code)
- Shows FPS counter (updated every 0.5s for stability)
- Shows entity count (recursive into Groups), active tween count, active timer count
- Draws collision bounds for all entities: green = movable, cyan = immovable
- Bounds are culled off-screen and skip zero-size entities

### Volume Overlay (VolumeOverlay.cs)
- Triggered by `+`/`-` keys (bound via InputMap as `engine:volume_up` / `engine:volume_down`)
- Shows volume bar and percentage for 1.5 seconds after change
- Plays tick sound on volume change

### Dialogue (Dialogue.cs + DialogueBox.cs)
- **Dialogue tree** (data, `VEngine.Engine.Core`):
  - `new Dialogue()` - create a dialogue tree with fluent builder
  - `.Say(text, speaker?, portrait?)` - display text line
  - `.Ask(text, speaker, choices...)` - display text with choice buttons, each targeting a label
  - `.Label(name)` - named jump target for branching
  - `.Goto(label)` - unconditional jump
  - `.Call(action)` - execute callback mid-dialogue (game logic triggers)
  - `.SetVar(name, value)` - store a dialogue variable
  - `.IfVar(name, value, label)` - jump to label if variable equals value
  - `.IfFlag(name, label)` - jump to label if variable is truthy (bool/int/string)
  - `.IfGoto(predicate, label)` - jump to label if custom predicate returns true
  - `.GetVar<T>(name)` - read a variable with typed default
  - `.Vars` - direct access to the variable dictionary
  - `.NodeCount` - total nodes
- **DialogueBox** (visual, `VEngine.Engine.UI`, extends Entity):
  - `new DialogueBox()` - screen-space entity (ScrollFactor 0, Layer 10)
  - `box.SetPortrait(speaker, path)` - register a portrait image for a speaker name
  - `box.Start(dialogue)` - begin displaying a dialogue tree
  - `box.Stop()` - close immediately
  - `box.IsActive` - whether dialogue is playing
  - `box.OnComplete` - fires when dialogue ends
  - **Typewriter reveal**: `TypingSpeed` (chars/sec, default 30). Press Space/Enter/A to skip
  - **Portraits**: displayed left of text. Looked up by speaker name or portrait override
  - **Choices**: appear after text finishes. Navigate with Up/Down/W/S/D-pad, select with Space/Enter/A or mouse click
  - **Word wrapping**: pre-computed from full text, stable during reveal
  - Configurable: `FontSize`, `FontPath`, `TextColor`, `SpeakerColor`, `BoxColor`, `BorderColor`, `ChoiceColor`, `ChoiceSelectedColor`, `BoxHeight`, `BoxMargin`, `BoxPadding`, `PortraitSize`
- Example:
  ```csharp
  var d = new Dialogue()
      .Say("Halt! Who goes there?", "Guard")
      .Ask("Do you have a pass?", "Guard",
          ("Show pass", "pass"), ("Run away", "flee"))
      .Label("pass").Say("Proceed.", "Guard").Goto("end")
      .Label("flee").Say("Stop!", "Guard")
      .Label("end");
  var box = new DialogueBox();
  box.SetPortrait("Guard", "portraits/guard.png");
  box.Start(d);
  scene.Add(box);
  ```

### UI (UI.cs, UIStack.cs, UIScrollView.cs, UITextInput.cs, UIWidgets.cs, UIRebindButton.cs, UIFocusManager.cs)
- All UI elements extend Entity -- add to scene with `scene.Add()`
- Render in screen space (bypass camera zoom/position/shake)
- Draw on Layer 10 by default (above game content)
- Shared font cache across all UI elements via FontCache
- **UILabel**: `new UILabel("Score: 0", 10, 10)` -- screen-fixed text
  - `.Text`, `.Color`, `.FontSize`
  - `.WrapWidth` - max width for word wrapping (0 = no wrap). Auto-adjusts BaseHeight
- **UIPanel**: `new UIPanel(x, y, w, h)` -- colored rectangle
  - `.Color`, `.BorderColor` (set border alpha to 0 to hide)
- **UIButton**: `new UIButton("Play", x, y, w, h)` -- clickable button with text
  - `.OnClick = () => { }` -- fires on press-and-release
  - `.Hovered`, `.Pressed` -- read-only state
  - `.NormalColor`, `.HoverColor`, `.PressedColor`, `.TextColor`, `.BorderColor`
  - Text auto-centered in button rect
  - Press must start AND end on the button to fire click (drag-off cancels)
- **UIStack**: `new UIStack(x, y, StackDirection.Vertical, spacing: 8)` -- auto-layout container
  - `stack.AddChild(element)` / `stack.RemoveChild(element)` (chainable)
  - `stack.Spacing` - pixels between children
  - `stack.Padding` - padding before first child
  - `StackDirection.Vertical` or `StackDirection.Horizontal`
  - Auto-sizes to fit children
  - Children positions managed automatically
- **UIScrollView**: `new UIScrollView(x, y, w, h)` -- scrollable container
  - `scroll.AddChild(element)` / `scroll.RemoveChild(element)` (chainable)
  - Scroll via mouse wheel or left-click drag
  - `.Spacing`, `.Padding`, `.ScrollSpeed` - layout and scroll configuration
  - `.BackgroundColor`, `.ScrollbarColor` - visual customization
  - Children auto-laid-out vertically, clipped to view bounds
  - Scrollbar shown when content exceeds container height
  - Only visible children are updated/drawn (performance optimization)
- **UITextInput**: `new UITextInput(x, y, w, h, placeholder: "Enter name...")` -- text input field
  - `.Text` - current text content (read/write)
  - `.Placeholder` - text shown when empty and not focused
  - `.MaxLength` - maximum characters (0 = unlimited)
  - `.Focused` - whether currently focused (read-only)
  - `.OnSubmit` - fires when Enter is pressed
  - `.OnChanged` - fires when text changes
  - `.Focus()` / `.Blur()` - programmatic focus control
  - Click to focus, Escape to blur. Blinking cursor when focused
  - `.BackgroundColor`, `.FocusedColor`, `.TextColor`, `.PlaceholderColor`, `.CursorColor`, `.BorderColor`
- **UICheckbox**: `new UICheckbox("Enable Music", x, y, initial: true)` -- toggle checkbox with label
  - `.Checked` - current state (read/write)
  - `.Text` - label text
  - `.OnChanged` - fires with new bool value on click
  - `.CheckColor`, `.BoxColor`, `.BorderColor`
- **UISlider**: `new UISlider(x, y, width, min: 0, max: 1, initial: 0.5f)` -- horizontal slider
  - `.Value` - current value (clamped to Min..Max)
  - `.Min`, `.Max` - range bounds
  - `.OnChanged` - fires with new float value on drag
  - `.TrackColor`, `.FillColor`, `.HandleColor`, `.BorderColor`
  - Drag handle to change value
- **UIRebindButton**: `new UIRebindButton("Jump", "jump", x, y, w, h)` -- input rebinding widget
  - Click to enter listening mode, then press any key or gamepad button to rebind
  - Shows current binding (keyboard key or gamepad button name)
  - Escape cancels rebind. Works with `Eng.Actions` (InputMap)
  - `.Label` - display name. `.OnRebound` - fires after successful rebind
  - `.NormalColor`, `.ListeningColor`, `.HoverColor`, `.TextColor`, `.BorderColor`
- **UIFocusManager**: keyboard/gamepad navigation for UI
  - `var focus = new UIFocusManager()` -- not an entity, call Update manually
  - `focus.Add(element)` / `focus.Remove(element)` -- register focusable elements
  - `focus.SetFocus(element)` / `focus.ClearFocus()` -- programmatic focus
  - `focus.Focused` -- currently focused element (or null)
  - `focus.Update(dt)` -- process Tab, Shift+Tab, arrow keys, D-pad, Enter/Space/A
  - `focus.DrawFocusIndicator(r, g, b, a)` -- draw highlight rect around focused element
  - Skips inactive elements during navigation

### Color (Math/Color.cs)
- RGBA struct with byte channels (0-255)
- Presets: White, Black, Transparent, Red, Green, Blue, Yellow, Cyan, Magenta, Purple, Orange
- `color.WithAlpha(a)` - returns copy with different alpha
- `Color.Lerp(a, b, t)` - linear interpolation between two colors
- Implicit conversion to/from `SDL.SDL_Color` for compatibility

### Rect (Math/Rect.cs)
- Axis-aligned rectangle struct (X, Y, W, H)
- `rect.Right` / `rect.Bottom` - edge accessors
- `rect.Center` - center point as Vec2
- `rect.Overlaps(other)` - AABB overlap check
- `rect.Contains(x, y)` - point containment
- Implicit conversion to/from `(float X, float Y, float W, float H)` tuple

### Vec2 (Math/Vec2.cs)
- Value type (struct), no allocations
- Operators: `+`, `-`, `*` (scalar, both sides), unary `-`
- `Length()`, `LengthSquared()`, `Normalized()`
- `Vec2.Dot()`, `Vec2.Distance()`, `Vec2.DistanceSquared()`, `Vec2.Lerp()`
- `Vec2.AngleTo(from, to)` - angle in radians (atan2)
- Constants: Zero, One, Up, Down, Left, Right

### StateMachine (StateMachine.cs)
- `new StateMachine<MyEnum>(MyEnum.Idle)` - create with initial state (enum or string)
- `sm.On(state).Enter(() => ...).Update(dt => ...).Exit(() => ...)` - register callbacks (fluent)
- `sm.Set(state)` - transition: calls Exit on old state, Enter on new. No-op if already in that state
- `sm.Update(dt)` - call each frame. Enters initial state on first call
- `sm.Current` - the current state key
- `sm.Duration` - seconds in current state (resets on transition)

### AI Helpers (AI.cs)
- `AI.MoveToward(entity, targetX, targetY, speed, arriveDistance)` - set velocity toward a point. Returns true when arrived
- `AI.MoveToward(entity, target, speed)` - move toward another entity
- `AI.MoveAway(entity, fromX, fromY, speed)` - flee from a point
- `AI.DistanceTo(a, b)` / `AI.InRange(a, b, range)` - distance checks
- `AI.HasLineOfSight(x1, y1, x2, y2, tilemap, stepSize)` - raycast against solid tiles
- `AI.FaceToward(sprite, targetX)` - set FlipX to face a direction

### Sequence (Sequence.cs)
- Scripted sequence of timed actions for cutscenes and scripted events
- Composes Tweens and Timers into a declarative, chainable API
- **Steps:**
  - `.Call(action)` - execute callback immediately, advance to next step
  - `.Wait(seconds)` - pause for duration before advancing
  - `.TweenFloat(getter, setter, target, duration, ease)` - tween a float, advance when complete
  - `.TweenVec2(getter, setter, target, duration, ease)` - tween a Vec2
  - `.TweenColor(getter, setter, target, duration, ease)` - tween a Color
  - `.Together(builders...)` - run multiple sub-sequences in parallel, advance when ALL finish
- `.OnComplete(callback)` - fires when entire sequence finishes (not on Cancel)
- `.Start()` - start or restart the sequence. Chainable
- `.Cancel()` - stop the sequence, cancel active timers/tweens
- `.Running` - whether the sequence is active
- `.CurrentStep` / `.StepCount` - progress tracking
- Auto-cancelled on scene switch (uses Eng.Timers and Eng.Tweens internally)
- No Update() needed -- progression is callback-driven via timer/tween completion
- Example:
  ```csharp
  new Sequence()
      .Call(() => player.Active = false)
      .TweenFloat(() => cam.Position.X, v => cam.Position.X = v, 500, 1f, Ease.InOutQuad)
      .Wait(0.5f)
      .Call(() => Eng.Effects.Flash(255, 255, 255, 0.3f))
      .Together(
          s => s.TweenFloat(() => cam.Position.X, v => cam.Position.X = v, 0, 1f),
          s => s.TweenFloat(() => cam.Position.Y, v => cam.Position.Y = v, 0, 1f)
      )
      .Call(() => player.Active = true)
      .Start();
  ```

### ScreenCapture (ScreenCapture.cs)
- `Eng.CaptureScreen()` - capture at end of current frame, saves to Screenshots/ folder. Returns path
- `Eng.CaptureScreen("path/to/file.png")` - capture to a specific file path
- `ScreenCapture.ScreenshotsPath` - configurable screenshots folder (default: "Screenshots" next to exe)
- Press **F12** to capture (bound via InputMap as `engine:screenshot`)
- Captures after post-processing but before buffer swap (always gets the final composited frame)
- Output is PNG at viewport resolution (game area only, no letterbox bars)
- Built-in minimal PNG encoder (ZLibStream compression, CRC32 -- no extra NuGet dependencies)
- Auto-creates directories if the path doesn't exist

### SaveData (SaveData.cs)
- `SaveData.Save("slot1.json", myObject)` - serialize any object to JSON
- `SaveData.Load<MyClass>("slot1.json")` - deserialize (returns default if not found)
- `SaveData.Exists("slot1.json")` / `SaveData.Delete("slot1.json")`
- `SaveData.List("*.json")` - list save filenames matching a pattern
- `SaveData.SavesPath` - configurable saves folder (default: "Saves" next to exe)

### AssetLoader (AssetLoader.cs)
- Asynchronous asset preloader for loading screens and stutter-free scene transitions
- Decodes images on a background thread, uploads to GPU on the main thread
- Access via `Eng.Loader`
- `Eng.Loader.Enqueue("sprites/hero.png", "sprites/enemy.png")` - queue asset paths
- `Eng.Loader.Start()` - begin background loading of all enqueued assets
- `Eng.Loader.ProcessUploads(maxPerFrame: 2)` - upload decoded images to GPU (call each frame)
- `Eng.Loader.Progress` - 0 to 1 progress (thread-safe)
- `Eng.Loader.Done` - true when all assets loaded and uploaded
- `Eng.Loader.Remaining` - assets still pending
- `Eng.Loader.Reset()` - reset for reuse
- Skips assets already in the texture cache
- PNG/JPG decoded via StbImageSharp on background thread; BMP read as raw bytes

### Behavior (Behavior.cs)
- Attachable behavior for entity composition alongside inheritance
- Subclass `Behavior` and override: `OnAttach()`, `Update(dt)`, `OnDestroy()`
- `Owner` property provides access to the entity the behavior is attached to
- Attach via `entity.AddBehavior(new MyBehavior())` (see Entity section above)
- Use for modular logic: health, flicker-on-hit, patrol, etc. without deep class hierarchies

### ChunkedTilemap (ChunkedTilemap.cs, extends Entity)
- Chunked tilemap for large/infinite worlds. Tiles stored in fixed-size chunks loaded on demand
- `new ChunkedTilemap("tileset.png", tileSize: 16, chunkSize: 32)` - create with tileset, tile size, and chunk size
- **Tile Access** (infinite coordinates, works with negative values):
  - `world.SetTile(col, row, tileIndex)` - set tile at any coordinate (creates chunk if needed)
  - `world.GetTile(col, row)` - get tile index (-1 = empty)
  - `world.IsSolid(col, row)` / `world.SetSolid(col, row, false)` - collision flags
  - `world.WorldToTile(worldX, worldY)` - convert world coords to tile coords
- **Chunk Streaming:**
  - `world.ChunkLoader = (cx, cy) => LoadChunkFromFile(cx, cy)` - callback to load chunks on demand
  - `world.LoadRadius = 3` - how many chunks around camera to keep loaded (default 3)
  - Chunks beyond `LoadRadius + 1` are automatically unloaded
  - `world.LoadedChunkCount` - number of chunks currently in memory
- **Collision:**
  - `world.CollideEntity(entity)` - AABB collision vs nearby solid tiles. Returns `CollisionDir`
  - `world.SetTileCollisionRect(tileIndex, x, y, w, h)` - custom collision rect per tile type
- **Rendering:**
  - Only visible tiles drawn (camera culling)
  - Animated tiles: `world.AnimateTile(baseTile, frames, fps)` - all instances cycle in sync
- `ChunkData` class for ChunkLoader return values: `Tiles` array + optional `Solid` array

### RenderLayer (RenderLayer.cs)
- Independent render layer with its own camera and framebuffer
- Use for minimaps, split-screen, HUD at native resolution, or any rendering with a different camera
- `new RenderLayer(screenX, screenY, width, height)` - create a layer
- `layer.Camera` - independent Camera instance (separate from Eng.Camera)
- `layer.Begin()` - swap to layer's camera and FBO, flush pending main draws
- `layer.End()` - restore main camera, composite layer to screen at (ScreenX, ScreenY)
- `layer.ScreenX`, `layer.ScreenY` - screen position where layer renders
- `layer.Width`, `layer.Height` - render dimensions
- `layer.ColorR/G/B/A` - tint/alpha applied when compositing to screen
- FBO created lazily on first Begin() call
- Implements `IDisposable` -- call Dispose() to free the FBO

### LevelLoader (LevelLoader.cs)
- `LevelLoader.Load(scene, path, out solids, out platforms)` - loads level JSON, creates entities
- `LevelLoader.GetSize(path)` - reads level dimensions without loading entities
- Creates Sprite entities with MakeGraphic for each solid/platform
- Sets Immovable = true on all level geometry

## Conventions

- Entity has position/scale only. KinematicEntity adds velocity/acceleration
- Scene.Draw() and Entity.Draw() take no parameters -- use Eng.GL for rendering
- Scale lives on Entity (ScaleX, ScaleY). Width/Height are computed properties
- Scene.Remove() destroys by default. Pass `destroy: false` to keep the entity alive
- Sprite textures are managed by GLRenderer's TextureCache (shared, not owned by individual sprites)
- MakeGraphic() uses a shared 1x1 white pixel texture (no render target creation)
- One sprite can swap spritesheets at runtime (call LoadGraphic or LoadFromMeta again)
- With origin points, feet alignment across animation swaps is automatic
- Use LoadFromMeta() instead of hardcoding frame sizes when possible
- Timers and tweens are auto-cancelled on scene switch (SwapScene calls CancelAll on both)
- Pool uses composition (pool.Group), not inheritance. Call pool.AddTo(scene) to include in scene
- Color type (VEngine.Engine.Math.Color) used throughout -- implicit conversion to SDL_Color
- Signal.Subscribe() returns an unsubscribe Action for easy cleanup
- Eng.Context can be swapped for unit testing (EngineContext holds all subsystems)

## Bug Fixes / Safety Guards

- `Effects.Freeze(0)` is a no-op (prevents permanent freeze)
- `Camera.Shake(0)` is a no-op (prevents division by zero)
- `AI.MoveToward` guards `dist < 0.001` (prevents division by zero on normalization)
- `FontCache.Shutdown` has per-font try-catch (prevents resource leak if one font fails to close)
- `AudioManager.SetSoundPosition` cleans up `_channelVolumes` on halted channels
