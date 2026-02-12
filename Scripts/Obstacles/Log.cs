using Godot;

namespace PeakShift.Obstacles;

/// <summary>
/// Log obstacle - fallen tree trunk across the path.
/// Can be jumped over or causes slowdown if hit while grounded.
/// Visual: horizontal capsule/rectangle.
/// </summary>
public partial class Log : ObstacleBase
{
    private Polygon2D _visual;
    private CollisionShape2D _collision;

    [Export]
    public float Length { get; set; } = 80f;

    [Export]
    public float Diameter { get; set; } = 20f;

    protected override void OnInitialize()
    {
        Behavior = CollisionType.Jump;
        AllowedTerrains = new[] { TerrainType.Snow, TerrainType.Dirt };
        Size = SizeCategory.Medium;

        CreateVisual();
    }

    private void CreateVisual()
    {
        // Create capsule collision (horizontal)
        var capsule = new CapsuleShape2D
        {
            Radius = Diameter / 2f,
            Height = Length
        };
        _collision = new CollisionShape2D
        {
            Shape = capsule,
            Rotation = Mathf.Pi / 2f // Rotate to horizontal
        };
        AddChild(_collision);

        // Create log visual (rounded rectangle)
        var halfLen = Length / 2f;
        var halfDiam = Diameter / 2f;
        var points = new Vector2[]
        {
            new Vector2(-halfLen, -halfDiam),
            new Vector2(halfLen, -halfDiam),
            new Vector2(halfLen, halfDiam),
            new Vector2(-halfLen, halfDiam)
        };
        _visual = new Polygon2D
        {
            Polygon = points,
            Color = new Color(0.4f, 0.3f, 0.2f) // Brown
        };
        AddChild(_visual);

        // Add some visual detail lines (bark texture)
        for (int i = 0; i < 3; i++)
        {
            float x = -halfLen + (Length / 4f) * (i + 1);
            var line = new Line2D();
            line.AddPoint(new Vector2(x, -halfDiam));
            line.AddPoint(new Vector2(x, halfDiam));
            line.Width = 2f;
            line.DefaultColor = new Color(0.3f, 0.2f, 0.1f);
            AddChild(line);
        }
    }

    protected override void OnActivate(TerrainType terrain)
    {
        // Slight color variation based on terrain
        if (_visual != null)
        {
            _visual.Color = terrain switch
            {
                TerrainType.Snow => new Color(0.45f, 0.35f, 0.25f), // Lighter brown on snow
                TerrainType.Dirt => new Color(0.35f, 0.25f, 0.15f), // Darker brown on dirt
                _ => new Color(0.4f, 0.3f, 0.2f)
            };
        }
    }

    protected override void OnPlayerHit(PlayerController player)
    {
        GD.Print($"[Log] Player hit log at {GlobalPosition} while {player.CurrentMoveState}");
    }
}
