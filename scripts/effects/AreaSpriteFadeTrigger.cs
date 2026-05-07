using System.Collections.Generic;
using Godot;

namespace Kuros.Effects
{
    /// <summary>
    /// 当指定对象进入触发区域时，降低目标 Sprite/CanvasItem 的透明度；离开后恢复。
    /// 可挂在任意节点上，通过导出的 AreaPath 与 TargetPath 进行复用。
    /// </summary>
    [GlobalClass]
    public partial class AreaSpriteFadeTrigger : Node
    {
        [Export] public NodePath TriggerAreaPath { get; set; } = new("");
        [Export] public NodePath TargetPath { get; set; } = new("");
        [Export] public string[] ActorGroupNames { get; set; } = { "player" };
        [Export(PropertyHint.Range, "0,1,0.01")] public float FadedAlpha { get; set; } = 0.35f;
        [Export(PropertyHint.Range, "0,2,0.01")] public float FadeDuration { get; set; } = 0.15f;

        private Area2D? _triggerArea;
        private CanvasItem? _targetItem;
        private Color _baseModulate = Colors.White;
        private readonly HashSet<ulong> _trackedBodies = new();
        private Tween? _fadeTween;

        public override void _Ready()
        {
            base._Ready();
            ResolveNodes();
            ConnectSignals();
            ApplyAlpha(_baseModulate.A);
        }

        public override void _ExitTree()
        {
            if (_triggerArea != null)
            {
                _triggerArea.BodyEntered -= OnBodyEntered;
                _triggerArea.BodyExited -= OnBodyExited;
            }

            _fadeTween?.Kill();
            base._ExitTree();
        }

        private void ResolveNodes()
        {
            if (!TriggerAreaPath.IsEmpty)
            {
                _triggerArea = GetNodeOrNull<Area2D>(TriggerAreaPath);
            }

            if (_triggerArea == null && GetParent() is Area2D parentArea)
            {
                _triggerArea = parentArea;
            }

            _targetItem = !TargetPath.IsEmpty
                ? GetNodeOrNull<CanvasItem>(TargetPath)
                : null;

            _targetItem ??= GetParent() as CanvasItem;

            if (_targetItem != null)
            {
                _baseModulate = _targetItem.Modulate;
            }
        }

        private void ConnectSignals()
        {
            if (_triggerArea == null)
            {
                GD.PushWarning($"[{nameof(AreaSpriteFadeTrigger)}] 未找到触发 Area2D: {GetPath()}");
                return;
            }

            _triggerArea.BodyEntered -= OnBodyEntered;
            _triggerArea.BodyExited -= OnBodyExited;
            _triggerArea.BodyEntered += OnBodyEntered;
            _triggerArea.BodyExited += OnBodyExited;
        }

        private void OnBodyEntered(Node2D body)
        {
            if (!IsTargetBody(body))
            {
                return;
            }

            _trackedBodies.Add(body.GetInstanceId());
            UpdateFadeState();
        }

        private void OnBodyExited(Node2D body)
        {
            if (!IsTargetBody(body))
            {
                return;
            }

            _trackedBodies.Remove(body.GetInstanceId());
            UpdateFadeState();
        }

        private bool IsTargetBody(Node2D body)
        {
            if (body == null) return false;

            // 没有配置任何group，所有body都触发
            if (ActorGroupNames == null || ActorGroupNames.Length == 0) return true;

            // 任意一个group匹配即触发
            foreach (var group in ActorGroupNames)
            {
                if (!string.IsNullOrWhiteSpace(group) && body.IsInGroup(group))
                    return true;
            }

            return false;
        }

        private void UpdateFadeState()
        {
            float targetAlpha = _trackedBodies.Count > 0
                ? Mathf.Clamp(FadedAlpha, 0f, 1f)
                : _baseModulate.A;

            ApplyAlpha(targetAlpha);
        }

        private void ApplyAlpha(float alpha)
        {
            if (_targetItem == null || !GodotObject.IsInstanceValid(_targetItem))
            {
                return;
            }

            _fadeTween?.Kill();

            if (FadeDuration <= 0f)
            {
                var color = _targetItem.Modulate;
                color.A = alpha;
                _targetItem.Modulate = color;
                return;
            }

            _fadeTween = CreateTween();
            _fadeTween.TweenProperty(_targetItem, "modulate:a", alpha, FadeDuration);
        }
    }
}
