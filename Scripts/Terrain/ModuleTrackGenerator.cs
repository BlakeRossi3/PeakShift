using System.Collections.Generic;
using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Core track generation engine using compound module-based terrain generation.
///
/// Architecture:
///   1. ProceduralModuleFactory generates compound modules containing multiple
///      sub-sections (Landing → Interior → ExitRamp+Gap) with C1 continuity.
///   2. DifficultyProfile controls guidance slope, compound module length/complexity,
///      gap scaling, and terrain switching frequency.
///   3. Each compound module is 8,000-20,000px and self-contained: starts with a
///      Landing zone, contains 2-4 curated interior sub-sections, and ends with
///      a Ramp+Gap. Module-to-module transitions always happen in the air.
///   4. The intro sequence uses legacy TrackModule-based placement for the initial
///      descent and first ramp.
///
/// The generator maintains a sequence of placed modules that the TerrainManager
/// can query for height, normals, gap info, and terrain type at any world X.
/// </summary>
public class ModuleTrackGenerator
{
    // ── Placed module instance ───────────────────────────────────

    /// <summary>
    /// A module that has been placed in the world with concrete positions.
    /// Supports both legacy TrackModule templates (intro) and CompoundModules.
    /// </summary>
    public class PlacedModule
    {
        /// <summary>Legacy template (used for intro sequence). Null for compound modules.</summary>
        public TrackModule Template { get; init; }

        /// <summary>Compound module data. Null for legacy template modules.</summary>
        public CompoundModule Compound { get; init; }

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

        /// <summary>Whether this module ends with a gap.</summary>
        public bool HasJump =>
            Compound != null || (Template?.HasJump ?? false);

        /// <summary>The exit terrain type of this module.</summary>
        public TerrainType ModuleExitTerrain =>
            Compound?.ExitTerrain ?? Template?.ExitTerrain ?? TerrainType.Snow;

        /// <summary>The terrain type the player experiences in this module.</summary>
        public TerrainType ActiveTerrainAt(float worldX)
        {
            if (Compound != null)
                return Compound.GetTerrainTypeAt(worldX - WorldStartX);

            float t = (worldX - WorldStartX) / Length;
            return Template.GetTerrainTypeAt(t);
        }

        /// <summary>Surface Y at a given world X within this module.</summary>
        public float HeightAt(float worldX)
        {
            if (Compound != null)
                return Compound.SampleHeight(worldX - WorldStartX);

            float t = (worldX - WorldStartX) / Length;
            return Template.SampleHeight(t, EntryY);
        }
    }

    // ── Configuration ────────────────────────────────────────────

    private readonly DifficultyProfile _difficulty;
    private readonly RandomNumberGenerator _rng = new();
    private readonly ProceduralModuleFactory _factory;

    /// <summary>How many modules ahead of the player to keep generated.</summary>
    public int LookaheadModules { get; set; } = 8;

    /// <summary>How far behind the player (px) before despawning modules.</summary>
    public float DespawnBehind { get; set; } = 2000f;

    // ── Intro run parameters ─────────────────────────────────────

    private const float IntroDescentLength = 5000f;
    private const float IntroDescentDrop = 10000f;
    private const float IntroRampLength = 2000f;
    private const float IntroRampRise = 1000f;
    private const float IntroGapWidth = 1000f;

    // ── Generation state ─────────────────────────────────────────

    private readonly List<PlacedModule> _placed = new();
    private float _nextModuleX;
    private float _nextModuleY;
    private int _sequenceIndex;
    private int _sameTerrainCount;
    private TerrainType _currentTerrain = TerrainType.Snow;
    private float _totalDistance;
    private float _lastExitSlope;

    /// <summary>All currently placed modules (read-only).</summary>
    public IReadOnlyList<PlacedModule> PlacedModules => _placed;

    /// <summary>Current terrain type at the generation head.</summary>
    public TerrainType CurrentTerrain => _currentTerrain;

    /// <summary>Total distance generated so far.</summary>
    public float TotalDistance => _totalDistance;

    // ── Construction ─────────────────────────────────────────────

    public ModuleTrackGenerator(DifficultyProfile difficulty)
    {
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
        _totalDistance = 0f;
        _lastExitSlope = 0f;

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
            if (!mod.HasJump) continue;

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
        float savedDist = _totalDistance;
        float savedSlope = _lastExitSlope;
        int savedCount = _placed.Count;

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
        _totalDistance = savedDist;
        _lastExitSlope = savedSlope;

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
        // Long steep intro descent (legacy TrackModule)
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
        PlaceLegacyModule(introDescent);

        // Intro ramp with jump (legacy TrackModule)
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
        PlaceLegacyModule(introRamp);

        _sameTerrainCount = 2;

        // Compute exit slope from intro ramp for landing matching
        // Ramp formula derivative at t=1: Rise * phase * pi * sin(phase * pi) / ((1 - cos(phase * pi)) * Length)
        // Negative because ramp goes upward (decreasing Y)
        const float phase = 0.85f;
        _lastExitSlope = -IntroRampRise * phase * Mathf.Pi * Mathf.Sin(phase * Mathf.Pi)
                         / ((1f - Mathf.Cos(phase * Mathf.Pi)) * IntroRampLength);
    }

    // ── Internal: compound module generation ─────────────────────

    private void GenerateNextModule()
    {
        // Plan terrain sequence for this compound module
        var terrainSeq = PlanTerrainSequence();

        // Create compound module
        var compound = _factory.CreateCompoundModule(
            _totalDistance, _nextModuleY, _lastExitSlope, terrainSeq);

        // Place it
        PlaceCompoundModule(compound);
    }

    private List<TerrainType> PlanTerrainSequence()
    {
        int maxSameRun = _difficulty.GetMaxSameTerrainRun(_totalDistance);
        bool shouldSwitch = _sameTerrainCount >= maxSameRun;

        var seq = new List<TerrainType> { _currentTerrain };

        if (shouldSwitch || _rng.Randf() < 0.3f)
        {
            var target = PickDifferentTerrain(_currentTerrain);
            seq.Add(target);

            // Small chance of a third terrain at high difficulty
            if (_difficulty.GetMaxDifficulty(_totalDistance) >= 4 && _rng.Randf() < 0.15f)
            {
                var third = PickDifferentTerrain(target);
                seq.Add(third);
            }
        }

        return seq;
    }

    private TerrainType PickDifferentTerrain(TerrainType current)
    {
        var candidates = new List<TerrainType>();
        if (current != TerrainType.Snow) candidates.Add(TerrainType.Snow);
        if (current != TerrainType.Dirt) candidates.Add(TerrainType.Dirt);

        int maxDiff = _difficulty.GetMaxDifficulty(_totalDistance);
        if (current != TerrainType.Ice && maxDiff >= 2)
            candidates.Add(TerrainType.Ice);

        if (candidates.Count == 0)
            return TerrainType.Snow;

        return candidates[_rng.RandiRange(0, candidates.Count - 1)];
    }

    private void PlaceCompoundModule(CompoundModule compound)
    {
        float gapMult = _difficulty.GetGapMultiplier(_totalDistance);
        float scaledGap = compound.GapWidth * gapMult;

        var placed = new PlacedModule
        {
            Template = null,
            Compound = compound,
            WorldStartX = _nextModuleX,
            WorldEndX = _nextModuleX + compound.TotalLength,
            EntryY = _nextModuleY,
            ExitY = compound.ExitY,
            Length = compound.TotalLength,
            SequenceIndex = _sequenceIndex,
            GapEndX = _nextModuleX + compound.TotalLength + scaledGap,
            ScaledGapWidth = scaledGap
        };

        _placed.Add(placed);

        // Advance state
        float landingOffset = _rng.RandfRange(30f, 80f);
        _nextModuleX = placed.GapEndX;
        _nextModuleY = compound.ExitY + landingOffset;
        _sequenceIndex++;
        _totalDistance += compound.TotalLength + scaledGap;

        // Track the exit ramp's lip slope for next module's landing
        var lastSec = compound.Sections[^1];
        _lastExitSlope = lastSec.ExitSlope;

        // Update terrain tracking
        _currentTerrain = compound.ExitTerrain;
        if (compound.HasTerrainTransition)
            _sameTerrainCount = 1;
        else
            _sameTerrainCount++;
    }

    // ── Internal: legacy module placement (intro only) ────────────

    private void PlaceLegacyModule(TrackModule template)
    {
        float dropMult = _difficulty.GetDropMultiplier(_totalDistance);
        float gapMult = _difficulty.GetGapMultiplier(_totalDistance);

        float effectiveDrop = template.Drop * dropMult;
        float scaledGapWidth = template.HasJump ? template.GapWidth * gapMult : 0f;

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
        float landingOffset = scaledTemplate.HasJump ? _rng.RandfRange(30f, 80f) : 0f;

        var placed = new PlacedModule
        {
            Template = scaledTemplate,
            Compound = null,
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

        _nextModuleX = gapEndX;
        _nextModuleY = exitY + landingOffset;
        _sequenceIndex++;
        _totalDistance += scaledTemplate.Length + scaledGapWidth;

        if (scaledTemplate.IsTransition)
        {
            _currentTerrain = scaledTemplate.ExitTerrain;
            _sameTerrainCount = 1;
        }
        else
        {
            _sameTerrainCount++;
        }
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
            if (!mod.HasJump) continue;
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
            Type = mod.ModuleExitTerrain
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
