using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Generates TrackModule instances procedurally with randomized parameters.
/// Replaces the fixed catalog for descent, ramp, flat, and bump modules.
/// Transition modules are still sourced from the ModuleCatalog.
///
/// All descent drops are derived from the guidance slope angle:
///   guidedDrop = length * tan(guidanceAngle)
///   actualDrop = guidedDrop * randomVariance
///
/// Ramp rises are proportionally smaller than the preceding descent's drop,
/// ensuring net movement follows the guidance slope downhill.
/// </summary>
public class ProceduralModuleFactory
{
    private readonly DifficultyProfile _difficulty;
    private readonly RandomNumberGenerator _rng;

    public ProceduralModuleFactory(DifficultyProfile difficulty, RandomNumberGenerator rng)
    {
        _difficulty = difficulty;
        _rng = rng;
    }

    /// <summary>
    /// Generate a Descent module with parameters derived from the guidance slope.
    /// </summary>
    /// <param name="distance">Current distance into the run.</param>
    /// <param name="terrain">Terrain type for this module.</param>
    /// <param name="flavor">Controls length/steepness: "normal", "short_steep", "long_gentle", "cruise".</param>
    public TrackModule GenerateDescent(float distance, TerrainType terrain, string flavor = "normal")
    {
        float minLength, maxLength;
        float slopeVarianceMin, slopeVarianceMax;

        switch (flavor)
        {
            case "short_steep":
                minLength = 800f;
                maxLength = 1800f;
                slopeVarianceMin = 1.3f;
                slopeVarianceMax = 2.2f;
                break;
            case "long_gentle":
                minLength = 2500f;
                maxLength = 4500f;
                slopeVarianceMin = 0.5f;
                slopeVarianceMax = 0.9f;
                break;
            case "cruise":
                minLength = 2000f;
                maxLength = 3500f;
                slopeVarianceMin = 0.6f;
                slopeVarianceMax = 0.8f;
                break;
            default: // "normal"
                minLength = 1200f;
                maxLength = 3000f;
                slopeVarianceMin = _difficulty.DescentDropVarianceMin;
                slopeVarianceMax = _difficulty.DescentDropVarianceMax;
                break;
        }

        float length = _rng.RandfRange(minLength, maxLength);
        float guidedDrop = length * _difficulty.GetGuidanceSlopeTan(distance);
        float variance = _rng.RandfRange(slopeVarianceMin, slopeVarianceMax);
        float drop = guidedDrop * variance;

        // Clamp to reasonable bounds
        drop = Mathf.Clamp(drop, 80f, 2500f);

        // Difficulty rating from steepness relative to guidance
        int diff = variance > 1.4f ? 3 : variance > 1.0f ? 2 : 1;
        diff = Mathf.Min(diff, _difficulty.GetMaxDifficulty(distance));

        // Obstacle density scales with length and difficulty (sparse — avalanche is the main threat)
        float obsDensity = Mathf.Clamp(length / 3000f * diff * 0.08f, 0f, 0.15f);

        return new TrackModule
        {
            Shape = TrackModule.ModuleShape.Descent,
            EntryTerrain = terrain,
            ExitTerrain = terrain,
            Length = length,
            Drop = drop,
            Difficulty = diff,
            Weight = 1.0f,
            HasJump = false,
            ObstacleDensity = obsDensity,
            AllowedObstacleTypes = GetObstacleTypes(terrain)
        };
    }

    /// <summary>
    /// Generate a Ramp module sized proportionally to the preceding descent.
    /// </summary>
    /// <param name="precedingDrop">The drop of the descent that precedes this ramp.</param>
    /// <param name="precedingLength">The length of the preceding descent.</param>
    /// <param name="distance">Current distance into the run.</param>
    /// <param name="terrain">Current terrain type.</param>
    /// <param name="withJump">Whether this ramp ends in a gap.</param>
    public TrackModule GenerateRamp(float precedingDrop, float precedingLength,
                                     float distance, TerrainType terrain, bool withJump)
    {
        // Ramp length: 40-65% of the preceding descent length (longer = gentler approach)
        float lengthRatio = _rng.RandfRange(0.40f, 0.65f);
        float length = Mathf.Clamp(precedingLength * lengthRatio, 900f, 3000f);

        // Rise: fraction of preceding drop (ensures net downhill)
        float riseRatio = Mathf.Lerp(
            _difficulty.RampRiseToDropRatio,
            _difficulty.RampRiseToDropRatioMax,
            Mathf.Clamp(distance / 20000f, 0f, 1f)
        );
        float riseVariance = _rng.RandfRange(0.85f, 1.15f);
        float rise = Mathf.Abs(precedingDrop) * riseRatio * riseVariance;
        rise = Mathf.Clamp(rise, 150f, 700f);

        // Gap width computed dynamically from preceding descent
        float gapWidth = 0f;
        if (withJump)
        {
            gapWidth = ComputeGapWidth(precedingDrop, precedingLength, distance);
        }

        return new TrackModule
        {
            Shape = TrackModule.ModuleShape.Ramp,
            EntryTerrain = terrain,
            ExitTerrain = terrain,
            Length = length,
            Rise = rise,
            Drop = 0f,
            Difficulty = Mathf.Min(3, _difficulty.GetMaxDifficulty(distance)),
            Weight = 1.0f,
            HasJump = withJump,
            GapWidth = gapWidth,
            ObstacleDensity = 0f
        };
    }

    /// <summary>
    /// Generate a Flat breather module.
    /// </summary>
    public TrackModule GenerateFlat(float distance, TerrainType terrain)
    {
        float length = _rng.RandfRange(300f, 700f);
        // Slight drop following guidance slope but very gentle
        float gentleDrop = length * _difficulty.GetGuidanceSlopeTan(distance) * 0.15f;
        gentleDrop = Mathf.Clamp(gentleDrop, 10f, 80f);

        float obsDensity = _rng.RandfRange(0.05f, 0.15f);

        return new TrackModule
        {
            Shape = TrackModule.ModuleShape.Flat,
            EntryTerrain = terrain,
            ExitTerrain = terrain,
            Length = length,
            Drop = gentleDrop,
            Rise = 0f,
            Difficulty = 1,
            Weight = 1.0f,
            HasJump = false,
            ObstacleDensity = obsDensity,
            AllowedObstacleTypes = GetObstacleTypes(terrain)
        };
    }

    /// <summary>
    /// Generate a Bump/roller module.
    /// </summary>
    public TrackModule GenerateBump(float distance, TerrainType terrain)
    {
        float length = _rng.RandfRange(400f, 800f);
        float guidedDrop = length * _difficulty.GetGuidanceSlopeTan(distance) * 0.6f;
        float drop = Mathf.Clamp(guidedDrop, 30f, 200f);
        float rise = _rng.RandfRange(80f, 220f);

        float obsDensity = _rng.RandfRange(0.05f, 0.1f);

        return new TrackModule
        {
            Shape = TrackModule.ModuleShape.Bump,
            EntryTerrain = terrain,
            ExitTerrain = terrain,
            Length = length,
            Drop = drop,
            Rise = rise,
            Difficulty = Mathf.Min(2, _difficulty.GetMaxDifficulty(distance)),
            Weight = 1.0f,
            HasJump = false,
            ObstacleDensity = obsDensity,
            AllowedObstacleTypes = GetObstacleTypes(terrain)
        };
    }

    /// <summary>
    /// Generate a RollingHills module — multiple gentle hills for hill-to-hill jumping.
    /// </summary>
    public TrackModule GenerateRollingHills(float distance, TerrainType terrain)
    {
        float length = _rng.RandfRange(6000f, 8000f);
        // Gentle net downhill — fraction of guidance slope
        float guidedDrop = length * _difficulty.GetGuidanceSlopeTan(distance) * 0.35f;
        float drop = Mathf.Clamp(guidedDrop, 30f, 250f);
        // Hill amplitude — modest so crests give brief air, not extended flight
        float rise = _rng.RandfRange(200f, 500f);

        float obsDensity = _rng.RandfRange(0.03f, 0.08f);

        return new TrackModule
        {
            Shape = TrackModule.ModuleShape.RollingHills,
            EntryTerrain = terrain,
            ExitTerrain = terrain,
            Length = length,
            Drop = drop,
            Rise = rise,
            Difficulty = Mathf.Min(2, _difficulty.GetMaxDifficulty(distance)),
            Weight = 1.0f,
            HasJump = false,
            ObstacleDensity = obsDensity,
            AllowedObstacleTypes = GetObstacleTypes(terrain)
        };
    }

    /// <summary>
    /// Compute dynamic gap width based on the preceding descent geometry.
    /// Steeper/longer descents produce wider gaps (sub-linear via sqrt).
    /// </summary>
    public float ComputeGapWidth(float descentDrop, float descentLength, float distance)
    {
        const float baseGap = 380f;
        const float referenceDrop = 600f;

        float dropRatio = Mathf.Abs(descentDrop) / referenceDrop;
        float gapFromDrop = baseGap * Mathf.Sqrt(dropRatio);

        float gapMultiplier = _difficulty.GetGapMultiplier(distance);
        float gap = gapFromDrop * gapMultiplier;
        return Mathf.Clamp(gap, 300f, 1200f);
    }

    private static string[] GetObstacleTypes(TerrainType terrain) => terrain switch
    {
        TerrainType.Snow => new[] { "Rock", "Tree", "Log" },
        TerrainType.Dirt => new[] { "Rock", "Tree", "Log" },
        TerrainType.Ice => new[] { "Rock" },
        _ => new[] { "Rock" }
    };
}
