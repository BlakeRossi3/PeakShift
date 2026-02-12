using System;
using System.Collections.Generic;
using Godot;

namespace PeakShift.Obstacles;

/// <summary>
/// Object pool for obstacle instances (Rock, Tree, Log, etc.).
/// Manages separate pools for each obstacle type.
/// </summary>
public class ObstaclePool
{
    private readonly Dictionary<Type, Stack<ObstacleBase>> _pools = new();
    private readonly Node2D _parent;
    private readonly Dictionary<Type, int> _totalCreated = new();

    /// <summary>Total obstacles created across all types.</summary>
    public int TotalCreated
    {
        get
        {
            int total = 0;
            foreach (var count in _totalCreated.Values)
                total += count;
            return total;
        }
    }

    /// <summary>Total obstacles currently available across all pools.</summary>
    public int Available
    {
        get
        {
            int total = 0;
            foreach (var pool in _pools.Values)
                total += pool.Count;
            return total;
        }
    }

    public ObstaclePool(Node2D parent, int prewarmCountPerType = 5)
    {
        _parent = parent;

        // Initialize pools for each obstacle type
        RegisterType<Rock>(prewarmCountPerType);
        RegisterType<Tree>(prewarmCountPerType);
        RegisterType<Log>(prewarmCountPerType);
    }

    private void RegisterType<T>(int prewarmCount) where T : ObstacleBase, new()
    {
        var type = typeof(T);
        _pools[type] = new Stack<ObstacleBase>();
        _totalCreated[type] = 0;

        // Pre-warm pool
        for (int i = 0; i < prewarmCount; i++)
        {
            var obstacle = CreateObstacle<T>();
            obstacle.Visible = false;
            obstacle.Position = new Vector2(-10000, -10000);
            _pools[type].Push(obstacle);
        }
    }

    /// <summary>
    /// Acquire an obstacle by type name ("Rock", "Tree", "Log").
    /// </summary>
    public ObstacleBase Acquire(string typeName)
    {
        return typeName switch
        {
            "Rock" => Acquire<Rock>(),
            "Tree" => Acquire<Tree>(),
            "Log" => Acquire<Log>(),
            _ => null
        };
    }

    /// <summary>
    /// Acquire an obstacle of a specific type from the pool.
    /// </summary>
    public T Acquire<T>() where T : ObstacleBase, new()
    {
        var type = typeof(T);

        if (!_pools.ContainsKey(type))
        {
            GD.PrintErr($"[ObstaclePool] Type {type.Name} not registered!");
            return null;
        }

        ObstacleBase obstacle;

        if (_pools[type].Count > 0)
        {
            obstacle = _pools[type].Pop();
        }
        else
        {
            obstacle = CreateObstacle<T>();
        }

        return obstacle as T;
    }

    /// <summary>
    /// Return an obstacle to the pool for reuse.
    /// </summary>
    public void Release(ObstacleBase obstacle)
    {
        if (obstacle == null) return;

        var type = obstacle.GetType();

        if (!_pools.ContainsKey(type))
        {
            GD.PrintErr($"[ObstaclePool] Cannot release unknown type {type.Name}");
            obstacle.QueueFree();
            return;
        }

        // Deactivate and hide
        obstacle.Deactivate();
        obstacle.Position = new Vector2(-10000, -10000);
        _pools[type].Push(obstacle);
    }

    private T CreateObstacle<T>() where T : ObstacleBase, new()
    {
        var type = typeof(T);
        var obstacle = new T();
        _parent.AddChild(obstacle);

        if (_totalCreated.ContainsKey(type))
            _totalCreated[type]++;
        else
            _totalCreated[type] = 1;

        return obstacle;
    }

    /// <summary>
    /// Get available count for a specific obstacle type.
    /// </summary>
    public int GetAvailableCount(string typeName)
    {
        var type = typeName switch
        {
            "Rock" => typeof(Rock),
            "Tree" => typeof(Tree),
            "Log" => typeof(Log),
            _ => null
        };

        if (type != null && _pools.ContainsKey(type))
            return _pools[type].Count;

        return 0;
    }
}
