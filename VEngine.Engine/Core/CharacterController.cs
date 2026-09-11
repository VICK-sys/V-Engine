using System;
using VEngine.Engine.Graphics;
using VEngine.Engine.Math;

namespace VEngine.Engine.Core;

/// <summary>
/// Reusable platformer character movement controller. Handles horizontal movement,
/// gravity, jumping (with coyote time, jump buffering, variable jump height),
/// wall slide, and wall jump. Does NOT handle animation, audio, or game logic —
/// compose those in your game-specific controller by observing the public state flags.
///
/// Usage:
///   var cc = new CharacterController(sprite) {
///       Solids = levelSolids,
///       SolidTilemap = tilemap,
///       WorldWidth = level.Width,
///   };
///   // In update:
///   cc.Move(inputX);                 // -1 (left), 0 (idle), 1 (right)
///   if (jumpPressed) cc.QueueJump(); // buffered — executes when grounded or during coyote time
///   if (jumpReleased) cc.CutJump();  // variable jump height
///   cc.Update(dt);
///   // Read cc.OnGround, cc.WallSliding, cc.IsJumping, etc. to drive animations.
/// </summary>
public class CharacterController
{
    public Sprite Sprite { get; }

    // ── Movement Tuning ────────────────────────────────────

    /// <summary>Horizontal movement speed in pixels/sec.</summary>
    public float MoveSpeed = 150f;

    /// <summary>Upward jump velocity (negative = up). Example: -400 for a snappy jump.</summary>
    public float JumpForce = -400f;

    /// <summary>When jump is released mid-ascent, clamp upward velocity to this fraction. 0.5 = cut to half.</summary>
    public float JumpCutMultiplier = 0.5f;

    /// <summary>Gravity acceleration in pixels/sec². Applied to Sprite.Acceleration.Y.</summary>
    public float Gravity = 600f;

    /// <summary>Max downward fall speed. Prevents runaway velocity in long falls.</summary>
    public float MaxFallSpeed = 800f;

    /// <summary>Wall slide downward speed (slower than free fall).</summary>
    public float WallSlideSpeed = 50f;

    /// <summary>Horizontal kick applied during a wall jump.</summary>
    public float WallJumpForceX = 200f;

    /// <summary>Upward kick applied during a wall jump.</summary>
    public float WallJumpForceY = -380f;

    /// <summary>Duration after leaving ground during which jump still works.</summary>
    public float CoyoteTime = 0.08f;

    /// <summary>Jump input is remembered for this long if pressed before landing.</summary>
    public float JumpBufferTime = 0.1f;

    /// <summary>Horizontal input is suppressed for this long after a wall jump.</summary>
    public float WallJumpLockTime = 0.15f;

    // ── Collision Targets ──────────────────────────────────

    /// <summary>Group of solid entities the controller collides with (walls, floors).</summary>
    public Group Solids = null!;

    /// <summary>Optional one-way platforms. Can drop through by moving down through them.</summary>
    public Group? Platforms;

    /// <summary>Optional tilemap with solid tiles. Uses Tilemap.CollideEntity for broad-phase collision.</summary>
    public Tilemap? SolidTilemap;

    /// <summary>World width in pixels. Used to clamp the sprite inside level bounds. 0 = no clamp.</summary>
    public int WorldWidth;

    /// <summary>When true, crouch input allows dropping through one-way platforms.</summary>
    public bool DropThroughPlatforms;

    // ── Public State (read-only from game code) ────────────

    /// <summary>True when standing on solid ground or a platform this frame.</summary>
    public bool OnGround { get; private set; }

    /// <summary>True when sliding down a wall.</summary>
    public bool WallSliding { get; private set; }

    /// <summary>True while moving upward from a jump (velocity.Y &lt; 0 after a Jump call).</summary>
    public bool IsJumping { get; private set; }

    /// <summary>True this frame when the controller is touching a wall (left or right).</summary>
    public bool TouchingWall { get; private set; }

    /// <summary>Last collision flags from the most recent Update. Inspect for bespoke reactions.</summary>
    public CollisionDir LastCollision { get; private set; }

    /// <summary>True when the sprite should face left (updated by Move()).</summary>
    public bool FacingLeft => Sprite.FlipX;

    // ── Internal Timers ────────────────────────────────────

    private float _coyoteTimer;
    private float _jumpBuffer;
    private float _wallJumpLock;
    private float _moveInput; // -1/0/1 from last Move() call

    // ── Events ─────────────────────────────────────────────

    /// <summary>Fired when the character leaves the ground via a jump.</summary>
    public event Action? Jumped;

    /// <summary>Fired when the character executes a wall jump.</summary>
    public event Action? WallJumped;

    /// <summary>Fired when the character lands on ground after being airborne.</summary>
    public event Action? Landed;

    private bool _wasOnGround;

    public CharacterController(Sprite sprite)
    {
        Sprite = sprite;
    }

    // ── Input Methods ──────────────────────────────────────

    /// <summary>Set horizontal movement input: -1 = left, 0 = idle, 1 = right.</summary>
    public void Move(float direction)
    {
        _moveInput = System.Math.Clamp(direction, -1f, 1f);
    }

    /// <summary>Request a jump. Will be executed if grounded, within coyote time, or touching a wall.</summary>
    public void QueueJump()
    {
        _jumpBuffer = JumpBufferTime;
    }

    /// <summary>Cut current jump short (variable jump height). Call when jump button is released.</summary>
    public void CutJump()
    {
        if (IsJumping && Sprite.Velocity.Y < 0)
        {
            Sprite.Velocity.Y *= JumpCutMultiplier;
            IsJumping = false;
        }
    }

    // ── Update ─────────────────────────────────────────────

    public void Update(float dt)
    {
        // Decay timers
        if (_jumpBuffer > 0) _jumpBuffer -= dt;
        if (_wallJumpLock > 0) _wallJumpLock -= dt;

        // Coyote time: refresh when grounded, decay when airborne
        if (OnGround)
            _coyoteTimer = CoyoteTime;
        else if (_coyoteTimer > 0)
            _coyoteTimer -= dt;

        // Horizontal movement (suppressed during wall jump lock)
        if (_wallJumpLock <= 0)
        {
            Sprite.Velocity.X = _moveInput * MoveSpeed;
            if (_moveInput < 0) Sprite.FlipX = true;
            else if (_moveInput > 0) Sprite.FlipX = false;
        }

        // Gravity
        Sprite.Acceleration.Y = Gravity;

        // Collision pass
        bool wasOnGround = _wasOnGround;
        LastCollision = RunCollision();

        // Clamp fall speed
        if (Sprite.Velocity.Y > MaxFallSpeed)
            Sprite.Velocity.Y = MaxFallSpeed;

        // World bounds clamp
        if (WorldWidth > 0)
        {
            var b = Sprite.GetCollisionBounds();
            if (b.X < 0) Sprite.Position.X -= b.X;
            if (b.X + b.W > WorldWidth)
                Sprite.Position.X -= (b.X + b.W - WorldWidth);
        }

        // Wall detection
        TouchingWall = (LastCollision & (CollisionDir.Left | CollisionDir.Right)) != 0;

        // Wall slide check
        bool pushingLeft = _moveInput < 0 && (LastCollision & CollisionDir.Left) != 0;
        bool pushingRight = _moveInput > 0 && (LastCollision & CollisionDir.Right) != 0;
        bool pushingIntoWall = pushingLeft || pushingRight;

        if (!OnGround && pushingIntoWall && Sprite.Velocity.Y >= 0)
            WallSliding = true;
        else if (OnGround || !pushingIntoWall || Sprite.Velocity.Y < -10f)
            WallSliding = false;

        if (WallSliding)
        {
            Sprite.Velocity.Y = WallSlideSpeed;
            // Face the wall
            Sprite.FlipX = (LastCollision & CollisionDir.Left) != 0;
        }

        // Try wall jump
        bool canWallJump = WallSliding || (!OnGround && TouchingWall && pushingIntoWall);
        if (canWallJump && _jumpBuffer > 0)
        {
            ExecuteWallJump();
        }
        // Try normal jump (ground or coyote)
        else if (_jumpBuffer > 0 && (OnGround || _coyoteTimer > 0))
        {
            ExecuteJump();
        }

        // Track jumping state — ends when velocity turns downward
        if (IsJumping && Sprite.Velocity.Y >= 0)
            IsJumping = false;

        // Landing event
        if (OnGround && !wasOnGround)
            Landed?.Invoke();
        _wasOnGround = OnGround;

        // Reset move input for next frame (forces caller to set it every frame)
        _moveInput = 0;
    }

    // ── Internal ───────────────────────────────────────────

    private CollisionDir RunCollision()
    {
        OnGround = false;
        var flags = Collision.SeparateGroup(Sprite, Solids);
        if ((flags & CollisionDir.Top) != 0) OnGround = true;

        if (SolidTilemap != null)
        {
            var tmFlags = SolidTilemap.CollideEntity(Sprite);
            if ((tmFlags & CollisionDir.Top) != 0) OnGround = true;
            flags |= tmFlags;
        }

        if (Platforms != null && !DropThroughPlatforms)
        {
            var pFlags = Collision.SeparateGroup(Sprite, Platforms, oneWay: true);
            if ((pFlags & CollisionDir.Top) != 0) OnGround = true;
            flags |= pFlags;
        }

        return flags;
    }

    private void ExecuteJump()
    {
        Sprite.Velocity.Y = JumpForce;
        _jumpBuffer = 0;
        _coyoteTimer = 0;
        OnGround = false;
        IsJumping = true;
        Jumped?.Invoke();
    }

    private void ExecuteWallJump()
    {
        // Wall is on the side opposite to facing (you push into it)
        bool wallOnLeft = (LastCollision & CollisionDir.Left) != 0;
        Sprite.Velocity.Y = WallJumpForceY;
        Sprite.Velocity.X = wallOnLeft ? WallJumpForceX : -WallJumpForceX;
        Sprite.FlipX = !wallOnLeft;
        _jumpBuffer = 0;
        _wallJumpLock = WallJumpLockTime;
        WallSliding = false;
        IsJumping = true;
        OnGround = false;
        WallJumped?.Invoke();
    }
}
