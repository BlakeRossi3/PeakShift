using System.Collections.Generic;
using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Generates TrackModule instances procedurally with randomized parameters.
/// Replaces the fixed catalog for descent, ramp, flat, and bump modules.
/// Terrain transitions are handled internally by CompoundModule sub-sections.
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

    // ── Compound Module Generation ────────────────────────────────

    /// <summary>
    /// Creates a complete compound module: Landing → [2-4 Interior] → ExitRamp + Gap.
    /// </summary>
    /// <param name="distance">Current distance into the run.</param>
    /// <param name="entryY">Y position where this module starts.</param>
    /// <param name="entrySlope">dy/dx slope at entry (from previous ramp lip trajectory).</param>
    /// <param name="terrainSequence">Ordered terrain types to use across the module.</param>
    public CompoundModule CreateCompoundModule(
        float distance, float entryY, float entrySlope,
        List<TerrainType> terrainSequence)
    {
        var compound = new CompoundModule();

        // Determine parameters from difficulty
        int interiorCount = _difficulty.GetCompoundInteriorCount(distance);
        float totalLength = _difficulty.GetCompoundModuleLength(distance, _rng);
        float guidanceSlopeTan = _difficulty.GetGuidanceSlopeTan(distance);

        // Budget length across sections
        float landingLength = _rng.RandfRange(800f, 1500f);
        float rampLength = _rng.RandfRange(1200f, 2500f);
        float interiorBudget = totalLength - landingLength - rampLength;
        interiorBudget = Mathf.Max(interiorBudget, interiorCount * 1500f);
        var interiorLengths = DivideBudget(interiorBudget, interiorCount);

        // Pick interior section types
        var interiorTypes = PickInteriorTypes(interiorCount, distance, terrainSequence);

        float currentX = 0f;
        float currentY = entryY;
        // Ensure landing entry slope is plausibly downhill (positive in Y-down)
        float currentSlope = Mathf.Abs(entrySlope) < 0.01f ? 0.4f : entrySlope;
        currentSlope = Mathf.Max(currentSlope, 0.15f);

        // ── Landing ──────────────────────────────────────────────
        float landingExitSlope = guidanceSlopeTan * _rng.RandfRange(0.6f, 1.0f);
        float landingDrop = (currentSlope + landingExitSlope) / 2f * landingLength;

        compound.Sections.Add(new SubSection
        {
            Type = SubSectionType.Landing,
            LocalStartX = currentX,
            LocalEndX = currentX + landingLength,
            Length = landingLength,
            EntryY = currentY,
            ExitY = currentY + landingDrop,
            EntrySlope = currentSlope,
            ExitSlope = landingExitSlope,
            Terrain = terrainSequence[0],
            Drop = landingDrop,
            Rise = 0f,
            Periods = 0,
            Difficulty = 1
        });

        currentX += landingLength;
        currentY += landingDrop;
        currentSlope = landingExitSlope;

        // ── Interior sub-sections ────────────────────────────────
        int terrainIdx = 0;
        for (int i = 0; i < interiorCount; i++)
        {
            float secLength = interiorLengths[i];
            var secType = interiorTypes[i];

            // Advance terrain index on TerrainTransition
            if (secType == SubSectionType.TerrainTransition && terrainIdx + 1 < terrainSequence.Count)
                terrainIdx++;

            var terrain = terrainSequence[Mathf.Min(terrainIdx, terrainSequence.Count - 1)];

            var secParams = ComputeSubSectionParams(secType, secLength, currentSlope,
                                                      guidanceSlopeTan, distance);

            // Clamp exit slope so it doesn't diverge too far from entry slope
            // (prevents jarring gradient changes between adjacent sections)
            float clampedExitSlope = Mathf.Clamp(secParams.ExitSlope,
                currentSlope * 0.4f, currentSlope * 2.0f);
            clampedExitSlope = Mathf.Max(clampedExitSlope, 0.05f); // always at least slightly downhill

            compound.Sections.Add(new SubSection
            {
                Type = secType,
                LocalStartX = currentX,
                LocalEndX = currentX + secLength,
                Length = secLength,
                EntryY = currentY,
                ExitY = currentY + secParams.Drop,
                EntrySlope = currentSlope,
                ExitSlope = clampedExitSlope,
                Terrain = terrain,
                Drop = secParams.Drop,
                Rise = secParams.Rise,
                Periods = secParams.Periods,
                Difficulty = secParams.Difficulty
            });

            currentX += secLength;
            currentY += secParams.Drop;
            currentSlope = clampedExitSlope;
        }

        // ── Exit Ramp ────────────────────────────────────────────
        float rampRise = _rng.RandfRange(300f, 700f);
        float rampExitSlope = _rng.RandfRange(-0.35f, -0.55f); // upward = negative dy/dx

        compound.Sections.Add(new SubSection
        {
            Type = SubSectionType.ExitRamp,
            LocalStartX = currentX,
            LocalEndX = currentX + rampLength,
            Length = rampLength,
            EntryY = currentY,
            ExitY = currentY - rampRise,
            EntrySlope = currentSlope,
            ExitSlope = rampExitSlope,
            Terrain = terrainSequence[^1],
            Drop = -rampRise,
            Rise = rampRise,
            Periods = 0,
            Difficulty = 1
        });

        // ── Finalize compound module ─────────────────────────────
        compound.TotalLength = currentX + rampLength;

        // Gap width based on total descent (net downhill before ramp)
        float netDescent = (currentY - entryY) + rampRise;
        compound.GapWidth = ComputeGapWidth(netDescent, compound.TotalLength, distance);
        compound.Difficulty = _difficulty.GetMaxDifficulty(distance);
        compound.HasTerrainTransition = terrainSequence.Count > 1;

        return compound;
    }

    // ── Compound Module Helpers ───────────────────────────────────

    private struct SectionParams
    {
        public float Drop;
        public float Rise;
        public float ExitSlope;
        public int Periods;
        public int Difficulty;
    }

    private SectionParams ComputeSubSectionParams(
        SubSectionType type, float length, float entrySlope,
        float guidanceSlopeTan, float distance)
    {
        int maxDiff = _difficulty.GetMaxDifficulty(distance);

        return type switch
        {
            SubSectionType.RollingHills => new SectionParams
            {
                Drop = length * guidanceSlopeTan * _rng.RandfRange(0.6f, 1.0f),
                Rise = ClampRiseForSlope(length, _rng.RandfRange(40f, 80f),
                    Mathf.Max(2, (int)(length / 3000f)), 0.20f),
                ExitSlope = guidanceSlopeTan * _rng.RandfRange(0.8f, 1.1f),
                Periods = Mathf.Max(2, (int)(length / 3000f)),
                Difficulty = Mathf.Min(2, maxDiff)
            },

            SubSectionType.RockGarden => new SectionParams
            {
                Drop = length * guidanceSlopeTan * _rng.RandfRange(0.7f, 1.0f),
                Rise = ClampRiseForSlope(length, _rng.RandfRange(15f, 35f),
                    Mathf.Max(3, (int)(length / 800f)), 0.18f),
                ExitSlope = guidanceSlopeTan * _rng.RandfRange(0.8f, 1.1f),
                Periods = Mathf.Max(3, (int)(length / 800f)),
                Difficulty = Mathf.Min(3, maxDiff)
            },

            SubSectionType.LongCruise => new SectionParams
            {
                Drop = length * guidanceSlopeTan * _rng.RandfRange(0.8f, 1.2f),
                Rise = 0f,
                ExitSlope = guidanceSlopeTan,
                Periods = 0,
                Difficulty = 1
            },

            SubSectionType.MogulField => new SectionParams
            {
                Drop = length * guidanceSlopeTan * _rng.RandfRange(0.6f, 0.9f),
                Rise = ClampRiseForSlope(length, _rng.RandfRange(20f, 50f),
                    Mathf.Max(4, (int)(length / 500f)), 0.15f),
                ExitSlope = guidanceSlopeTan * _rng.RandfRange(0.8f, 1.0f),
                Periods = Mathf.Max(4, (int)(length / 500f)),
                Difficulty = Mathf.Min(3, maxDiff)
            },

            SubSectionType.TerrainTransition => new SectionParams
            {
                Drop = length * guidanceSlopeTan * _rng.RandfRange(0.6f, 1.0f),
                Rise = 0f,
                ExitSlope = guidanceSlopeTan * _rng.RandfRange(0.7f, 1.0f),
                Periods = 0,
                Difficulty = 1
            },

            SubSectionType.SteepChute => new SectionParams
            {
                Drop = length * guidanceSlopeTan * _rng.RandfRange(1.2f, 1.8f),
                Rise = 0f,
                ExitSlope = guidanceSlopeTan * _rng.RandfRange(1.0f, 1.4f),
                Periods = 0,
                Difficulty = Mathf.Min(4, maxDiff)
            },

            SubSectionType.PowderField => new SectionParams
            {
                Drop = length * guidanceSlopeTan * _rng.RandfRange(0.5f, 0.8f),
                Rise = ClampRiseForSlope(length, _rng.RandfRange(15f, 40f), 4, 0.12f),
                ExitSlope = guidanceSlopeTan * _rng.RandfRange(0.7f, 1.0f),
                Periods = 4,
                Difficulty = 1
            },

            _ => new SectionParams
            {
                Drop = length * guidanceSlopeTan,
                Rise = 0f,
                ExitSlope = guidanceSlopeTan,
                Periods = 0,
                Difficulty = 1
            }
        };
    }

    private List<SubSectionType> PickInteriorTypes(
        int count, float distance, List<TerrainType> terrainSeq)
    {
        var types = new List<SubSectionType>();
        bool needsTransition = terrainSeq.Count > 1;
        int transitionsPlaced = 0;
        int transitionsNeeded = terrainSeq.Count - 1;

        // Weights for interior types (difficulty-dependent)
        var weights = new Dictionary<SubSectionType, float>
        {
            { SubSectionType.RollingHills, 1.5f },
            { SubSectionType.LongCruise, 1.2f },
            { SubSectionType.MogulField, 0.8f },
            { SubSectionType.PowderField, 0.7f },
            { SubSectionType.RockGarden, 0.6f },
            { SubSectionType.SteepChute, 0.4f }
        };

        // Filter by difficulty
        int maxDiff = _difficulty.GetMaxDifficulty(distance);
        if (maxDiff < 3) weights.Remove(SubSectionType.RockGarden);
        if (maxDiff < 4) weights.Remove(SubSectionType.SteepChute);

        for (int i = 0; i < count; i++)
        {
            // Place terrain transitions at appropriate positions
            if (needsTransition && transitionsPlaced < transitionsNeeded)
            {
                float idealPos = (float)(transitionsPlaced + 1) / (transitionsNeeded + 1) * count;
                if (i >= (int)idealPos)
                {
                    types.Add(SubSectionType.TerrainTransition);
                    transitionsPlaced++;
                    continue;
                }
            }

            types.Add(WeightedPickType(weights));
        }

        return types;
    }

    private SubSectionType WeightedPickType(Dictionary<SubSectionType, float> weights)
    {
        float total = 0f;
        foreach (var w in weights)
            total += w.Value;

        float roll = _rng.Randf() * total;
        float cumulative = 0f;

        foreach (var w in weights)
        {
            cumulative += w.Value;
            if (roll <= cumulative)
                return w.Key;
        }

        // Fallback
        return SubSectionType.LongCruise;
    }

    /// <summary>
    /// Clamps Rise so the max slope contribution from the sine oscillation
    /// stays under maxSlopeContribution. Formula: Rise * 2π * Periods / Length.
    /// </summary>
    private static float ClampRiseForSlope(float length, float desiredRise, int periods, float maxSlopeContribution)
    {
        float maxRise = maxSlopeContribution * length / (Mathf.Pi * 2f * Mathf.Max(1, periods));
        return Mathf.Min(desiredRise, maxRise);
    }

    private float[] DivideBudget(float totalLength, int count)
    {
        var lengths = new float[count];
        float remaining = totalLength;

        for (int i = 0; i < count - 1; i++)
        {
            float avgPart = remaining / (count - i);
            float minPart = Mathf.Max(avgPart * 0.5f, 1500f);
            float maxPart = Mathf.Min(avgPart * 1.5f, 6000f);
            lengths[i] = _rng.RandfRange(minPart, maxPart);
            remaining -= lengths[i];
        }
        lengths[count - 1] = Mathf.Max(remaining, 1500f);

        return lengths;
    }
}
