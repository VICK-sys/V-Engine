# V-Engine Architecture Flowchart

## Game Loop (Game.cs)

```
┌─────────────────────────────────────────────────────────────┐
│                        FRAME START                          │
└─────────────────────┬───────────────────────────────────────┘
                      ▼
              ┌───────────────┐
              │ ApplyPending  │  Scene switch deferred to
              │    Scene      │  start of next frame
              └───────┬───────┘
                      ▼
              ┌───────────────┐
              │  Poll Events  │  SDL2 input events
              │  (SDL2)       │  (keyboard, mouse, gamepad)
              └───────┬───────┘
                      ▼
              ┌───────────────┐
              │ Handle Engine │  F3=debug, F11=fullscreen,
              │   Actions     │  F12=screenshot, +/-=volume
              └───────┬───────┘
                      ▼
              ┌───────────────┐
              │ Effects.Update│  Flash fade, freeze timer
              │   (realDt)    │  (runs on REAL time)
              └───────┬───────┘
                      ▼
        ┌─────────────────────────────┐
        │   FIXED TIMESTEP LOOP       │
        │   (accumulator-based)       │
        │   ┌───────────────────────┐ │
        │   │  Update Time          │ │
        │   │  (scaledDt, total)    │ │
        │   └─────────┬─────────────┘ │
        │             ▼               │
        │   ┌───────────────────────┐ │
        │   │  Timers.Update        │ │
        │   │  (delayed/repeating)  │ │
        │   └─────────┬─────────────┘ │
        │             ▼               │
        │   ┌───────────────────────┐ │
        │   │  Tweens.Update        │ │
        │   │  (float/Vec2/Color)   │ │
        │   └─────────┬─────────────┘ │
        │             ▼               │
        │   ┌───────────────────────┐ │
        │   │  OnPreUpdate Signal   │ │
        │   │  (user hooks)         │ │
        │   └─────────┬─────────────┘ │
        │             ▼               │
        │   ┌───────────────────────┐ │
        │   │  Scene.Update(dt)     │◄┼── Entities update backward
        │   │  ├─ Group.Update      │ │   (safe removal during iteration)
        │   │  │  ├─ Entity.Update  │ │
        │   │  │  ├─ Behaviors      │ │
        │   │  │  └─ Collision      │ │
        │   │  └─ Game logic        │ │
        │   └─────────┬─────────────┘ │
        │             ▼               │
        │   ┌───────────────────────┐ │
        │   │  Camera.Update        │ │
        │   │  (follow, shake,      │ │
        │   │   bounds, zoom)       │ │
        │   └─────────┬─────────────┘ │
        │             ▼               │
        │   ┌───────────────────────┐ │
        │   │  OnPostUpdate Signal  │ │
        │   └─────────┬─────────────┘ │
        │             ▼               │
        │   ┌───────────────────────┐ │
        │   │  Transition.Update    │ │
        │   │  VolumeOverlay.Update │ │
        │   └─────────────────────┘   │
        │   (loops if accumulator     │
        │    has more time)           │
        └─────────────┬───────────────┘
                      ▼
┌─────────────────────────────────────────────────────────────┐
│                     RENDER PHASE                            │
│                                                             │
│  ┌──────────────┐                                           │
│  │  BeginFrame   │  glClear, set viewport                   │
│  └──────┬───────┘                                           │
│         ▼                                                   │
│  ┌──────────────┐                                           │
│  │ PostProcess   │  Redirect to ping FBO (if enabled)       │
│  │   .Begin()    │                                          │
│  └──────┬───────┘                                           │
│         ▼                                                   │
│  ┌──────────────┐                                           │
│  │ OnPreDraw    │  Signal (user hooks)                      │
│  └──────┬───────┘                                           │
│         ▼                                                   │
│  ┌──────────────────────────────────┐                       │
│  │ Scene.Draw()                     │                       │
│  │  ├─ Sort by Layer, then ZOrder   │                       │
│  │  ├─ Sprite.Draw() ──► SpriteBatch│──► up to 8192 quads  │
│  │  ├─ Tilemap.Draw() (culled)      │    8 textures/batch   │
│  │  ├─ Text.Draw()                  │                       │
│  │  ├─ Draw.* helpers               │──► PrimitiveBatch     │
│  │  └─ LightRenderer.Render()      │                       │
│  └──────┬───────────────────────────┘                       │
│         ▼                                                   │
│  ┌──────────────┐                                           │
│  │  FlushAll()  │  Submit batches to GPU                    │
│  └──────┬───────┘                                           │
│         ▼                                                   │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │Effects (flash)│─►│ Transition   │─►│DebugOverlay  │      │
│  └──────────────┘  └──────────────┘  │VolumeOverlay │      │
│                                      └──────┬───────┘      │
│         ┌───────────────────────────────────┘               │
│         ▼                                                   │
│  ┌──────────────┐                                           │
│  │ OnPostDraw   │  Signal (user hooks)                      │
│  └──────┬───────┘                                           │
│         ▼                                                   │
│  ┌──────────────┐                                           │
│  │  FlushAll()  │  Final flush                              │
│  └──────┬───────┘                                           │
│         ▼                                                   │
│  ┌──────────────┐                                           │
│  │ PostProcess   │  Ping-pong FBO passes → screen           │
│  │   .End()      │  (CRT, Glitch, custom shaders)           │
│  └──────┬───────┘                                           │
│         ▼                                                   │
│  ┌──────────────┐                                           │
│  │  EndFrame     │  SDL_GL_SwapWindow (present)             │
│  └──────────────┘                                           │
└─────────────────────────────────────────────────────────────┘
```

## Subsystem Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Eng (Static Global)                       │
│                    └─ EngineContext                          │
├────────────┬────────────┬────────────┬──────────────────────┤
│            │            │            │                      │
▼            ▼            ▼            ▼                      ▼
┌────────┐ ┌──────┐ ┌─────────┐ ┌──────────┐         ┌──────────┐
│ Game   │ │  GL  │ │  Input  │ │  Audio   │         │ Helpers  │
│        │ │Render│ │         │ │          │         │          │
│ Loop   │ │      │ │Keyboard │ │PlaySound │         │ Timers   │
│ Scenes │ │Sprite│ │Mouse    │ │PlayMusic │         │ Tweens   │
│ Stack  │ │Batch │ │Gamepad  │ │Spatial   │         │ Effects  │
│        │ │Prim  │ │InputMap │ │          │         │ Camera   │
│        │ │Batch │ │         │ │          │         │ Signals  │
└───┬────┘ └──┬───┘ └────┬────┘ └──────────┘         └──────────┘
    │         │          │
    ▼         ▼          ▼
┌──────────────────────────────────────┐
│              Scene                    │
│  ┌─────────────────────────────────┐ │
│  │ Group (_root)                   │ │
│  │  ┌──────────┐  ┌─────────────┐ │ │
│  │  │  Entity   │  │KinematicEnt│ │ │
│  │  │  ├─pos    │  │ ├─velocity  │ │ │
│  │  │  ├─scale  │  │ ├─accel     │ │ │
│  │  │  ├─layer  │  │ └─angVel    │ │ │
│  │  │  └─behav. │  └──────┬──────┘ │ │
│  │  └──────────┘          │        │ │
│  │         ┌──────────────┼────┐   │ │
│  │         ▼              ▼    ▼   │ │
│  │    ┌────────┐  ┌──────┐ ┌────┐ │ │
│  │    │ Sprite │  │ Text │ │ UI │ │ │
│  │    │ sprtsht│  │ TTF  │ │    │ │ │
│  │    │ anim   │  │ atlas│ │    │ │ │
│  │    │ effects│  └──────┘ └────┘ │ │
│  │    └────────┘                  │ │
│  │  ┌──────────┐  ┌────────────┐  │ │
│  │  │ Tilemap  │  │ParticleEmt │  │ │
│  │  │ ChunkMap │  │TrailRender │  │ │
│  │  └──────────┘  │SpriteStack │  │ │
│  │                └────────────┘  │ │
│  └────────────────────────────────┘ │
│  ┌────────────┐  ┌───────────────┐  │
│  │LightRender │  │  Collision    │  │
│  │ point/spot │  │ AABB/circle   │  │
│  │ shadows    │  │ sweep CCD     │  │
│  └────────────┘  │ SpatialHash   │  │
│                  └───────────────┘  │
└──────────────────────────────────────┘
```

## Rendering Pipeline

```
Game Objects                Batching                     GPU
───────────                 ────────                     ───

Sprite.Draw()  ──┐
Tilemap.Draw() ──┼──► SpriteBatch ──────► glDrawElements
SpriteStack    ──┤    (8192 quads max)    (1 draw call)
Text.Draw()    ──┘    (8 textures max)

Draw.Line()    ──┐
Draw.Rect()    ──┼──► PrimitiveBatch ───► glDrawElements
Draw.Circle()  ──┘    (lines, filled)     (1 draw call)

                       ┌─────────┐
                       │ Flush   │  Triggers when:
                       └────┬────┘  • batch full
                            │       • blend mode changes
                            │       • texture slots full
                            ▼       • manual FlushAll()
                  ┌──────────────────┐
                  │   PostProcess    │
                  │  ┌────┐ ┌────┐  │
                  │  │ping│◄►│pong│  │  Ping-pong FBOs
                  │  └────┘ └────┘  │  per shader pass
                  │  Pass 1 → 2 → N │
                  └────────┬─────────┘
                           ▼
                    ┌─────────────┐
                    │   Screen    │
                    │ (swap buf)  │
                    └─────────────┘
```

## Scene Lifecycle

```
SwitchScene(new MyScene(), transition)
    │
    ▼
┌──────────────┐     ┌──────────────────────────────┐
│ Transition   │────►│ Out phase (0 → 0.5)          │
│ starts       │     │ Old scene still updates/draws │
└──────────────┘     └──────────────┬───────────────┘
                                    ▼  (midpoint)
                     ┌──────────────────────────────┐
                     │ SwapScene()                   │
                     │  1. Timers.CancelAll()        │
                     │  2. Tweens.CancelAll()        │
                     │  3. Old scene.Destroy()       │
                     │  4. New scene.Create()        │
                     └──────────────┬───────────────┘
                                    ▼
                     ┌──────────────────────────────┐
                     │ In phase (0.5 → 1.0)         │
                     │ New scene updates/draws       │
                     └──────────────┬───────────────┘
                                    ▼
                     ┌──────────────────────────────┐
                     │ Transition complete           │
                     └──────────────────────────────┘
```

## Entity Hierarchy

```
Entity (base)
├── Position, Scale, Angle, Layer, ZOrder
├── Active, Visible, Destroyed
├── Behaviors (composition)
├── AABB collision bounds
│
├── KinematicEntity
│   ├── Velocity, Acceleration, AngularVelocity
│   │
│   ├── Sprite
│   │   ├── Spritesheet, Animations, Flip
│   │   ├── Flash, Silhouette, Outline
│   │   └── Per-animation hitbox, origin, damageBox
│   │
│   └── SpriteStack (pseudo-3D slices)
│
├── Group (entity container)
│   └── O(1) removal, sorted draw
│
├── Tilemap / ChunkedTilemap
├── Text (TTF / GlyphAtlas)
├── ParticleEmitter (struct particles)
├── TrailRenderer (ring buffer)
├── DialogueBox
└── UI elements (UIButton, UILabel, UIPanel, etc.)
```
