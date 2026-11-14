using Godot;
using System;
using System.Collections.Generic;

namespace Kuros.Environments;

/// <summary>黄色区域激活精确检测；只在初始化、区域加载或显式传送后补查物理空间。</summary>
public partial class FallDetectionGate : Node
{
    private static readonly Dictionary<ulong, FallDetectionGate> Gates = new();
    private readonly HashSet<FallActivationArea> _areas = new();
    private Node2D _actor = null!;
    private Func<Vector2> _foot = null!;
    private Action<bool> _changed = null!;
    private int _refreshFrames;
    private bool _initialized;
    private bool _fallbackHazard;
    private ulong _actorId;
    private Area2D? _probe;
    public bool Active { get; private set; }

    public void Configure(Node2D actor, Func<Vector2> foot, Action<bool> changed)
    { _actor = actor; _foot = foot; _changed = changed; }

    public override void _Ready()
    {
        ProcessPhysicsPriority = 99;
        _actorId = _actor.GetInstanceId();
        Gates[_actorId] = this;
        CreateProbe();
        RequestRefresh();
    }

    private void CreateProbe()
    {
        if (_probe == null)
        {
            // 只监听黄区，不占用身体层；也适用于内部刚体移动而根节点不动的家具。
            _probe = new Area2D { Name = "ActivationProbe", TopLevel = true, CollisionLayer = 0,
                CollisionMask = FallActivationArea.Layer, Monitorable = false, Position = _foot() };
            _probe.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 1 } });
            _probe.AreaEntered += ProbeEntered;
            _probe.AreaExited += ProbeExited;
            AddChild(_probe);
            _probe.GlobalPosition = _foot();
        }
    }

    /// <summary>等待最终定位与物理注册完成后补查；不是周期轮询。</summary>
    public void RequestRefresh()
    { _refreshFrames = 2; SetPhysicsProcess(true); }

    internal static void AreaAdded(FallActivationArea area)
    {
        foreach (var gate in Gates.Values)
            if (IsInstanceValid(gate) && gate.IsInsideTree() && gate._actor.GetWorld2D() == area.GetWorld2D()) gate.RequestRefresh();
    }

    internal static void AreaRemoved(FallActivationArea area)
    {
        foreach (var gate in Gates.Values)
            if (gate._areas.Remove(area) && gate._initialized) gate.Publish();
    }

    private void Publish()
    {
        _areas.RemoveWhere(area => !IsInstanceValid(area) || !area.IsInsideTree() || area.IsQueuedForDeletion());
        bool active = _areas.Count > 0 || _fallbackHazard;
        if (Active == active && _initialized) return;
        Active = active;
        _changed(active);
    }

    private void ProbeEntered(Area2D area)
    { if (area is FallActivationArea yellow) { _areas.Add(yellow); if (_initialized) Publish(); } }
    private void ProbeExited(Area2D area)
    { if (area is FallActivationArea yellow) { _areas.Remove(yellow); if (_initialized) Publish(); } }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsInstanceValid(_actor) || !_actor.IsInsideTree() || _actor.IsQueuedForDeletion()) return;
        // 所有对象统一只同步脚底坐标；远离黄区时不进行精确空间查询。
        if (_probe != null) _probe.GlobalPosition = _foot();
        if (_refreshFrames > 0) { _refreshFrames--; return; }
        if (_refreshFrames < 0 && !_fallbackHazard) return;
        _refreshFrames = -1;
        _areas.Clear();
        // 首次/传送/新区域加载时补查已有探针重叠，不依赖目标身体碰撞。
        foreach (var area in FallActivationArea.LiveAreas)
            if (IsInstanceValid(area) && area.IsInsideTree() && !area.IsQueuedForDeletion()
                && area.GetWorld2D() == _actor.GetWorld2D()
                && _probe != null && _probe.OverlapsArea(area)) _areas.Add(area);
        bool hazard = false;
        foreach (var hit in _actor.GetWorld2D().DirectSpaceState.IntersectPoint(new PhysicsPointQueryParameters2D
        {
            Position = _foot(), CollisionMask = FallActivationArea.Layer | FallHazardArea.Layer,
            CollideWithAreas = true, CollideWithBodies = false
        }, 128))
        {
            var area = hit["collider"].AsGodotObject();
            if (area is FallActivationArea yellow) _areas.Add(yellow);
            if (area is FallHazardArea red && red.Region != null) hazard = true;
        }
        // 配置遗漏黄色范围时，直接出生在红区仍可触发；仅这种异常位置临时保留补查。
        _fallbackHazard = hazard && _areas.Count == 0;
        bool first = !_initialized;
        if (first) { Active = _areas.Count > 0 || _fallbackHazard; _changed(Active); }
        _initialized = true;
        if (!first) Publish();
        SetPhysicsProcess(_fallbackHazard || _probe != null);
    }

    public override void _ExitTree()
    {
        Gates.Remove(_actorId);
        if (IsInstanceValid(_probe)) { _probe!.AreaEntered -= ProbeEntered; _probe.AreaExited -= ProbeExited; }
        _areas.Clear();
    }
}
