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
                minLength = 400f;
                maxLength = 900f;
                slopeVarianceMin = 1.2f;
                slopeVarianceMax = 2.0f;
                break;
            case "long_gentle":
                minLength = 2500f;
                maxLength = 4000f;
                slopeVarianceMin = 0.4f;
                slopeVarianceMax = 0.8f;
                break;
            case "cruise":
                minLength = 1800f;
                maxLength = 3000f;
                slopeVarianceMin = 0.5f;
                slopeVarianceMax = 0.7f;
                break;
            default: // "normal"
                minLength = 800f;
                maxLength = 2500f;
                slopeVarianceMin = _difficulty.DescentDropVarianceMin;
                slopeVarianceMax = _difficulty.DescentDropVarianceMax;
                break;
        }

        float length = _rng.RandfRange(minLength, maxLength);
        float guidedDrop = length * _difficulty.GetGuidanceSlopeTan(distance);
        float variance = _rng.RandfRange(slopeVarianceMin, slopeVarianceMax);
        float drop = guidedDrop * variance;

        // Clamp to reasonable bounds
        drop = Mathf.Clamp(drop, 50f, 3000f);

        // Difficulty rating from steepness relative to guidance
        int diff = variance > 1.4f ? 3 : variance > 1.0f ? 2 : 1;
        diff = Mathf.Min(diff, _difficulty.GetMaxDifficulty(distance));

        // Obstacle density scales with length and difficulty
        float obsDensity = Mathf.Clamp(length / 3000f * diff * 0.3f, 0f, 0.6f);

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
        // Ramp length: 15-30% of the preceding descent length
        float lengthRatio = _rng.RandfRange(0.15f, 0.30f);
        float length = Mathf.Clamp(precedingLength * lengthRatio, 200f, 600f);

        // Rise: fraction of preceding drop (ensures net downhill)
        float riseRatio = Mathf.Lerp(
            _difficulty.RampRiseToDropRatio,
            _difficulty.RampRiseToDropRatioMax,
            Mathf.Clamp(distance / 20000f, 0f, 1f)
        );
        float riseVariance = _rng.RandfRange(0.8f, 1.2f);
        float rise = Mathf.Abs(precedingDrop) * riseRatio * riseVariance;
        rise = Mathf.Clamp(rise, 80f, 500f);

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

        float obsDensity = _rng.RandfRange(0.3f, 0.6f);

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

        float obsDensity = _rng.RandfRange(0.2f, 0.4f);

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
    /// Compute dynamic gap width based on the preceding descent geometry.
    /// Steeper/longer descents produce wider gaps (sub-linear via sqrt).
    /// </summary>
    public float ComputeGapWidth(float descentDrop, float descentLength, float distance)
    {
        const float baseGap = 180f;
        const float referenceDrop = 600f;

        float dropRatio = Mathf.Abs(descentDrop) / referenceDrop;
        float gapFromDrop = baseGap * Mathf.Sqrt(dropRatio);

        float gapMultiplier = _difficulty.GetGapMultiplier(distance);
        float gap = gapFromDrop * gapMultiplier;
        return Mathf.Clamp(gap, 120f, 500f);
    }

    private static string[] GetObstacleTypes(TerrainType terrain) => terrain switch
    {
        TerrainType.Snow => new[] { "Rock", "Tree", "Log" },
        TerrainType.Dirt => new[] { "Rock", "Tree", "Log" },
        TerrainType.Ice => new[] { "Rock" },
        _ => new[] { "Rock" }
    };
}
