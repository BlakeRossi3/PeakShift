using System;
using System.Collections.Generic;
using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// [Obsolete] Terrain transitions are now handled internally by CompoundModule sub-sections.
/// This catalog is retained for reference but is no longer used at runtime.
///
/// Previously: Registry of transition track modules for terrain type changes.
/// Regular terrain (descent, ramp, flat, bump) is now generated procedurally
/// by ProceduralModuleFactory. This catalog only holds transition modules.
/// </summary>
[Obsolete("Terrain transitions are now handled by CompoundModule sub-sections.")]
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
    /// Builds the default catalog with transition modules only.
    /// Regular terrain is generated procedurally by ProceduralModuleFactory.
    /// </summary>
    public static ModuleCatalog BuildDefaultCatalog()
    {
        var catalog = new ModuleCatalog();

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

        GD.Print($"[ModuleCatalog] Built transition catalog with {catalog._modules.Count} modules");
        return catalog;
    }
}
