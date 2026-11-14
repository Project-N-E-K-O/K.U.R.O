using Godot;
using System;
using System.Collections.Generic;
namespace Kuros.Environments;

/// <summary>对象唯一的地面坐标与查询入口。注册表仅按目标查找，不扫描全场；远处通过 Gate 休眠。</summary>
public partial class GroundContactSensor : Node
{
    private static readonly Dictionary<ulong, GroundContactSensor> Sensors = new();
    private Node2D _target = null!;
    private Func<Vector2> _position = null!;
    private FallDetectionGate _gate = null!;
    private ulong _id, _frame = ulong.MaxValue;
    private Vector2 _lastPosition;
    private bool _includeActivation;
    private FallPhysics.GroundResult _result;
    public bool Active => IsInstanceValid(_gate) && _gate.Active;
    public Vector2 GroundPosition => _position();

    public void Configure(Node2D target, Func<Vector2> position, Action<bool> changed, bool includeActivation = false)
    {
        _target = target; _position = position; _includeActivation = includeActivation;
        _gate = new FallDetectionGate { Name = "DetectionGate" };
        _gate.Configure(target, position, changed);
    }
    public override void _Ready()
    {
        _id = _target.GetInstanceId(); Sensors[_id] = this;
        AddChild(_gate);
        SetPhysicsProcess(false);
    }
    public FallPhysics.GroundResult SampleNow()
    {
        var point = GroundPosition;
        ulong frame = Engine.GetPhysicsFrames();
        if (_frame != frame || point != _lastPosition)
        {
            _result = FallPhysics.Query(_target.GetWorld2D(), point, _includeActivation);
            _frame = frame; _lastPosition = point;
        }
        return _result;
    }
    public void Refresh()
    {
        _frame = ulong.MaxValue;
        if (IsInstanceValid(_gate) && _gate.IsInsideTree()) _gate.RequestRefresh();
    }
    public static GroundContactSensor? For(Node target)
        => Sensors.TryGetValue(target.GetInstanceId(), out var sensor) && IsInstanceValid(sensor) && sensor.IsInsideTree() ? sensor : null;
    public override void _ExitTree()
    { if (Sensors.TryGetValue(_id, out var current) && current == this) Sensors.Remove(_id); }
}
