using Godot;

namespace PeakShift.Obstacles;

/// <summary>
/// Tree obstacle - can be jumped over, crashes if hit while grounded.
/// Visual: vertical rectangle for trunk with triangle for foliage.
/// Appears primarily on snow and dirt terrain.
/// </summary>
public partial class Tree : ObstacleBase
{
    private Polygon2D _trunk;
    private Polygon2D _foliage;
    private CollisionShape2D _collision;

    [Export]
    public float Height { get; set; } = 120f;

    [Export]
    public float TrunkWidth { get; set; } = 16f;

    protected override void OnInitialize()
    {
        Behavior = CollisionType.Jump;
        AllowedTerrains = new[] { TerrainType.Snow, TerrainType.Dirt };
        Size = SizeCategory.Large;

        CreateVisual();
    }

    private void CreateVisual()
    {
        // Trunk collision (rectangle at base)
        var rect = new RectangleShape2D
        {
            Size = new Vector2(TrunkWidth, Height * 0.4f)
        };
        _collision = new CollisionShape2D
        {
            Shape = rect,
            Position = new Vector2(0, -Height * 0.2f)
        };
        AddChild(_collision);

        // Trunk visual (brown rectangle)
        var trunkPoints = new Vector2[]
        {
            new Vector2(-TrunkWidth / 2, 0),
            new Vector2(TrunkWidth / 2, 0),
            new Vector2(TrunkWidth / 2, -Height * 0.6f),
            new Vector2(-TrunkWidth / 2, -Height * 0.6f)
        };
        _trunk = new Polygon2D
        {
            Polygon = trunkPoints,
            Color = new Color(0.35f, 0.25f, 0.15f) // Brown
        };
        AddChild(_trunk);

        // Foliage visual (triangle)
        float foliageWidth = Height * 0.6f;
        var foliagePoints = new Vector2[]
        {
            new Vector2(0, -Height),                         // Top
            new Vector2(-foliageWidth / 2, -Height * 0.5f),  // Bottom left
            new Vector2(foliageWidth / 2, -Height * 0.5f)    // Bottom right
        };
        _foliage = new Polygon2D
        {
            Polygon = foliagePoints,
            Color = new Color(0.2f, 0.5f, 0.2f) // Green
        };
        AddChild(_foliage);
    }

    protected override void OnActivate(TerrainType terrain)
    {
        // Adjust foliage color based on terrain
        if (_foliage != null)
        {
            _foliage.Color = terrain switch
            {
                TerrainType.Snow => new Color(0.15f, 0.45f, 0.25f), // Darker green on snow
                TerrainType.Dirt => new Color(0.25f, 0.55f, 0.2f),  // Brighter green on dirt
                _ => new Color(0.2f, 0.5f, 0.2f)
            };
        }
    }

    protected override void OnPlayerHit(PlayerController player)
    {
        GD.Print($"[Tree] Player hit tree at {GlobalPosition} while {player.CurrentMoveState}");
    }
}
