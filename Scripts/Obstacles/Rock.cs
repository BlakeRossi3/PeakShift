using Godot;

namespace PeakShift.Obstacles;

/// <summary>
/// Rock obstacle - hard collision, instant crash.
/// Visual: circular collision shape with colored polygon.
/// Appears on all terrain types.
/// </summary>
public partial class Rock : ObstacleBase
{
    private Polygon2D _visual;
    private CollisionShape2D _collision;

    [Export]
    public float Radius { get; set; } = 32f;

    protected override void OnInitialize()
    {
        Behavior = CollisionType.Hard;
        AllowedTerrains = new[] { TerrainType.Snow, TerrainType.Dirt, TerrainType.Ice };

        // Set size based on radius
        if (Radius < 24f)
            Size = SizeCategory.Small;
        else if (Radius < 48f)
            Size = SizeCategory.Medium;
        else
            Size = SizeCategory.Large;

        CreateVisual();
    }

    private void CreateVisual()
    {
        // Create circular collision shape
        var circle = new CircleShape2D { Radius = Radius };
        _collision = new CollisionShape2D { Shape = circle };
        AddChild(_collision);

        // Create rock visual (octagon approximation)
        var points = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.Pi / 4f + Mathf.Pi / 8f; // Offset for variety
            float radiusVariation = Radius * GD.Randf() * 0.15f; // Slight random variation
            float r = Radius + radiusVariation;
            points[i] = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        _visual = new Polygon2D
        {
            Polygon = points,
            Color = new Color(0.4f, 0.4f, 0.45f) // Gray rock
        };
        AddChild(_visual);
    }

    protected override void OnActivate(TerrainType terrain)
    {
        // Adjust color based on terrain
        if (_visual != null)
        {
            _visual.Color = terrain switch
            {
                TerrainType.Snow => new Color(0.5f, 0.5f, 0.55f), // Lighter gray on snow
                TerrainType.Dirt => new Color(0.35f, 0.3f, 0.25f), // Brown-gray on dirt
                TerrainType.Ice => new Color(0.6f, 0.65f, 0.7f),   // Blue-gray on ice
                _ => new Color(0.4f, 0.4f, 0.45f)
            };
        }
    }

    protected override void OnPlayerHit(PlayerController player)
    {
        // Optional: particle effect, sound, etc.
        GD.Print($"[Rock] Player hit rock at {GlobalPosition}");
    }
}
