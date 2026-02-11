using System.Collections.Generic;
using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Core track generation engine. Replaces the old ad-hoc TerrainManager generation
/// with modular prefab-based track pieces from a ModuleCatalog.
///
/// Architecture:
///   1. ModuleCatalog provides the library of available TrackModule definitions.
///   2. DifficultyProfile controls how constraints tighten over distance.
///   3. This generator picks modules via weighted random selection with constraints:
///      - Terrain alternation (max N same-type in a row, forced transitions)
///      - Difficulty gating (harder modules unlock with distance)
///      - Jump spacing (not every module is a jump, but jumps are clear-or-die)
///   4. ModulePool recycles StaticBody2D nodes for efficiency.
///   5. Placed modules are tracked as PlacedModule instances for height queries.
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

        /// <summary>True if this module is a gap (airborne section).</summary>
        public bool IsGapModule => Template.Shape == TrackModule.ModuleShape.Gap;
    }

    // ── Configuration ────────────────────────────────────────────

    private readonly ModuleCatalog _catalog;
    private readonly DifficultyProfile _difficulty;
    private readonly RandomNumberGenerator _rng = new();

    /// <summary>How many modules ahead of the player to keep generated.</summary>
    public int LookaheadModules { get; set; } = 8;

    /// <summary>How far behind the player (px) before despawning modules.</summary>
    public float DespawnBehind { get; set; } = 2000f;

    // ── Intro run parameters ─────────────────────────────────────

    private const float IntroDescentLength = 5000f;
    private const float IntroDescentDrop = 8000f;
    private const float IntroRampLength = 500f;
    private const float IntroRampRise = 350f;
    private const float IntroGapWidth = 250f;

    // ── State ────────────────────────────────────────────────────

    private readonly List<PlacedModule> _placed = new();
    private float _nextModuleX;
    private float _nextModuleY;
    private int _sequenceIndex;
    private int _sameTerrainCount;
    private TerrainType _currentTerrain = TerrainType.Snow;
    private int _modulesSinceLastJump;
    private float _totalDistance;
    private bool _introPlaced;

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
        _introPlaced = false;

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
    /// Returns the list of placed modules for inspection.
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

        // Generate preview modules
        var preview = new List<PlacedModule>();
        for (int i = 0; i < count; i++)
        {
            GenerateNextModule();
            preview.Add(_placed[^1]);
        }

        // Restore state: remove preview modules
        while (_placed.Count > savedCount)
            _placed.RemoveAt(_placed.Count - 1);

        _nextModuleX = savedX;
        _nextModuleY = savedY;
        _sequenceIndex = savedSeq;
        _sameTerrainCount = savedSame;
        _currentTerrain = savedTerrain;
        _modulesSinceLastJump = savedJump;
        _totalDistance = savedDist;

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

        _introPlaced = true;
        _sameTerrainCount = 2;
    }

    // ── Internal: module generation ──────────────────────────────

    private void GenerateNextModule()
    {
        int maxDiff = _difficulty.GetMaxDifficulty(_totalDistance);
        int maxSameRun = _difficulty.GetMaxSameTerrainRun(_totalDistance);

        bool mustSwitch = _sameTerrainCount >= maxSameRun;
        bool shouldJump = _modulesSinceLastJump >= 2;  // At least one jump every ~3 modules

        TrackModule selected;

        // If both jump and switch are needed, prioritize jump every other time
        if (mustSwitch && shouldJump && _modulesSinceLastJump >= 3)
        {
            // Really need a jump - try to get a transition with jump
            selected = SelectTransitionJumpModule(maxDiff);
        }
        else if (mustSwitch)
        {
            // Must place a transition piece
            selected = SelectTransitionModule(maxDiff);
        }
        else if (shouldJump)
        {
            // Try to place a ramp with jump
            selected = SelectJumpModule(maxDiff);
        }
        else
        {
            // Normal selection: descent, flat, bump, or ramp-no-jump
            selected = SelectNormalModule(maxDiff);
        }

        PlaceModule(selected);
    }

    private TrackModule SelectTransitionJumpModule(int maxDiff)
    {
        // Try to find a transition module with a jump
        var targets = new List<TerrainType>();
        if (_currentTerrain != TerrainType.Snow) targets.Add(TerrainType.Snow);
        if (_currentTerrain != TerrainType.Dirt) targets.Add(TerrainType.Dirt);
        if (_currentTerrain != TerrainType.Ice && maxDiff >= 2) targets.Add(TerrainType.Ice);

        var targetTerrain = targets[_rng.RandiRange(0, targets.Count - 1)];

        var candidates = _catalog.QueryTransitions(_currentTerrain, targetTerrain);
        candidates.RemoveAll(m => m.Difficulty > maxDiff || m.MinDistance > _totalDistance);

        // Prefer transition modules with jumps
        var jumpTransitions = candidates.FindAll(m => m.HasJump);
        if (jumpTransitions.Count > 0)
            return WeightedSelect(jumpTransitions);

        // Fallback to regular transition
        return SelectTransitionModule(maxDiff);
    }

    private TrackModule SelectTransitionModule(int maxDiff)
    {
        // Pick a target terrain different from current
        var targets = new List<TerrainType>();
        if (_currentTerrain != TerrainType.Snow) targets.Add(TerrainType.Snow);
        if (_currentTerrain != TerrainType.Dirt) targets.Add(TerrainType.Dirt);
        if (_currentTerrain != TerrainType.Ice && maxDiff >= 2) targets.Add(TerrainType.Ice);

        var targetTerrain = targets[_rng.RandiRange(0, targets.Count - 1)];

        var candidates = _catalog.QueryTransitions(_currentTerrain, targetTerrain);

        // Filter by difficulty
        candidates.RemoveAll(m => m.Difficulty > maxDiff || m.MinDistance > _totalDistance);

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

    private TrackModule SelectJumpModule(int maxDiff)
    {
        var candidates = _catalog.Query(
            terrainFilter: _currentTerrain,
            maxDifficulty: maxDiff,
            currentDistance: _totalDistance,
            allowJumps: true,
            requireTransition: false
        );

        // Prefer modules with jumps
        var jumpCandidates = candidates.FindAll(m => m.HasJump);
        if (jumpCandidates.Count > 0)
            return WeightedSelect(jumpCandidates);

        // Fallback to any candidate
        if (candidates.Count > 0)
            return WeightedSelect(candidates);

        // Emergency fallback
        return CreateFallbackDescent();
    }

    private TrackModule SelectNormalModule(int maxDiff)
    {
        var candidates = _catalog.Query(
            terrainFilter: _currentTerrain,
            maxDifficulty: maxDiff,
            currentDistance: _totalDistance,
            allowJumps: true,
            requireTransition: false
        );

        if (candidates.Count > 0)
            return WeightedSelect(candidates);

        return CreateFallbackDescent();
    }

    private TrackModule CreateFallbackDescent()
    {
        return new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = _currentTerrain,
            ExitTerrain = _currentTerrain,
            Length = 1500f,
            Drop = 600f,
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
            MinDistance = template.MinDistance
        };

        float exitY = scaledTemplate.ComputeExitY(_nextModuleY);
        float gapEndX = _nextModuleX + scaledTemplate.Length + scaledGapWidth;

        // Landing offset after gap (slight drop for natural feel)
        float landingOffset = template.HasJump ? _rng.RandfRange(30f, 80f) : 0f;

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
