using System.Collections.Generic;
using Godot;

namespace PeakShift.Terrain;

/// <summary>
/// Object pool for terrain chunk Node2D instances (StaticBody2D).
/// Avoids constant allocation/deallocation of scene nodes during scrolling.
/// </summary>
public class ModulePool
{
    private readonly Stack<StaticBody2D> _available = new();
    private readonly Node2D _parent;
    private int _totalCreated;

    /// <summary>Number of nodes currently available in the pool.</summary>
    public int Available => _available.Count;

    /// <summary>Total nodes ever created by this pool.</summary>
    public int TotalCreated => _totalCreated;

    public ModulePool(Node2D parent, int prewarmCount = 0)
    {
        _parent = parent;

        for (int i = 0; i < prewarmCount; i++)
        {
            var body = CreateBody();
            body.Visible = false;
            _available.Push(body);
        }
    }

    /// <summary>
    /// Get a StaticBody2D from the pool (or create a new one).
    /// The body is added as a child of the parent node, visible, and at ZIndex -1.
    /// All existing children (polygons, lines, collisions) are cleared.
    /// </summary>
    public StaticBody2D Acquire()
    {
        StaticBody2D body;

        if (_available.Count > 0)
        {
            body = _available.Pop();
            // Clear any leftover children from previous use
            foreach (var child in body.GetChildren())
            {
                if (child is Node node)
                    node.QueueFree();
            }
            body.Visible = true;
        }
        else
        {
            body = CreateBody();
        }

        return body;
    }

    /// <summary>
    /// Return a StaticBody2D to the pool for reuse.
    /// Hides it and clears children.
    /// </summary>
    public void Release(StaticBody2D body)
    {
        if (body == null) return;

        foreach (var child in body.GetChildren())
        {
            if (child is Node node)
                node.QueueFree();
        }

        body.Visible = false;
        body.Position = new Vector2(-10000, -10000);
        _available.Push(body);
    }

    private StaticBody2D CreateBody()
    {
        var body = new StaticBody2D
        {
            ZIndex = -1
        };
        _parent.AddChild(body);
        _totalCreated++;
        return body;
    }
}
