using System.Collections.Generic;
using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Registry of all available track modules. Provides filtered selection
/// based on terrain type, difficulty, and distance constraints.
///
/// To add a new module: call RegisterModule() or add to BuildDefaultCatalog().
/// </summary>
public class ModuleCatalog
{
    private readonly List<TrackModule> _modules = new();

    /// <summary>All registered modules (read-only).</summary>
    public IReadOnlyList<TrackModule> All => _modules;

    /// <summary>Register a module into the catalog.</summary>
    public void RegisterModule(TrackModule module)
    {
        _modules.Add(module);
    }

    /// <summary>
    /// Returns modules that match the given constraints.
    /// </summary>
    /// <param name="terrainFilter">If set, only modules with this entry terrain (or transitions from it).</param>
    /// <param name="maxDifficulty">Maximum difficulty rating allowed.</param>
    /// <param name="currentDistance">Current run distance for MinDistance filtering.</param>
    /// <param name="allowJumps">If false, excludes modules with jumps.</param>
    /// <param name="requireTransition">If true, only returns transition modules.</param>
    public List<TrackModule> Query(
        TerrainType? terrainFilter = null,
        int maxDifficulty = 5,
        float currentDistance = 0f,
        bool allowJumps = true,
        bool requireTransition = false)
    {
        var results = new List<TrackModule>();

        foreach (var mod in _modules)
        {
            if (mod.Difficulty > maxDifficulty) continue;
            if (mod.MinDistance > currentDistance) continue;
            if (!allowJumps && mod.HasJump) continue;

            if (requireTransition && !mod.IsTransition) continue;
            if (!requireTransition && mod.IsTransition) continue;

            if (terrainFilter.HasValue)
            {
                // Module must start with the required terrain type
                if (mod.EntryTerrain != terrainFilter.Value) continue;
            }

            results.Add(mod);
        }

        return results;
    }

    /// <summary>
    /// Returns transition modules that go from one terrain type to another.
    /// </summary>
    public List<TrackModule> QueryTransitions(TerrainType from, TerrainType to)
    {
        var results = new List<TrackModule>();
        foreach (var mod in _modules)
        {
            if (mod.IsTransition && mod.EntryTerrain == from && mod.ExitTerrain == to)
                results.Add(mod);
        }
        return results;
    }

    /// <summary>
    /// Builds the default module catalog with a variety of pieces.
    /// Call this once at startup. Modules are defined as code-based Resources
    /// so they can also be authored as .tres files in the editor.
    /// </summary>
    public static ModuleCatalog BuildDefaultCatalog()
    {
        var catalog = new ModuleCatalog();

        // ── Snow descents ────────────────────────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 1500f, Drop = 600f,
            Difficulty = 1, Weight = 1.0f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 2200f, Drop = 900f,
            Difficulty = 2, Weight = 0.8f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 2800f, Drop = 1200f,
            Difficulty = 3, Weight = 0.7f, MinDistance = 3000f
        });

        // ── Dirt descents ────────────────────────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 1400f, Drop = 550f,
            Difficulty = 1, Weight = 1.0f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 2000f, Drop = 850f,
            Difficulty = 2, Weight = 0.8f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 2600f, Drop = 1100f,
            Difficulty = 3, Weight = 0.7f, MinDistance = 3000f
        });

        // ── Ice descents ─────────────────────────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = TerrainType.Ice, ExitTerrain = TerrainType.Ice,
            Length = 1600f, Drop = 700f,
            Difficulty = 2, Weight = 0.6f, MinDistance = 1000f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = TerrainType.Ice, ExitTerrain = TerrainType.Ice,
            Length = 2400f, Drop = 1000f,
            Difficulty = 4, Weight = 0.5f, MinDistance = 5000f
        });

        // ── Snow ramps (with jumps) ──────────────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 350f, Rise = 200f,
            Difficulty = 1, Weight = 1.2f,
            HasJump = true, GapWidth = 180f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 450f, Rise = 320f,
            Difficulty = 3, Weight = 1.0f, MinDistance = 4000f,
            HasJump = true, GapWidth = 300f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 500f, Rise = 400f,
            Difficulty = 5, Weight = 0.7f, MinDistance = 8000f,
            HasJump = true, GapWidth = 420f
        });

        // ── Dirt ramps (with jumps) ──────────────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 300f, Rise = 180f,
            Difficulty = 1, Weight = 1.2f,
            HasJump = true, GapWidth = 160f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 420f, Rise = 280f,
            Difficulty = 3, Weight = 1.0f, MinDistance = 4000f,
            HasJump = true, GapWidth = 260f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 480f, Rise = 380f,
            Difficulty = 5, Weight = 0.7f, MinDistance = 8000f,
            HasJump = true, GapWidth = 400f
        });

        // ── Ice ramps (with jumps, always hard) ──────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Ice, ExitTerrain = TerrainType.Ice,
            Length = 380f, Rise = 250f,
            Difficulty = 3, Weight = 0.8f, MinDistance = 2000f,
            HasJump = true, GapWidth = 280f
        });

        // ── Transition pieces: Snow → Dirt ───────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Transition,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Dirt,
            Length = 600f, Drop = 200f,
            Difficulty = 1, Weight = 1.5f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Transition,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Dirt,
            Length = 400f, Drop = 150f,
            Difficulty = 2, Weight = 1.0f, MinDistance = 2000f
        });
        // Transition with jump
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Dirt,
            Length = 400f, Rise = 200f,
            Difficulty = 2, Weight = 1.2f,
            HasJump = true, GapWidth = 200f
        });

        // ── Transition pieces: Dirt → Snow ───────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Transition,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Snow,
            Length = 600f, Drop = 200f,
            Difficulty = 1, Weight = 1.5f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Transition,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Snow,
            Length = 400f, Drop = 150f,
            Difficulty = 2, Weight = 1.0f, MinDistance = 2000f
        });
        // Transition with jump
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Snow,
            Length = 400f, Rise = 200f,
            Difficulty = 2, Weight = 1.2f,
            HasJump = true, GapWidth = 200f
        });

        // ── Transition pieces: Snow ↔ Ice ────────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Transition,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Ice,
            Length = 500f, Drop = 180f,
            Difficulty = 2, Weight = 0.8f, MinDistance = 1000f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Transition,
            EntryTerrain = TerrainType.Ice, ExitTerrain = TerrainType.Snow,
            Length = 500f, Drop = 180f,
            Difficulty = 2, Weight = 0.8f, MinDistance = 1000f
        });
        // Transition with jump
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Ice,
            Length = 400f, Rise = 220f,
            Difficulty = 3, Weight = 0.9f, MinDistance = 1000f,
            HasJump = true, GapWidth = 220f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Ice, ExitTerrain = TerrainType.Snow,
            Length = 400f, Rise = 220f,
            Difficulty = 3, Weight = 0.9f, MinDistance = 1000f,
            HasJump = true, GapWidth = 220f
        });

        // ── Transition pieces: Dirt ↔ Ice ────────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Transition,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Ice,
            Length = 500f, Drop = 180f,
            Difficulty = 3, Weight = 0.6f, MinDistance = 2000f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Transition,
            EntryTerrain = TerrainType.Ice, ExitTerrain = TerrainType.Dirt,
            Length = 500f, Drop = 180f,
            Difficulty = 3, Weight = 0.6f, MinDistance = 2000f
        });
        // Transition with jump
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Ice,
            Length = 400f, Rise = 220f,
            Difficulty = 3, Weight = 0.7f, MinDistance = 2000f,
            HasJump = true, GapWidth = 220f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Ice, ExitTerrain = TerrainType.Dirt,
            Length = 400f, Rise = 220f,
            Difficulty = 3, Weight = 0.7f, MinDistance = 2000f,
            HasJump = true, GapWidth = 220f
        });

        // ── Flat / breather pieces ───────────────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Flat,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 400f, Drop = 30f,
            Difficulty = 1, Weight = 0.8f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Flat,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 600f, Drop = 50f,
            Difficulty = 1, Weight = 0.7f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Flat,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 400f, Drop = 30f,
            Difficulty = 1, Weight = 0.8f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Flat,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 600f, Drop = 50f,
            Difficulty = 1, Weight = 0.7f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Flat,
            EntryTerrain = TerrainType.Ice, ExitTerrain = TerrainType.Ice,
            Length = 500f, Drop = 40f,
            Difficulty = 2, Weight = 0.6f, MinDistance = 1000f
        });

        // ── Snow ramps without jump (no-gap slope change) ────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 250f, Rise = 100f,
            Difficulty = 1, Weight = 0.9f,
            HasJump = false
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 350f, Rise = 150f,
            Difficulty = 2, Weight = 0.7f,
            HasJump = false
        });

        // ── Dirt ramps without jump ──────────────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 250f, Rise = 100f,
            Difficulty = 1, Weight = 0.9f,
            HasJump = false
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 350f, Rise = 150f,
            Difficulty = 2, Weight = 0.7f,
            HasJump = false
        });

        // ── Ice ramps without jump ───────────────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Ice, ExitTerrain = TerrainType.Ice,
            Length = 300f, Rise = 120f,
            Difficulty = 2, Weight = 0.6f, MinDistance = 1000f,
            HasJump = false
        });

        // ── Bump/roller pieces (up then down) ────────────────────
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Bump,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 500f, Rise = 120f, Drop = 100f,
            Difficulty = 1, Weight = 0.9f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Bump,
            EntryTerrain = TerrainType.Snow, ExitTerrain = TerrainType.Snow,
            Length = 700f, Rise = 180f, Drop = 150f,
            Difficulty = 2, Weight = 0.7f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Bump,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 500f, Rise = 120f, Drop = 100f,
            Difficulty = 1, Weight = 0.9f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Bump,
            EntryTerrain = TerrainType.Dirt, ExitTerrain = TerrainType.Dirt,
            Length = 700f, Rise = 180f, Drop = 150f,
            Difficulty = 2, Weight = 0.7f
        });
        catalog.RegisterModule(new TrackModule
        {
            Shape = TrackModule.ModuleShape.Bump,
            EntryTerrain = TerrainType.Ice, ExitTerrain = TerrainType.Ice,
            Length = 600f, Rise = 150f, Drop = 120f,
            Difficulty = 2, Weight = 0.7f, MinDistance = 1000f
        });

        GD.Print($"[ModuleCatalog] Built default catalog with {catalog._modules.Count} modules");
        return catalog;
    }
}
