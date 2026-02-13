using System.Collections.Generic;
using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Core track generation engine using procedural wave-based terrain generation.
///
/// Architecture:
///   1. ProceduralModuleFactory generates descent/ramp/flat/bump modules on the fly
///      with parameters derived from the guidance slope angle.
///   2. ModuleCatalog provides transition modules for terrain type changes.
///   3. DifficultyProfile controls guidance slope, variety chances, and scaling.
///   4. Wave-based generation: each wave = Descent → (optional variety) → Ramp+Gap.
///      Variety is injected via descent flavors, flat breathers, bump rollers,
///      double-descents, and ramp skips.
///   5. Dynamic gap sizing: gap width is computed from the preceding descent's
///      geometry, creating a Tiny Wings-style relationship.
///
/// The generator maintains a sequence of placed modules that the TerrainManager
/// can query for height, normals, gap info, and terrain type at any world X.
/// </summary>
public class ModuleTrackGenerator
{
    // ── Placed module instance ───────────────────────────────────

    /// <summary>
    /// A module that has been placed in the world with concrete positions.
    /// </summary>
    public class PlacedModule
    {
        public TrackModule Template { get; init; }
        public float WorldStartX { get; init; }
        public float WorldEndX { get; init; }
        public float EntryY { get; init; }
        public float ExitY { get; init; }
        public float Length { get; init; }
        public int SequenceIndex { get; init; }

        /// <summary>If this module has a jump, the gap starts here.</summary>
        public float GapStartX => WorldEndX;

        /// <summary>If this module has a jump, the gap ends here.</summary>
        public float GapEndX { get; init; }

        /// <summary>Actual gap width after difficulty scaling.</summary>
        public float ScaledGapWidth { get; init; }

        /// <summary>The terrain type the player experiences in this module.</summary>
        public TerrainType ActiveTerrainAt(float worldX)
        {
            float t = (worldX - WorldStartX) / Length;
            return Template.GetTerrainTypeAt(t);
        }

        /// <summary>Surface Y at a given world X within this module.</summary>
        public float HeightAt(float worldX)
        {
            float t = (worldX - WorldStartX) / Length;
            return Template.SampleHeight(t, EntryY);
        }
    }

    // ── Configuration ────────────────────────────────────────────

    private readonly ModuleCatalog _catalog;
    private readonly DifficultyProfile _difficulty;
    private readonly RandomNumberGenerator _rng = new();
    private readonly ProceduralModuleFactory _factory;

    /// <summary>How many modules ahead of the player to keep generated.</summary>
    public int LookaheadModules { get; set; } = 8;

    /// <summary>How far behind the player (px) before despawning modules.</summary>
    public float DespawnBehind { get; set; } = 2000f;

    // ── Intro run parameters ─────────────────────────────────────

    private const float IntroDescentLength = 3000f;
    private const float IntroDescentDrop = 7000f;
    private const float IntroRampLength = 1400f;
    private const float IntroRampRise = 300f;
    private const float IntroGapWidth = 650f;

    // ── Wave generation state ────────────────────────────────────

    private enum WavePhase { Descent, VarietyInjection, Ramp, PostGap }

    private readonly List<PlacedModule> _placed = new();
    private float _nextModuleX;
    private float _nextModuleY;
    private int _sequenceIndex;
    private int _sameTerrainCount;
    private TerrainType _currentTerrain = TerrainType.Snow;
    private int _modulesSinceLastJump;
    private float _totalDistance;

    // Wave phase tracking
    private WavePhase _currentPhase = WavePhase.Descent;
    private float _lastDescentDrop;
    private float _lastDescentLength;

    /// <summary>All currently placed modules (read-only).</summary>
    public IReadOnlyList<PlacedModule> PlacedModules => _placed;

    /// <summary>Current terrain type at the generation head.</summary>
    public TerrainType CurrentTerrain => _currentTerrain;

    /// <summary>Total distance generated so far.</summary>
    public float TotalDistance => _totalDistance;

    // ── Construction ─────────────────────────────────────────────

    public ModuleTrackGenerator(ModuleCatalog catalog, DifficultyProfile difficulty)
    {
        _catalog = catalog;
        _difficulty = difficulty;
        _rng.Randomize();
        _factory = new ProceduralModuleFactory(difficulty, _rng);
    }

    // ── Public API ───────────────────────────────────────────────

    /// <summary>
    /// Initialize the generator at the given start position.
    /// Places the intro descent + first ramp + gap, then fills lookahead.
    /// </summary>
    public void Initialize(float startX, float startY)
    {
        _placed.Clear();
        _nextModuleX = startX;
        _nextModuleY = startY;
        _sequenceIndex = 0;
        _sameTerrainCount = 0;
        _currentTerrain = TerrainType.Snow;
        _modulesSinceLastJump = 0;
        _totalDistance = 0f;
        _currentPhase = WavePhase.Descent;
        _lastDescentDrop = 0f;
        _lastDescentLength = 0f;

        // Place the intro sequence
        PlaceIntroSequence();

        // Fill lookahead
        while (_placed.Count < LookaheadModules)
            GenerateNextModule();
    }

    /// <summary>
    /// Call each frame. Generates ahead and trims behind the player.
    /// Returns true if any new modules were placed.
    /// </summary>
    public bool Update(float playerX)
    {
        bool changed = false;

        // Generate ahead
        while (CountModulesAheadOf(playerX) < LookaheadModules)
        {
            GenerateNextModule();
            changed = true;
        }

        // Trim behind
        changed |= TrimBehind(playerX);

        return changed;
    }

    /// <summary>
    /// Get the terrain surface Y at a given world X.
    /// </summary>
    public float GetHeight(float worldX)
    {
        var mod = FindModuleAt(worldX);
        if (mod != null)
            return mod.HeightAt(worldX);

        // Check if we're in a gap
        var gap = FindGapAt(worldX);
        if (gap != null)
            return gap.ExitY;  // lip height as reference

        // Extrapolate from last placed module
        if (_placed.Count > 0)
        {
            var last = _placed[^1];
            if (worldX > last.GapEndX)
                return last.ExitY;
        }

        return 200f;  // fallback
    }

    /// <summary>
    /// Get the terrain type at a given world X.
    /// </summary>
    public TerrainType GetTerrainTypeAt(float worldX)
    {
        var mod = FindModuleAt(worldX);
        if (mod != null)
            return mod.ActiveTerrainAt(worldX);

        return _currentTerrain;
    }

    /// <summary>
    /// Returns true if the given world X is inside a gap (no terrain below).
    /// </summary>
    public bool IsOverGap(float worldX)
    {
        return FindGapAt(worldX) != null;
    }

    /// <summary>
    /// Returns gap info for the current or next gap ahead of worldX.
    /// </summary>
    public GapInfo GetCurrentOrNextGap(float worldX)
    {
        for (int i = 0; i < _placed.Count; i++)
        {
            var mod = _placed[i];
            if (!mod.Template.HasJump) continue;

            float gapStart = mod.GapStartX;
            float gapEnd = mod.GapEndX;

            // Player is inside this gap
            if (worldX >= gapStart && worldX < gapEnd)
                return MakeGapInfo(mod, i);

            // Next gap ahead
            if (gapStart > worldX)
                return MakeGapInfo(mod, i);
        }

        return new GapInfo { Found = false };
    }

    /// <summary>
    /// Preview mode: generate N modules from current state without side effects.
    /// </summary>
    public List<PlacedModule> PreviewGenerate(int count)
    {
        // Save state
        float savedX = _nextModuleX;
        float savedY = _nextModuleY;
        int savedSeq = _sequenceIndex;
        int savedSame = _sameTerrainCount;
        var savedTerrain = _currentTerrain;
        int savedJump = _modulesSinceLastJump;
        float savedDist = _totalDistance;
        int savedCount = _placed.Count;
        var savedPhase = _currentPhase;
        float savedLastDrop = _lastDescentDrop;
        float savedLastLength = _lastDescentLength;

        // Generate preview modules
        var preview = new List<PlacedModule>();
        for (int i = 0; i < count; i++)
        {
            GenerateNextModule();
            preview.Add(_placed[^1]);
        }

        // Restore state
        while (_placed.Count > savedCount)
            _placed.RemoveAt(_placed.Count - 1);

        _nextModuleX = savedX;
        _nextModuleY = savedY;
        _sequenceIndex = savedSeq;
        _sameTerrainCount = savedSame;
        _currentTerrain = savedTerrain;
        _modulesSinceLastJump = savedJump;
        _totalDistance = savedDist;
        _currentPhase = savedPhase;
        _lastDescentDrop = savedLastDrop;
        _lastDescentLength = savedLastLength;

        return preview;
    }

    // ── Gap info struct (matching TerrainManager API) ────────────

    public struct GapInfo
    {
        public bool Found;
        public float GapStartX;
        public float GapEndX;
        public float LipY;
        public float LandingY;
        public float Width;
        public TerrainType Type;
    }

    // ── Internal: intro sequence ─────────────────────────────────

    private void PlaceIntroSequence()
    {
        // Long steep intro descent
        var introDescent = new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = TerrainType.Snow,
            ExitTerrain = TerrainType.Snow,
            Length = IntroDescentLength,
            Drop = IntroDescentDrop,
            Difficulty = 1,
            HasJump = false
        };
        PlaceModule(introDescent);

        // Stash intro descent stats for the first ramp
        _lastDescentDrop = IntroDescentDrop;
        _lastDescentLength = IntroDescentLength;

        // Flat approach before the intro ramp so the player settles on the ground
        var introApproach = new TrackModule
        {
            Shape = TrackModule.ModuleShape.Flat,
            EntryTerrain = TerrainType.Snow,
            ExitTerrain = TerrainType.Snow,
            Length = 800f,
            Drop = 40f,
            Rise = 0f,
            Difficulty = 1,
            HasJump = false,
            ObstacleDensity = 0f
        };
        PlaceModule(introApproach);

        // Intro ramp with jump
        var introRamp = new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = TerrainType.Snow,
            ExitTerrain = TerrainType.Snow,
            Length = IntroRampLength,
            Rise = IntroRampRise,
            Difficulty = 1,
            HasJump = true,
            GapWidth = IntroGapWidth
        };
        PlaceModule(introRamp);

        _sameTerrainCount = 3;
        _currentPhase = WavePhase.PostGap;
    }

    // ── Internal: wave-based module generation ───────────────────

    private void GenerateNextModule()
    {
        int maxSameRun = _difficulty.GetMaxSameTerrainRun(_totalDistance);
        bool mustSwitch = _sameTerrainCount >= maxSameRun;

        // Terrain transition takes priority — insert between waves
        if (mustSwitch && (_currentPhase == WavePhase.Descent || _currentPhase == WavePhase.PostGap))
        {
            int maxDiff = _difficulty.GetMaxDifficulty(_totalDistance);
            PlaceModule(SelectTransitionModule(maxDiff));
            _currentPhase = WavePhase.Descent;
            return;
        }

        switch (_currentPhase)
        {
            case WavePhase.Descent:
                GenerateDescentPhase();
                break;
            case WavePhase.VarietyInjection:
                GenerateVarietyPhase();
                break;
            case WavePhase.Ramp:
                GenerateRampPhase();
                break;
            case WavePhase.PostGap:
                _currentPhase = WavePhase.Descent;
                GenerateDescentPhase();
                break;
        }
    }

    private void GenerateDescentPhase()
    {
        string flavor = PickDescentFlavor();
        var descent = _factory.GenerateDescent(_totalDistance, _currentTerrain, flavor);
        PlaceModule(descent);

        _lastDescentDrop = descent.Drop;
        _lastDescentLength = descent.Length;

        // Decide next phase
        float roll = _rng.Randf();

        // Double descent? (two descents in a row)
        if (roll < _difficulty.DoubleDescentChance)
        {
            _currentPhase = WavePhase.Descent;
            return;
        }
        roll -= _difficulty.DoubleDescentChance;

        // Skip ramp entirely? (descent flows into next descent)
        if (roll < _difficulty.SkipRampChance)
        {
            _currentPhase = WavePhase.Descent;
            return;
        }
        roll -= _difficulty.SkipRampChance;

        // Insert a variety piece before the ramp?
        float varietyTotal = _difficulty.FlatBreatherChance + _difficulty.BumpRollerChance;
        if (roll < varietyTotal)
        {
            _currentPhase = WavePhase.VarietyInjection;
        }
        else
        {
            _currentPhase = WavePhase.Ramp;
        }
    }

    private void GenerateVarietyPhase()
    {
        float flatChance = _difficulty.FlatBreatherChance;
        float bumpChance = _difficulty.BumpRollerChance;
        float total = flatChance + bumpChance;

        float roll = _rng.Randf() * total;

        if (roll < flatChance)
        {
            PlaceModule(_factory.GenerateFlat(_totalDistance, _currentTerrain));
        }
        else
        {
            PlaceModule(_factory.GenerateBump(_totalDistance, _currentTerrain));
        }

        _currentPhase = WavePhase.Ramp;
    }

    private void GenerateRampPhase()
    {
        // Always place a flat approach before the ramp so the player can
        // settle on the ground after the descent and build stable momentum.
        var approach = _factory.GenerateApproach(_totalDistance, _currentTerrain);
        PlaceModule(approach);

        // Decide if this ramp has a jump
        bool shouldJump = _modulesSinceLastJump >= 2;
        bool withJump = shouldJump || _rng.Randf() < 0.4f;

        var ramp = _factory.GenerateRamp(
            _lastDescentDrop, _lastDescentLength,
            _totalDistance, _currentTerrain, withJump
        );
        PlaceModule(ramp);

        _currentPhase = withJump ? WavePhase.PostGap : WavePhase.Descent;
    }

    private string PickDescentFlavor()
    {
        float normalWeight = 1.0f;
        float shortSteepWeight = 0.2f;
        float longGentleWeight = 0.2f;
        float cruiseWeight = 0.15f;

        // At higher difficulty, increase variety weights
        int maxDiff = _difficulty.GetMaxDifficulty(_totalDistance);
        if (maxDiff >= 3)
        {
            shortSteepWeight = 0.35f;
            longGentleWeight = 0.3f;
        }
        if (maxDiff >= 4)
        {
            shortSteepWeight = 0.4f;
        }

        float total = normalWeight + shortSteepWeight + longGentleWeight + cruiseWeight;
        float roll = _rng.Randf() * total;

        if (roll < normalWeight) return "normal";
        roll -= normalWeight;
        if (roll < shortSteepWeight) return "short_steep";
        roll -= shortSteepWeight;
        if (roll < longGentleWeight) return "long_gentle";
        return "cruise";
    }

    // ── Internal: transition module selection ────────────────────

    private TrackModule SelectTransitionModule(int maxDiff)
    {
        // Pick a target terrain different from current
        var targets = new List<TerrainType>();
        if (_currentTerrain != TerrainType.Snow) targets.Add(TerrainType.Snow);
        if (_currentTerrain != TerrainType.Dirt) targets.Add(TerrainType.Dirt);
        if (_currentTerrain != TerrainType.Ice && maxDiff >= 2) targets.Add(TerrainType.Ice);

        var targetTerrain = targets[_rng.RandiRange(0, targets.Count - 1)];

        var candidates = _catalog.QueryTransitions(_currentTerrain, targetTerrain);
        candidates.RemoveAll(m => m.Difficulty > maxDiff || m.MinDistance > _totalDistance);

        // If a jump is overdue, prefer transition modules with jumps
        if (_modulesSinceLastJump >= 3)
        {
            var jumpTransitions = candidates.FindAll(m => m.HasJump);
            if (jumpTransitions.Count > 0)
                return WeightedSelect(jumpTransitions);
        }

        if (candidates.Count > 0)
            return WeightedSelect(candidates);

        // Fallback: create a generic transition
        return new TrackModule
        {
            Shape = TrackModule.ModuleShape.Transition,
            EntryTerrain = _currentTerrain,
            ExitTerrain = targetTerrain,
            Length = 500f,
            Drop = 180f,
            Difficulty = 1
        };
    }

    private TrackModule WeightedSelect(List<TrackModule> candidates)
    {
        if (candidates.Count == 1)
            return candidates[0];

        float totalWeight = 0f;
        foreach (var c in candidates)
            totalWeight += c.Weight;

        float roll = _rng.Randf() * totalWeight;
        float cumulative = 0f;

        foreach (var c in candidates)
        {
            cumulative += c.Weight;
            if (roll <= cumulative)
                return c;
        }

        return candidates[^1];
    }

    // ── Internal: module placement ───────────────────────────────

    private void PlaceModule(TrackModule template)
    {
        float dropMult = _difficulty.GetDropMultiplier(_totalDistance);
        float gapMult = _difficulty.GetGapMultiplier(_totalDistance);

        // Scale geometry by difficulty
        float effectiveDrop = template.Drop * dropMult;
        float scaledGapWidth = template.HasJump ? template.GapWidth * gapMult : 0f;

        // Create a scaled copy for placement (so the template stays pristine)
        var scaledTemplate = new TrackModule
        {
            Shape = template.Shape,
            EntryTerrain = template.EntryTerrain,
            ExitTerrain = template.ExitTerrain,
            Length = template.Length,
            Drop = effectiveDrop,
            Rise = template.Rise,
            Difficulty = template.Difficulty,
            HasJump = template.HasJump,
            GapWidth = scaledGapWidth,
            Weight = template.Weight,
            MinDistance = template.MinDistance,
            ObstacleDensity = template.ObstacleDensity,
            AllowedObstacleTypes = template.AllowedObstacleTypes
        };

        float exitY = scaledTemplate.ComputeExitY(_nextModuleY);
        float gapEndX = _nextModuleX + scaledTemplate.Length + scaledGapWidth;

        // Landing offset after gap (slight drop for natural feel)
        float landingOffset = scaledTemplate.HasJump ? _rng.RandfRange(30f, 80f) : 0f;

        var placed = new PlacedModule
        {
            Template = scaledTemplate,
            WorldStartX = _nextModuleX,
            WorldEndX = _nextModuleX + scaledTemplate.Length,
            EntryY = _nextModuleY,
            ExitY = exitY,
            Length = scaledTemplate.Length,
            SequenceIndex = _sequenceIndex,
            GapEndX = gapEndX,
            ScaledGapWidth = scaledGapWidth
        };

        _placed.Add(placed);

        // Advance state
        _nextModuleX = gapEndX;
        _nextModuleY = exitY + landingOffset;
        _sequenceIndex++;
        _totalDistance += scaledTemplate.Length + scaledGapWidth;

        // Update terrain tracking
        if (scaledTemplate.IsTransition)
        {
            _currentTerrain = scaledTemplate.ExitTerrain;
            _sameTerrainCount = 1;
        }
        else
        {
            _sameTerrainCount++;
        }

        // Update jump tracking
        if (scaledTemplate.HasJump)
            _modulesSinceLastJump = 0;
        else
            _modulesSinceLastJump++;
    }

    // ── Internal: queries ────────────────────────────────────────

    private PlacedModule FindModuleAt(float worldX)
    {
        for (int i = 0; i < _placed.Count; i++)
        {
            var mod = _placed[i];
            if (worldX >= mod.WorldStartX && worldX < mod.WorldEndX)
                return mod;
        }
        return null;
    }

    private PlacedModule FindGapAt(float worldX)
    {
        for (int i = 0; i < _placed.Count; i++)
        {
            var mod = _placed[i];
            if (!mod.Template.HasJump) continue;
            if (worldX >= mod.GapStartX && worldX < mod.GapEndX)
                return mod;
        }
        return null;
    }

    private GapInfo MakeGapInfo(PlacedModule mod, int index)
    {
        float landingY = mod.ExitY;

        // Use next module's entry Y if available
        if (index + 1 < _placed.Count)
            landingY = _placed[index + 1].EntryY;

        return new GapInfo
        {
            Found = true,
            GapStartX = mod.GapStartX,
            GapEndX = mod.GapEndX,
            LipY = mod.ExitY,
            LandingY = landingY,
            Width = mod.ScaledGapWidth,
            Type = mod.Template.ExitTerrain
        };
    }

    private int CountModulesAheadOf(float worldX)
    {
        int count = 0;
        for (int i = _placed.Count - 1; i >= 0; i--)
        {
            if (_placed[i].WorldEndX > worldX)
                count++;
            else
                break;
        }
        return count;
    }

    private bool TrimBehind(float playerX)
    {
        bool trimmed = false;
        while (_placed.Count > 2 && _placed[0].GapEndX < playerX - DespawnBehind)
        {
            _placed.RemoveAt(0);
            trimmed = true;
        }
        return trimmed;
    }
}
