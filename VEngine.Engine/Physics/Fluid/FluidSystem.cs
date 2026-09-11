using System;
using System.Collections.Generic;
using VEngine.Engine.Core;
using VEngine.Engine.Math;
using VEngine.Engine.Physics.Shapes;

namespace VEngine.Engine.Physics.Fluid;

/// <summary>
/// Position Based Fluids (PBF) simulation system (Macklin &amp; Müller 2013).
/// Constraint-based instead of force-based — much more stable and incompressible
/// than traditional WCSPH. Particles grow dynamically with no hard limit.
/// </summary>
public class FluidSystem : Entity
{
    // ── Buoyancy ──
    /// <summary>Fluid density for buoyancy calculation. Higher = stronger upward force on submerged objects.</summary>
    public float FluidDensity = 2.5f;
    /// <summary>Linear drag on submerged bodies (velocity damping).</summary>
    public float LinearDrag = 3f;
    /// <summary>Angular drag on submerged bodies.</summary>
    public float AngularDrag = 2f;
    /// <summary>Direct velocity multiplier per second when fully submerged. 0.05 = lose 95% speed/sec.</summary>
    public float VelocityDamping = 0.05f;
    /// <summary>Extra velocity kill at the surface boundary (0-1). Prevents bobbing.</summary>
    public float SurfaceDamping = 0.92f;

    // ── Solver Mode ──
    /// <summary>Use WCSPH force-based solver (natural splashing) instead of PBF constraint-based.</summary>
    public bool UseWCSPH;
    /// <summary>WCSPH gas constant (stiffness). Reference: 200.</summary>
    public float GasConstant = 200f;
    /// <summary>WCSPH viscosity. Reference: 10.</summary>
    public float WCSPHViscosity = 10f;
    /// <summary>WCSPH surface tension. Reference: 0.5.</summary>
    public float WCSPHSurfaceTension = 0.5f;
    /// <summary>Per-frame velocity damping. Reference: 0.998.</summary>
    public float Damping = 0.998f;

    // ── PBF Simulation Parameters ──
    public float SmoothingRadius;
    public float ParticleMass = 1f;
    /// <summary>Target rest density. Auto-computed from kernel if left at 0.</summary>
    public float RestDensity;
    /// <summary>Number of constraint solver iterations per frame. More = stiffer fluid.</summary>
    public int SolverIterations = 4;
    /// <summary>Relaxation parameter (epsilon). Prevents division by zero in constraint solve. ~100-600.</summary>
    public float Relaxation = 300f;
    /// <summary>Surface tension strength via the scorr correction term.</summary>
    public float SurfaceTensionK = 0.1f;
    /// <summary>XSPH viscosity — smooths velocity field for cohesive flow. 0-0.2.</summary>
    public float XSPHViscosity = 0.07f;
    public Vec2 Gravity = new(0, 400f);
    public float MaxSpeed = 800f;
    /// <summary>Physics sub-steps per frame. More = smoother, more stable. 1-6.</summary>
    public int SubSteps = 3;

    // ── DFSPH Extensions ──
    /// <summary>Divergence correction iterations. Eliminates compression when streams merge. 0 = off.</summary>
    public int DivergenceIterations = 2;
    /// <summary>Vorticity confinement strength. Restores rotational motion for natural turbulence. 0 = off.</summary>
    public float VorticityStrength = 0.15f;

    // ── Boundaries ──
    public float BoundsLeft, BoundsTop, BoundsRight, BoundsBottom;
    public float BoundaryDamping = 0.3f;

    // ── Pouring ──
    public bool Pouring;
    public Vec2 PourPosition;
    public float PourRate = 60f;
    public Vec2 PourVelocity = new(0, 100f);
    public float PourSpread = 8f;

    // ── Physics Interaction ──
    public float BodyForceScale = 0.5f;

    // ── Particle Merging ──
    public int MergeInterval = 15;
    public float MergeSpeedThreshold = 60f;
    public int MergeMinNeighbors = 6;

    // ── Particle Splitting ──
    public float SplitSpeedThreshold = 200f;
    public int SplitSurfaceNeighbors = 5;

    /// <summary>Hard particle budget. Emergency merge fires when exceeded. 0 = unlimited.</summary>
    public int ParticleBudget = 1500;

    /// <summary>Use native C++ solver when available (auto-detected). Set to false to force managed.</summary>
    public bool UseNativeSolver { get; set; } = true;
    public bool UsingNativeSolver => UseNativeSolver && !UseWCSPH && _nativeAvailable && _nativeSolver != IntPtr.Zero;

    // ── Internals ──
    private FluidParticle[] _particles;
    private int _activeCount;
    private int _capacity;
    private readonly FluidSpatialHash _hash;
    private readonly FluidRenderer _renderer;
    private float _pourAccumulator;
    private int _mergeCounter;
    private readonly Random _rng;

    // Native solver
    private IntPtr _nativeSolver;
    private bool _nativeAvailable;
    private float[]? _soaPX, _soaPY, _soaVX, _soaVY, _soaWeight, _soaDensity;
    private int[]? _soaActive, _soaNeighborCount;

    // Neighbor cache
    private const int MaxNeighborsPerParticle = 48;
    private int[] _neighborData;
    private int[] _neighborCounts;
    private readonly List<int> _tempNeighbors = new(64);

    // Free slot stack
    private readonly Stack<int> _freeSlots;

    // Active index list
    private int[] _activeIndices;
    private int _activeIndexCount;

    // PBF working arrays
    private Vec2[] _predicted;   // predicted positions
    private float[] _lambda;     // constraint multipliers
    private Vec2[] _deltaPos;    // position corrections

    // DFSPH working arrays
    private float[] _divergence; // velocity divergence per particle
    private float[] _vorticity;  // 2D curl (scalar) per particle

    public int Capacity => _capacity;
    public int ActiveCount => _activeCount;
    public FluidRenderer Renderer => _renderer;
    internal FluidParticle[] Particles => _particles;

    public FluidSystem(int initialCapacity = 512, float smoothingRadius = 16f, int? seed = null)
    {
        _capacity = initialCapacity;
        SmoothingRadius = smoothingRadius;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _particles = new FluidParticle[initialCapacity];
        _hash = new FluidSpatialHash(smoothingRadius);
        _renderer = new FluidRenderer();
        _neighborData = new int[initialCapacity * MaxNeighborsPerParticle];
        _neighborCounts = new int[initialCapacity];
        _activeIndices = new int[initialCapacity];
        _predicted = new Vec2[initialCapacity];
        _lambda = new float[initialCapacity];
        _deltaPos = new Vec2[initialCapacity];
        _divergence = new float[initialCapacity];
        _vorticity = new float[initialCapacity];
        _freeSlots = new Stack<int>(initialCapacity);
        for (int i = initialCapacity - 1; i >= 0; i--)
            _freeSlots.Push(i);
        FluidKernels.SetSmoothingRadius(smoothingRadius);

        // Auto-compute rest density from hexagonal packing at h/2 spacing
        if (RestDensity <= 0)
        {
            float spacing = smoothingRadius * 0.5f;
            RestDensity = ParticleMass * FluidKernels.Poly6(0);
            RestDensity += 6f * ParticleMass * FluidKernels.Poly6(spacing * spacing);
        }

        Layer = 3;

        // Try to initialize native solver
        _nativeAvailable = NativeFluidSolver.IsAvailable();
        if (_nativeAvailable)
        {
            _nativeSolver = NativeFluidSolver.fluid_create(initialCapacity, smoothingRadius);
            _soaPX = new float[initialCapacity];
            _soaPY = new float[initialCapacity];
            _soaVX = new float[initialCapacity];
            _soaVY = new float[initialCapacity];
            _soaWeight = new float[initialCapacity];
            _soaDensity = new float[initialCapacity];
            _soaActive = new int[initialCapacity];
            _soaNeighborCount = new int[initialCapacity];
            Console.WriteLine("[FluidSystem] Native C++ solver available — using accelerated path.");
        }
    }

    // ── Public API ──

    public void Emit(Vec2 position, Vec2 velocity)
    {
        if (_freeSlots.Count == 0)
        {
            // Grow if under budget, otherwise drop the particle
            if (ParticleBudget > 0 && _capacity >= ParticleBudget) return;
            Grow();
        }

        int i = _freeSlots.Pop();
        _particles[i] = new FluidParticle
        {
            Position = position,
            Velocity = velocity,
            Weight = 1f,
            Active = true
        };
        _activeCount++;
    }

    public void Emit(Vec2 position, Vec2 velocity, int count, float spread)
    {
        for (int i = 0; i < count; i++)
        {
            var offset = new Vec2(
                ((float)_rng.NextDouble() - 0.5f) * spread * 2f,
                ((float)_rng.NextDouble() - 0.5f) * spread * 2f);
            var jitter = new Vec2(
                ((float)_rng.NextDouble() - 0.5f) * 20f,
                ((float)_rng.NextDouble() - 0.5f) * 20f);
            Emit(position + offset, velocity + jitter);
        }
    }

    public void ClearParticles()
    {
        for (int i = 0; i < _capacity; i++)
            _particles[i].Active = false;
        _activeCount = 0;
        _freeSlots.Clear();
        for (int i = _capacity - 1; i >= 0; i--)
            _freeSlots.Push(i);
    }

    private void Grow()
    {
        int newCapacity = _capacity * 2;
        if (ParticleBudget > 0 && newCapacity > ParticleBudget)
            newCapacity = ParticleBudget;
        Array.Resize(ref _particles, newCapacity);
        Array.Resize(ref _neighborData, newCapacity * MaxNeighborsPerParticle);
        Array.Resize(ref _neighborCounts, newCapacity);
        Array.Resize(ref _activeIndices, newCapacity);
        Array.Resize(ref _predicted, newCapacity);
        Array.Resize(ref _lambda, newCapacity);
        Array.Resize(ref _deltaPos, newCapacity);
        Array.Resize(ref _divergence, newCapacity);
        Array.Resize(ref _vorticity, newCapacity);
        for (int i = newCapacity - 1; i >= _capacity; i--)
            _freeSlots.Push(i);
        _capacity = newCapacity;

        // Resize native SOA arrays if native solver is active
        if (_nativeAvailable && _nativeSolver != IntPtr.Zero)
        {
            NativeFluidSolver.fluid_destroy(_nativeSolver);
            _nativeSolver = NativeFluidSolver.fluid_create(newCapacity, SmoothingRadius);
            Array.Resize(ref _soaPX, newCapacity);
            Array.Resize(ref _soaPY, newCapacity);
            Array.Resize(ref _soaVX, newCapacity);
            Array.Resize(ref _soaVY, newCapacity);
            Array.Resize(ref _soaWeight, newCapacity);
            Array.Resize(ref _soaDensity, newCapacity);
            Array.Resize(ref _soaActive, newCapacity);
            Array.Resize(ref _soaNeighborCount, newCapacity);
        }
    }

    // ── Entity Lifecycle ──

    public override void Update(float dt)
    {
        base.Update(dt);
        if (dt <= 0) return;

        // Guard against zero rest density (causes NaN in pressure solver)
        if (RestDensity <= 0) RestDensity = 1f;

        UpdatePouring(dt);
        if (_activeCount == 0) return;

        if (UseWCSPH)
        {
            // ── WCSPH solver (reference implementation) ──
            BuildActiveList();
            HashParticles();
            BuildNeighborCache();
            WCSPH_ComputeDensityPressure();
            WCSPH_ComputeForces();
            WCSPH_Integrate(dt);
            ResolveBoundaries();
            InteractWithBodies();

            MergeNearby();
            SplitIfDisturbed();
            EmergencyMerge();
            return;
        }

        // ── Adaptive quality: scale work to particle count ──
        int count = _activeCount;
        int steps = count > 1000 ? 1
                  : count > 600  ? System.Math.Min(SubSteps, 2)
                  : System.Math.Max(1, SubSteps);
        int iters = count > 1000 ? 1 : SolverIterations;
        int divIters = count > 800 ? 0 : DivergenceIterations;
        bool doVorticity = VorticityStrength > 0 && count < 800;
        bool doViscosity = count < 1200;

        float subDt = dt / steps;

        bool useNative = UsingNativeSolver;

        if (useNative)
        {
            NativeSubsteps(steps, subDt, iters, divIters, doViscosity, doVorticity);
            for (int step = 0; step < steps; step++)
            {
                InteractWithBodies();
                ApplyBuoyancy(subDt);
            }
        }
        else
        {
            for (int step = 0; step < steps; step++)
            {
                BuildActiveList();
                PredictPositions(subDt);

                if (step == 0)
                {
                    HashParticles();
                    BuildNeighborCache();
                }

                for (int iter = 0; iter < System.Math.Max(1, iters); iter++)
                {
                    ComputeLambda();
                    ComputePositionDelta();
                    ApplyPositionDelta();
                    EnforceBodiesOnPredicted();
                }
                UpdateVelocities(subDt);
                for (int iter = 0; iter < divIters; iter++)
                    CorrectDivergence(subDt);
                if (doViscosity) ApplyViscosity();
                if (doVorticity)
                    ApplyVorticityConfinement(subDt);
                CommitPositions();

                ResolveBoundaries();
                InteractWithBodies();
                ApplyBuoyancy(subDt);
            }
        }

        // Merge/split once per frame (macro-level, not per sub-step)
        MergeNearby();
        SplitIfDisturbed();
        EmergencyMerge();
    }

    /// <summary>
    /// Transfer particle data to native solver, run substeps, transfer back.
    /// </summary>
    private void NativeSubsteps(int steps, float subDt, int iters, int divIters, bool doViscosity, bool doVorticity)
    {
        // AOS → SOA conversion
        int n = _capacity;
        for (int i = 0; i < n; i++)
        {
            _soaPX![i] = _particles[i].Position.X;
            _soaPY![i] = _particles[i].Position.Y;
            _soaVX![i] = _particles[i].Velocity.X;
            _soaVY![i] = _particles[i].Velocity.Y;
            _soaWeight![i] = _particles[i].Weight;
            _soaActive![i] = _particles[i].Active ? 1 : 0;
        }

        // Upload + configure
        NativeFluidSolver.fluid_upload_particles(_nativeSolver, n, _soaPX!, _soaPY!, _soaVX!, _soaVY!, _soaWeight!, _soaActive!);
        NativeFluidSolver.fluid_set_params(_nativeSolver,
            ParticleMass, RestDensity, Relaxation,
            SurfaceTensionK, XSPHViscosity,
            Gravity.X, Gravity.Y, MaxSpeed, VorticityStrength);
        NativeFluidSolver.fluid_set_bounds(_nativeSolver,
            BoundsLeft, BoundsTop, BoundsRight, BoundsBottom, BoundaryDamping);

        // Run substeps in native code
        for (int step = 0; step < steps; step++)
        {
            NativeFluidSolver.fluid_substep(_nativeSolver, subDt, iters, divIters,
                doViscosity ? 1 : 0, doVorticity ? 1 : 0);
        }

        // Download results
        NativeFluidSolver.fluid_download_particles(_nativeSolver,
            _soaPX!, _soaPY!, _soaVX!, _soaVY!, _soaDensity!, _soaNeighborCount!);

        // SOA → AOS conversion
        for (int i = 0; i < n; i++)
        {
            _particles[i].Position.X = _soaPX![i];
            _particles[i].Position.Y = _soaPY![i];
            _particles[i].Velocity.X = _soaVX![i];
            _particles[i].Velocity.Y = _soaVY![i];
            _particles[i].Density = _soaDensity![i];
            _particles[i].NeighborCount = _soaNeighborCount![i];
        }

        // Rebuild active list for managed code that follows (body interaction etc.)
        BuildActiveList();
    }

    public override void Draw()
    {
        _renderer.Draw(_particles, _capacity);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        _renderer.Dispose();
        if (_nativeSolver != IntPtr.Zero)
        {
            NativeFluidSolver.fluid_destroy(_nativeSolver);
            _nativeSolver = IntPtr.Zero;
        }
    }

    // ── Pouring ──

    private void UpdatePouring(float dt)
    {
        if (!Pouring) return;
        _pourAccumulator += PourRate * dt;
        while (_pourAccumulator >= 1f)
        {
            var offset = new Vec2(
                ((float)_rng.NextDouble() - 0.5f) * PourSpread,
                ((float)_rng.NextDouble() - 0.5f) * PourSpread * 0.5f);
            var jitter = new Vec2(
                ((float)_rng.NextDouble() - 0.5f) * 10f,
                ((float)_rng.NextDouble() - 0.5f) * 10f);
            Emit(PourPosition + offset, PourVelocity + jitter);
            _pourAccumulator -= 1f;
        }
    }

    // ── Active Index List ──

    private void BuildActiveList()
    {
        _activeIndexCount = 0;
        for (int i = 0; i < _capacity; i++)
        {
            if (_particles[i].Active)
                _activeIndices[_activeIndexCount++] = i;
        }
    }

    // ══════════════════════════════════════════════════════════
    // ── PBF Core: Predict → Constrain → Update Velocities ──
    // ══════════════════════════════════════════════════════════

    private void PredictPositions(float dt)
    {
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            _particles[i].Velocity += Gravity * dt;
            _predicted[i] = _particles[i].Position + _particles[i].Velocity * dt;
        }
    }

    private void HashParticles()
    {
        _hash.Clear();
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            _hash.Insert(i, _predicted[i].X, _predicted[i].Y);
        }
    }

    private void BuildNeighborCache()
    {
        float h2 = FluidKernels.H2;
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            _tempNeighbors.Clear();
            _hash.QueryNeighbors(_predicted[i].X, _predicted[i].Y, _tempNeighbors);

            int offset = i * MaxNeighborsPerParticle;
            int count = 0;
            for (int n = 0; n < _tempNeighbors.Count && count < MaxNeighborsPerParticle; n++)
            {
                int j = _tempNeighbors[n];
                if (!_particles[j].Active) continue;
                var diff = _predicted[i] - _predicted[j];
                if (diff.LengthSquared() < h2)
                    _neighborData[offset + count++] = j;
            }
            _neighborCounts[i] = count;
        }
    }

    /// <summary>Compute density constraint violation and per-particle lambda.</summary>
    private void ComputeLambda()
    {
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];

            // Density at predicted position
            float density = 0;
            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                var diff = _predicted[i] - _predicted[j];
                density += ParticleMass * MathF.Sqrt(_particles[j].Weight) * FluidKernels.Poly6(diff.LengthSquared());
            }

            _particles[i].Density = MathF.Max(density, 1e-6f);
            _particles[i].NeighborCount = nCount;

            // Constraint: Ci = density/restDensity - 1
            // Only resist compression (Ci > 0). Don't attract under-dense particles —
            // that causes self-splashing oscillation. Surface tension (scorr) handles cohesion.
            float constraint = MathF.Max(0, density / RestDensity - 1f);

            // Sum of squared gradients for denominator
            float gradSumSq = 0;
            var gradI = Vec2.Zero;

            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (j == i) continue;

                var diff = _predicted[i] - _predicted[j];
                float r2 = diff.LengthSquared();
                if (r2 < 1e-6f) continue;
                float r = MathF.Sqrt(r2);
                var rDir = diff / r;
                float sw = MathF.Sqrt(_particles[j].Weight);

                var gradJ = FluidKernels.SpikyGradient(rDir, r) * (-sw / RestDensity);
                gradSumSq += gradJ.LengthSquared();
                gradI -= gradJ;
            }

            gradSumSq += gradI.LengthSquared();
            _lambda[i] = -constraint / (gradSumSq + Relaxation);
        }
    }

    /// <summary>Compute position correction from lambda values + surface tension.</summary>
    private void ComputePositionDelta()
    {
        // Reference kernel value for surface tension correction (scorr)
        float dq = 0.3f * FluidKernels.H;
        float wDq = FluidKernels.Poly6(dq * dq);
        float wDqSafe = wDq > 1e-12f ? wDq : 1f;

        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];

            var delta = Vec2.Zero;

            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (j == i) continue;

                var diff = _predicted[i] - _predicted[j];
                float r2 = diff.LengthSquared();
                if (r2 < 1e-6f) continue;
                float r = MathF.Sqrt(r2);
                var rDir = diff / r;

                // Surface tension correction (scorr = -k * (W/W(Δq))^4)
                float wij = FluidKernels.Poly6(r2);
                float ratio = wij / wDqSafe;
                float scorr = -SurfaceTensionK * ratio * ratio * ratio * ratio;

                float sw = MathF.Sqrt(_particles[j].Weight);
                delta += FluidKernels.SpikyGradient(rDir, r) * ((_lambda[i] + _lambda[j] + scorr) * sw);
            }

            _deltaPos[i] = delta / RestDensity;
        }
    }

    private void ApplyPositionDelta()
    {
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            _predicted[i] += _deltaPos[i];
        }
    }

    /// <summary>Derive velocity from position change (core PBF velocity update).</summary>
    private void UpdateVelocities(float dt)
    {
        float invDt = 1f / dt;
        float maxSpeedSq = MaxSpeed * MaxSpeed;

        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            _particles[i].Velocity = (_predicted[i] - _particles[i].Position) * invDt;

            float speedSq = _particles[i].Velocity.LengthSquared();
            if (speedSq > maxSpeedSq)
                _particles[i].Velocity *= MaxSpeed / MathF.Sqrt(speedSq);
        }
    }

    /// <summary>XSPH viscosity — smooths the velocity field for cohesive motion.</summary>
    private void ApplyViscosity()
    {
        if (XSPHViscosity <= 0) return;

        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];

            var correction = Vec2.Zero;
            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (j == i) continue;

                var diff = _predicted[i] - _predicted[j];
                float w = FluidKernels.Poly6(diff.LengthSquared());
                float rhoj = MathF.Max(_particles[j].Density, 1e-4f);
                correction += (_particles[j].Velocity - _particles[i].Velocity) * (w / rhoj);
            }

            _particles[i].Velocity += correction * XSPHViscosity;
        }
    }

    /// <summary>
    /// Lightweight pairwise repulsion for when SolverIterations=0.
    /// Much weaker than PBF — allows particles to stack under gravity while preventing
    /// total overlap. Produces a "settling sand" behavior that fills containers.
    /// </summary>
    private void ApplySoftRepulsion()
    {
        float minDist = SmoothingRadius * 0.8f; // particles repel below this distance
        float minDist2 = minDist * minDist;
        float strength = 0.8f; // how hard to push (0-1, lower = softer)

        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];

            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (j <= i) continue; // each pair once

                float dx = _predicted[i].X - _predicted[j].X;
                float dy = _predicted[i].Y - _predicted[j].Y;
                float d2 = dx * dx + dy * dy;

                if (d2 < minDist2 && d2 > 1e-6f)
                {
                    float d = MathF.Sqrt(d2);
                    float overlap = minDist - d;
                    float push = overlap * strength * 0.5f;
                    float nx = dx / d, ny = dy / d;

                    _predicted[i].X += nx * push;
                    _predicted[i].Y += ny * push;
                    _predicted[j].X -= nx * push;
                    _predicted[j].Y -= ny * push;
                }
            }
        }
    }

    /// <summary>Clamp predicted positions to bounds during soft repulsion iterations.</summary>
    private void EnforcePredictedBounds()
    {
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            if (_predicted[i].X < BoundsLeft) _predicted[i].X = BoundsLeft;
            if (_predicted[i].X > BoundsRight) _predicted[i].X = BoundsRight;
            if (_predicted[i].Y < BoundsTop) _predicted[i].Y = BoundsTop;
            if (_predicted[i].Y > BoundsBottom) _predicted[i].Y = BoundsBottom;
        }
    }

    // ══════════════════════════════════════════════════════════
    // ── WCSPH Solver (ported from reference implementation) ──
    // ══════════════════════════════════════════════════════════

    private void WCSPH_ComputeDensityPressure()
    {
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];

            float density = 0;
            var centerSum = Vec2.Zero;
            float weightSum = 0;

            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                var diff = _particles[i].Position - _particles[j].Position;
                float w = FluidKernels.Poly6(diff.LengthSquared());
                density += ParticleMass * w;
                centerSum += _particles[j].Position * w;
                weightSum += w;
            }

            _particles[i].Density = MathF.Max(density, 0.0001f);
            _particles[i].Pressure = GasConstant * (density - RestDensity);
            _particles[i].NeighborCount = nCount;
            _particles[i].NearCenter = weightSum > 0 ? centerSum / weightSum : _particles[i].Position;
        }
    }

    private void WCSPH_ComputeForces()
    {
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];

            var fPressure = Vec2.Zero;
            var fViscosity = Vec2.Zero;
            float pi_ = _particles[i].Pressure;
            float rhoi = _particles[i].Density;

            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (i == j) continue;

                var diff = _particles[i].Position - _particles[j].Position;
                float r2 = diff.LengthSquared();
                if (r2 < 1e-6f) continue;

                float r = MathF.Sqrt(r2);
                var rDir = diff / r;
                float rhoj = _particles[j].Density;

                float pressureTerm = (pi_ + _particles[j].Pressure) / (2f * rhoj);
                fPressure += FluidKernels.SpikyGradient(rDir, r) * (-ParticleMass * pressureTerm);

                var velDiff = _particles[j].Velocity - _particles[i].Velocity;
                fViscosity += velDiff * (WCSPHViscosity * ParticleMass * FluidKernels.ViscosityLaplacian(r) / rhoj);
            }

            // Surface tension: pull toward weighted center of neighbors
            var fSurface = Vec2.Zero;
            if (_particles[i].NeighborCount > 1)
                fSurface = (_particles[i].NearCenter - _particles[i].Position) * WCSPHSurfaceTension;

            var fGravity = Gravity * rhoi;

            _particles[i].Force = fPressure + fViscosity + fSurface + fGravity;
        }
    }

    private void WCSPH_Integrate(float dt)
    {
        float maxSpeedSq = MaxSpeed * MaxSpeed;

        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            var accel = _particles[i].Force / _particles[i].Density;
            _particles[i].Velocity += accel * dt;
            _particles[i].Velocity *= Damping;

            float speedSq = _particles[i].Velocity.LengthSquared();
            if (speedSq > maxSpeedSq)
                _particles[i].Velocity *= MaxSpeed / MathF.Sqrt(speedSq);

            _particles[i].Position += _particles[i].Velocity * dt;
            _particles[i].Force = Vec2.Zero;
        }
    }

    /// <summary>DFSPH divergence correction — reduces velocity compression for smooth merging.</summary>
    private void CorrectDivergence(float dt)
    {
        if (dt <= 0) return;

        // Pass 1: compute divergence and alpha (denominator) per particle
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];

            float div = 0;
            float alpha = 0;
            float rhoi = MathF.Max(_particles[i].Density, 1e-4f);

            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (j == i) continue;

                var diff = _predicted[i] - _predicted[j];
                float r2 = diff.LengthSquared();
                if (r2 < 1e-6f) continue;
                float r = MathF.Sqrt(r2);
                var rDir = diff / r;

                var grad = FluidKernels.SpikyGradient(rDir, r);
                float rhoj = MathF.Max(_particles[j].Density, 1e-4f);
                float sw = MathF.Sqrt(_particles[j].Weight);

                // div = ρ_i * Σ (m/ρ_j) * (v_i - v_j) · ∇W
                div += (ParticleMass * sw / rhoj) *
                       Vec2.Dot(_particles[i].Velocity - _particles[j].Velocity, grad);

                // α = Σ m * |∇W|² / ρ_j²
                alpha += ParticleMass * sw * grad.LengthSquared() / (rhoj * rhoj);
            }

            _divergence[i] = div * rhoi;
            // κ = div / (dt * α)
            _lambda[i] = alpha > 1e-6f ? _divergence[i] / (dt * alpha + Relaxation) : 0;
        }

        // Pass 2: correct velocities
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];

            float rhoi = MathF.Max(_particles[i].Density, 1e-4f);
            var correction = Vec2.Zero;

            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (j == i) continue;

                var diff = _predicted[i] - _predicted[j];
                float r2 = diff.LengthSquared();
                if (r2 < 1e-6f) continue;
                float r = MathF.Sqrt(r2);
                var rDir = diff / r;

                float rhoj = MathF.Max(_particles[j].Density, 1e-4f);
                float sw = MathF.Sqrt(_particles[j].Weight);

                correction += FluidKernels.SpikyGradient(rDir, r) *
                    (ParticleMass * sw * (_lambda[i] / rhoi + _lambda[j] / rhoj));
            }

            _particles[i].Velocity -= correction * dt;
        }
    }

    /// <summary>Vorticity confinement — amplifies existing rotation for natural turbulence.</summary>
    private void ApplyVorticityConfinement(float dt)
    {
        if (VorticityStrength <= 0) return;

        // Pass 1: compute 2D curl (scalar vorticity) per particle
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];

            float curl = 0;
            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (j == i) continue;

                var diff = _predicted[i] - _predicted[j];
                float r2 = diff.LengthSquared();
                if (r2 < 1e-6f) continue;
                float r = MathF.Sqrt(r2);
                var rDir = diff / r;

                float rhoj = MathF.Max(_particles[j].Density, 1e-4f);
                var grad = FluidKernels.SpikyGradient(rDir, r);
                var vDiff = _particles[j].Velocity - _particles[i].Velocity;

                // 2D cross product: (vx, vy) × (gx, gy) = vx*gy - vy*gx
                curl += (ParticleMass / rhoj) * (vDiff.X * grad.Y - vDiff.Y * grad.X);
            }

            _vorticity[i] = curl;
        }

        // Pass 2: compute gradient of |ω| and apply confinement force
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];

            var eta = Vec2.Zero; // gradient of |ω|
            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (j == i) continue;

                var diff = _predicted[i] - _predicted[j];
                float r2 = diff.LengthSquared();
                if (r2 < 1e-6f) continue;
                float r = MathF.Sqrt(r2);
                var rDir = diff / r;

                float rhoj = MathF.Max(_particles[j].Density, 1e-4f);
                var grad = FluidKernels.SpikyGradient(rDir, r);

                // η = Σ (m/ρ_j) * |ω_j| * ∇W
                eta += grad * (ParticleMass / rhoj * MathF.Abs(_vorticity[j]));
            }

            float etaLen = eta.Length();
            if (etaLen < 1e-4f) continue;

            // Normalize: N = η / |η|
            var N = eta / etaLen;

            // Force: f = ε * (N × ω)  →  in 2D: (N.y * ω, -N.x * ω)
            float w = _vorticity[i];
            var force = new Vec2(N.Y * w, -N.X * w) * VorticityStrength;

            _particles[i].Velocity += force * dt;
        }
    }

    private void CommitPositions()
    {
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            _particles[i].Position = _predicted[i];
        }
    }

    // ══════════════════════════════════════════════════════
    // ── Boundaries, Body Interaction, Merge, Split ──
    // ══════════════════════════════════════════════════════

    private void ResolveBoundaries()
    {
        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];

            if (_particles[i].Position.X < BoundsLeft)
            { _particles[i].Position.X = BoundsLeft; _particles[i].Velocity.X = MathF.Abs(_particles[i].Velocity.X) * BoundaryDamping; }
            if (_particles[i].Position.X > BoundsRight)
            { _particles[i].Position.X = BoundsRight; _particles[i].Velocity.X = -MathF.Abs(_particles[i].Velocity.X) * BoundaryDamping; }
            if (_particles[i].Position.Y < BoundsTop)
            { _particles[i].Position.Y = BoundsTop; _particles[i].Velocity.Y = MathF.Abs(_particles[i].Velocity.Y) * BoundaryDamping; }
            if (_particles[i].Position.Y > BoundsBottom)
            { _particles[i].Position.Y = BoundsBottom; _particles[i].Velocity.Y = -MathF.Abs(_particles[i].Velocity.Y) * BoundaryDamping; }
        }
    }

    private void MergeNearby()
    {
        if (MergeInterval <= 0) return;

        // Only start merging when particle count is above 70% of budget (let the pool fill first)
        int mergeFloor = ParticleBudget > 0 ? (int)(ParticleBudget * 0.5f) : 400;
        if (_activeCount < mergeFloor) return;

        int interval = _activeCount > 2000 ? System.Math.Max(5, MergeInterval / 3)
                     : _activeCount > 1000 ? System.Math.Max(8, MergeInterval / 2)
                     : MergeInterval;

        if (++_mergeCounter < interval) return;
        _mergeCounter = 0;

        float mergeRadiusSq = SmoothingRadius * SmoothingRadius * 0.25f;
        float baseSpeedSq = MergeSpeedThreshold * MergeSpeedThreshold;
        int budget = _activeCount > 2000 ? System.Math.Clamp(_activeCount / 8, 10, 100)
                   : System.Math.Clamp(_activeCount / 15, 3, 40);
        int merged = 0;

        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            if (!_particles[i].Active) continue;
            if (_particles[i].NeighborCount < MergeMinNeighbors) continue;

            float wi = _particles[i].Weight;
            float iSpeedLimit = baseSpeedSq * (1f + MathF.Sqrt(wi) * 0.3f);
            if (_particles[i].Velocity.LengthSquared() > iSpeedLimit) continue;

            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];
            float bestDistSq = mergeRadiusSq * (1f + MathF.Sqrt(wi) * 0.15f);
            int bestJ = -1;

            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (j == i || !_particles[j].Active) continue;
                if (_particles[j].NeighborCount < MergeMinNeighbors) continue;
                float nw = _particles[j].Weight;
                if (_particles[j].Velocity.LengthSquared() > baseSpeedSq * (1f + MathF.Sqrt(nw) * 0.3f)) continue;

                float dSq = (_particles[i].Position - _particles[j].Position).LengthSquared();
                if (dSq < bestDistSq) { bestDistSq = dSq; bestJ = j; }
            }

            if (bestJ < 0) continue;

            float mergeWj = _particles[bestJ].Weight;
            float total = wi + mergeWj;
            _particles[i].Position = (_particles[i].Position * wi + _particles[bestJ].Position * mergeWj) / total;
            _particles[i].Velocity = (_particles[i].Velocity * wi + _particles[bestJ].Velocity * mergeWj) / total;
            _particles[i].Weight = total;

            _particles[bestJ].Active = false;
            _freeSlots.Push(bestJ);
            _activeCount--;
            if (++merged >= budget) break;
        }
    }

    private void SplitIfDisturbed()
    {
        float splitSpeedSq = SplitSpeedThreshold * SplitSpeedThreshold;
        int splitBudget = 15; // max splits per frame — prevents cascade explosions
        int splits = 0;

        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            if (!_particles[i].Active) continue;
            if (_particles[i].Weight <= 1.01f) continue;

            bool shouldSplit = false;
            if (_particles[i].Velocity.LengthSquared() > splitSpeedSq)
                shouldSplit = true;
            if (!shouldSplit && _particles[i].Weight > 2f
                && _particles[i].NeighborCount < SplitSurfaceNeighbors)
                shouldSplit = true;
            if (!shouldSplit) continue;

            float halfWeight = _particles[i].Weight * 0.5f;
            if (halfWeight < 0.5f) continue;

            _particles[i].Weight = halfWeight;

            var vel = _particles[i].Velocity;
            Vec2 perp;
            float vLen = vel.Length();
            if (vLen > 1f) perp = new Vec2(-vel.Y / vLen, vel.X / vLen);
            else perp = new Vec2(((float)_rng.NextDouble() - 0.5f) * 2f,
                                 ((float)_rng.NextDouble() - 0.5f) * 2f).Normalized();

            if (_freeSlots.Count == 0) Grow();
            int j = _freeSlots.Pop();
            _particles[j] = new FluidParticle
            {
                Position = _particles[i].Position + perp * (SmoothingRadius * 0.25f),
                Velocity = _particles[i].Velocity,
                Weight = halfWeight,
                Active = true
            };
            _activeCount++;
            if (++splits >= splitBudget) break;
        }
    }

    /// <summary>Hard budget enforcement — force-merge closest pairs ignoring speed until under budget.</summary>
    private void EmergencyMerge()
    {
        if (ParticleBudget <= 0 || _activeCount <= ParticleBudget) return;

        int excess = _activeCount - ParticleBudget;
        float radiusSq = SmoothingRadius * SmoothingRadius;
        int merged = 0;

        for (int a = 0; a < _activeIndexCount && merged < excess; a++)
        {
            int i = _activeIndices[a];
            if (!_particles[i].Active) continue;

            int offset = i * MaxNeighborsPerParticle;
            int nCount = _neighborCounts[i];
            float bestSq = radiusSq;
            int bestJ = -1;

            for (int n = 0; n < nCount; n++)
            {
                int j = _neighborData[offset + n];
                if (j == i || !_particles[j].Active) continue;
                float dSq = (_particles[i].Position - _particles[j].Position).LengthSquared();
                if (dSq < bestSq) { bestSq = dSq; bestJ = j; }
            }

            if (bestJ < 0) continue;

            float wi = _particles[i].Weight, wj = _particles[bestJ].Weight;
            float total = wi + wj;
            _particles[i].Position = (_particles[i].Position * wi + _particles[bestJ].Position * wj) / total;
            _particles[i].Velocity = (_particles[i].Velocity * wi + _particles[bestJ].Velocity * wj) / total;
            _particles[i].Weight = total;
            _particles[bestJ].Active = false;
            _freeSlots.Push(bestJ);
            _activeCount--;
            merged++;
        }
    }

    /// <summary>Push predicted positions out of rigid bodies. Called inside solver loop
    /// so the constraint solver can never place particles inside walls.</summary>
    private void EnforceBodiesOnPredicted()
    {
        var physics = Eng.Physics;
        if (physics.BodyCount == 0) return;

        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            var pPos = _predicted[i];

            for (int b = 0; b < physics.Bodies.Count; b++)
            {
                var body = physics.Bodies[b];
                if (body.Shape == null) continue;

                if (pPos.X < body.AABB.Min.X - 2 || pPos.X > body.AABB.Max.X + 2) continue;
                if (pPos.Y < body.AABB.Min.Y - 2 || pPos.Y > body.AABB.Max.Y + 2) continue;
                if (!body.Shape.TestPoint(body.Position, body.Angle, pPos)) continue;

                if (body.Shape is CircleShape circle)
                {
                    var center = body.Position + circle.Center.Rotate(body.Angle);
                    var dir = pPos - center;
                    float dist = dir.Length();
                    if (dist < 0.01f) dir = new Vec2(0, -1); else dir /= dist;
                    _predicted[i] = center + dir * (circle.Radius + 1.5f);
                }
                else if (body.Shape is PolygonShape poly)
                {
                    float maxSep = float.MinValue;
                    int bestEdge = 0;
                    for (int e = 0; e < poly.Count; e++)
                    {
                        var wn = poly.GetWorldNormal(e, body.Angle);
                        var wv = poly.GetWorldVertex(e, body.Position, body.Angle);
                        float sep = Vec2.Dot(wn, pPos - wv);
                        if (sep > maxSep) { maxSep = sep; bestEdge = e; }
                    }
                    var pushDir = poly.GetWorldNormal(bestEdge, body.Angle);
                    _predicted[i] += pushDir * (-maxSep + 2f);
                }

                pPos = _predicted[i]; // update for next body check
            }
        }
    }

    /// <summary>Estimate submersion of dynamic bodies from nearby particles and apply buoyancy + drag.
    /// Matches WaterBody's force model: buoyancy at estimated centroid, exponential velocity damping,
    /// flow-relative drag, and strong surface damping to prevent bobbing.</summary>
    private void ApplyBuoyancy(float dt)
    {
        var physics = Eng.Physics;
        if (physics.BodyCount == 0 || _activeCount == 0) return;

        var gravity = physics.Settings.Gravity;
        float gravMag = gravity.Length();
        if (gravMag < 0.01f) return;
        var upDir = -(gravity / gravMag);

        float senseRadius = SmoothingRadius * 2f;
        float senseRadiusSq = senseRadius * senseRadius;

        for (int b = 0; b < physics.Bodies.Count; b++)
        {
            var body = physics.Bodies[b];
            if (body.Type != BodyType.Dynamic || body.Shape == null) continue;

            // Compute body area
            float bodyArea;
            if (body.Shape is CircleShape circ)
                bodyArea = MathF.PI * circ.Radius * circ.Radius;
            else if (body.Shape is PolygonShape poly)
            {
                float a = 0;
                for (int v = 0; v < poly.Count; v++)
                {
                    int next = (v + 1) % poly.Count;
                    a += Vec2.Cross(poly.Vertices[v], poly.Vertices[next]);
                }
                bodyArea = MathF.Abs(a * 0.5f);
            }
            else continue;

            // Gather nearby particles: estimate surface, buoyancy center, and local flow
            int nearbyCount = 0;
            float totalWeight = 0;
            float waterYSum = 0;
            Vec2 buoyancyCenterSum = Vec2.Zero;
            float buoyancyCenterWeight = 0;
            Vec2 fluidVelSum = Vec2.Zero;
            float fluidVelWeight = 0;
            float topYSum = 0;
            float topYWeight = 0;

            for (int a = 0; a < _activeIndexCount; a++)
            {
                int i = _activeIndices[a];
                var diff = _particles[i].Position - body.Position;
                float distSq = diff.LengthSquared();
                if (distSq > senseRadiusSq) continue;

                nearbyCount++;
                float w = _particles[i].Weight;
                totalWeight += w;
                waterYSum += _particles[i].Position.Y * w;

                // Surface estimation: weight higher particles more (lower Y = higher in screen)
                float heightWeight = MathF.Max(0, body.AABB.Max.Y - _particles[i].Position.Y);
                topYSum += _particles[i].Position.Y * (w * heightWeight);
                topYWeight += w * heightWeight;

                // Buoyancy center: weighted centroid of nearby fluid
                buoyancyCenterSum += _particles[i].Position * w;
                buoyancyCenterWeight += w;

                // Local fluid velocity (closer particles contribute more)
                float proximity = 1f - MathF.Sqrt(distSq) / senseRadius;
                fluidVelSum += _particles[i].Velocity * (w * proximity);
                fluidVelWeight += w * proximity;
            }

            if (nearbyCount < 3) continue;

            // Estimate water surface level (weighted toward top particles)
            float waterTopY = topYWeight > 0.01f
                ? topYSum / topYWeight
                : (waterYSum / totalWeight) - senseRadius * 0.5f;

            float bodyBottom = body.AABB.Max.Y;
            float bodyTop = body.AABB.Min.Y;
            float bodyHeight = MathF.Max(bodyBottom - bodyTop, 1f);

            float submergedFraction = System.Math.Clamp((bodyBottom - waterTopY) / bodyHeight, 0f, 1f);
            float densityFraction = System.Math.Clamp(totalWeight / 15f, 0f, 1f);
            submergedFraction *= densityFraction;

            if (submergedFraction <= 0.01f) continue;

            // Buoyancy center (weighted centroid of nearby particles)
            Vec2 buoyancyCenter = buoyancyCenterWeight > 0.01f
                ? buoyancyCenterSum / buoyancyCenterWeight
                : body.Position;

            // Buoyancy force at submerged centroid (creates realistic torque)
            float submergedArea = submergedFraction * bodyArea;
            var buoyancyForce = upDir * (FluidDensity * submergedArea * gravMag);
            body.ApplyForceAtPoint(buoyancyForce, buoyancyCenter);

            // Local fluid velocity for flow-relative drag
            Vec2 flowVelocity = fluidVelWeight > 0.01f ? fluidVelSum / fluidVelWeight : Vec2.Zero;

            // Exponential velocity damping relative to flow (matches WaterBody model)
            var relativeVel = body.LinearVelocity - flowVelocity;
            float dampPower = submergedFraction * dt * 60f;
            float linearDamp = MathF.Pow(VelocityDamping, dampPower);
            body.LinearVelocity = flowVelocity + relativeVel * linearDamp;
            body.AngularVelocity *= MathF.Pow(VelocityDamping * 2f, dampPower);

            // Force-based drag for gradual feel
            float speed = relativeVel.Length();
            if (speed > 0.5f)
                body.ApplyForce(relativeVel * (-LinearDrag * submergedFraction * speed));

            // Angular drag via torque
            body.ApplyTorque(-AngularDrag * submergedFraction * body.AngularVelocity);

            // Surface damping: strong energy loss at the water line (prevents bobbing)
            if (submergedFraction > 0.05f && submergedFraction < 0.95f)
            {
                body.LinearVelocity = new Vec2(
                    body.LinearVelocity.X * SurfaceDamping,
                    body.LinearVelocity.Y * (SurfaceDamping * SurfaceDamping));
                body.AngularVelocity *= SurfaceDamping;
            }
        }
    }

    private void InteractWithBodies()
    {
        var physics = Eng.Physics;
        if (physics.BodyCount == 0) return;

        // Ensure AABBs are fresh (fluid updates independently of physics world step)
        for (int b = 0; b < physics.Bodies.Count; b++)
            physics.Bodies[b].UpdateAABB();

        for (int a = 0; a < _activeIndexCount; a++)
        {
            int i = _activeIndices[a];
            var pPos = _particles[i].Position;

            for (int b = 0; b < physics.Bodies.Count; b++)
            {
                var body = physics.Bodies[b];
                if (body.Shape == null) continue;

                if (pPos.X < body.AABB.Min.X - 2 || pPos.X > body.AABB.Max.X + 2) continue;
                if (pPos.Y < body.AABB.Min.Y - 2 || pPos.Y > body.AABB.Max.Y + 2) continue;
                if (!body.Shape.TestPoint(body.Position, body.Angle, pPos)) continue;

                Vec2 pushDir;
                if (body.Shape is CircleShape circle)
                {
                    var center = body.Position + circle.Center.Rotate(body.Angle);
                    pushDir = pPos - center;
                    float dist = pushDir.Length();
                    if (dist < 0.01f) pushDir = new Vec2(0, -1);
                    else pushDir /= dist;
                    _particles[i].Position = center + pushDir * (circle.Radius + 1f);
                }
                else if (body.Shape is PolygonShape poly)
                {
                    float maxSep = float.MinValue;
                    int bestEdge = 0;
                    for (int e = 0; e < poly.Count; e++)
                    {
                        var wn = poly.GetWorldNormal(e, body.Angle);
                        var wv = poly.GetWorldVertex(e, body.Position, body.Angle);
                        float sep = Vec2.Dot(wn, pPos - wv);
                        if (sep > maxSep) { maxSep = sep; bestEdge = e; }
                    }
                    pushDir = poly.GetWorldNormal(bestEdge, body.Angle);
                    _particles[i].Position += pushDir * (-maxSep + 1.5f);
                }
                else continue;

                float vn = Vec2.Dot(_particles[i].Velocity, pushDir);
                if (vn < 0)
                    _particles[i].Velocity -= pushDir * (1.5f * vn);

                if (body.Type == BodyType.Dynamic)
                    body.ApplyForceAtPoint(-pushDir * (_particles[i].Density * BodyForceScale), pPos);
            }
        }

    }
}
