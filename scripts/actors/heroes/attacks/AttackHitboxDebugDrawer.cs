using System;
using Godot;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// Runtime debug drawer used to visualize the active attack hitbox shape.
    /// </summary>
    [Tool]
    public partial class AttackHitboxDebugDrawer : Node2D
    {
        private Shape2D? _shape;
        private Color _color = Colors.Red;
        private float _lineWidth = 2f;
        private float _remainingDuration;
        private Resource? _subscribedPreviewSkill;

        [ExportGroup("Editor Preview")]
        [Export] public bool EnableEditorPreview { get; set; } = false;
        [Export] public Resource? PreviewSkill { get; set; }
        [Export] public NodePath PreviewAttackAreaPath { get; set; } = new NodePath("../AttackArea");
        [Export] public NodePath PreviewCollisionShapePath { get; set; } = new NodePath("../AttackArea/CollisionShape2D");

        public override void _Ready()
        {
            ZAsRelative = false;
            ShowBehindParent = false;

            if (Engine.IsEditorHint())
            {
                EnsurePreviewSkillSubscription();
                UpdateEditorPreview();
                SetProcess(true);
                return;
            }

            // Do not force-hide here; ShowFromCollisionShape can be called in the same frame
            // as AddChild, and _Ready may run afterward.
            SetProcess(Visible);
        }

        public override void _ExitTree()
        {
            UnsubscribePreviewSkill();
            base._ExitTree();
        }

        public void ShowFromCollisionShape(CollisionShape2D collisionShape, Color color, float lineWidth, float duration)
        {
            if (collisionShape.Shape == null)
            {
                Hide();
                return;
            }

            _shape = collisionShape.Shape;
            _color = color;
            _lineWidth = MathF.Max(1f, lineWidth);
            _remainingDuration = MathF.Max(0.05f, duration);

            GlobalPosition = collisionShape.GlobalPosition;
            GlobalRotation = collisionShape.GlobalRotation;
            GlobalScale = collisionShape.GlobalScale;

            Visible = true;
            SetProcess(true);
            QueueRedraw();
        }

        public override void _Process(double delta)
        {
            if (Engine.IsEditorHint())
            {
                EnsurePreviewSkillSubscription();
                UpdateEditorPreview();
                return;
            }

            if (!Visible)
            {
                SetProcess(false);
                return;
            }

            _remainingDuration -= (float)delta;
            if (_remainingDuration <= 0f)
            {
                Visible = false;
                SetProcess(false);
            }
        }

        private void EnsurePreviewSkillSubscription()
        {
            if (_subscribedPreviewSkill == PreviewSkill)
            {
                return;
            }

            UnsubscribePreviewSkill();

            if (PreviewSkill == null)
            {
                return;
            }

            var changedCallable = new Callable(this, MethodName.OnPreviewSkillChanged);
            if (!PreviewSkill.IsConnected(Resource.SignalName.Changed, changedCallable))
            {
                PreviewSkill.Changed += OnPreviewSkillChanged;
            }
            _subscribedPreviewSkill = PreviewSkill;
        }

        private void UnsubscribePreviewSkill()
        {
            if (_subscribedPreviewSkill == null)
            {
                return;
            }

            var changedCallable = new Callable(this, MethodName.OnPreviewSkillChanged);
            if (_subscribedPreviewSkill.IsConnected(Resource.SignalName.Changed, changedCallable))
            {
                _subscribedPreviewSkill.Changed -= OnPreviewSkillChanged;
            }
            _subscribedPreviewSkill = null;
        }

        private void OnPreviewSkillChanged()
        {
            if (!Engine.IsEditorHint())
            {
                return;
            }

            UpdateEditorPreview();
            QueueRedraw();
        }

        private void UpdateEditorPreview()
        {
            if (!EnableEditorPreview || PreviewSkill == null)
            {
                Visible = false;
                return;
            }

            if (!TryReadPreviewDebugConfig(PreviewSkill, out var config) || !config.ShowHitboxDebug)
            {
                Visible = false;
                return;
            }

            var attackArea = GetNodeOrNull<Area2D>(PreviewAttackAreaPath);
            if (attackArea == null)
            {
                Visible = false;
                return;
            }

            var previewCollision = ResolvePreviewCollisionShape(attackArea);
            if (previewCollision == null || previewCollision.Shape == null)
            {
                Visible = false;
                return;
            }

            _shape = previewCollision.Shape;

            _color = config.HitboxDebugColor;
            _lineWidth = MathF.Max(1f, config.HitboxDebugLineWidth);

            GlobalPosition = previewCollision.GlobalPosition;
            GlobalRotation = previewCollision.GlobalRotation;
            GlobalScale = previewCollision.GlobalScale;

            Visible = true;
            QueueRedraw();
        }

        private CollisionShape2D? ResolvePreviewCollisionShape(Area2D attackArea)
        {
            if (!PreviewCollisionShapePath.IsEmpty)
            {
                var explicitShape = GetNodeOrNull<CollisionShape2D>(PreviewCollisionShapePath);
                if (explicitShape != null)
                {
                    return explicitShape;
                }
            }

            var direct = attackArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (direct != null)
            {
                return direct;
            }

            foreach (Node child in attackArea.GetChildren())
            {
                if (child is CollisionShape2D collision)
                {
                    return collision;
                }
            }

            return null;
        }

        private static bool TryReadPreviewDebugConfig(Resource resource, out PreviewDebugConfig config)
        {
            config = new PreviewDebugConfig();

            try
            {
                config.ShowHitboxDebug = resource.Get("ShowHitboxDebug").AsBool();
                config.HitboxDebugColor = resource.Get("HitboxDebugColor").AsColor();
                config.HitboxDebugLineWidth = resource.Get("HitboxDebugLineWidth").AsSingle();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private struct PreviewDebugConfig
        {
            public bool ShowHitboxDebug;
            public Color HitboxDebugColor;
            public float HitboxDebugLineWidth;
        }

        public override void _Draw()
        {
            if (!Visible || _shape == null)
            {
                return;
            }

            switch (_shape)
            {
                case RectangleShape2D rect:
                    DrawRectangleShape(rect);
                    break;
                case CircleShape2D circle:
                    DrawCircleShape(circle);
                    break;
                case CapsuleShape2D capsule:
                    DrawCapsuleShape(capsule);
                    break;
            }
        }

        private void DrawRectangleShape(RectangleShape2D rect)
        {
            var drawRect = new Rect2(-rect.Size * 0.5f, rect.Size);
            DrawRect(drawRect, new Color(_color.R, _color.G, _color.B, 0.18f), true);
            DrawRect(drawRect, _color, false, _lineWidth);
        }

        private void DrawCircleShape(CircleShape2D circle)
        {
            DrawCircle(Vector2.Zero, circle.Radius, new Color(_color.R, _color.G, _color.B, 0.18f));
            DrawArc(Vector2.Zero, circle.Radius, 0f, Mathf.Tau, 48, _color, _lineWidth);
        }

        private void DrawCapsuleShape(CapsuleShape2D capsule)
        {
            float radius = capsule.Radius;
            float halfHeight = capsule.Height * 0.5f;
            float sideHalf = Mathf.Max(0f, halfHeight - radius);

            var bodyRect = new Rect2(new Vector2(-radius, -sideHalf), new Vector2(radius * 2f, sideHalf * 2f));
            DrawRect(bodyRect, new Color(_color.R, _color.G, _color.B, 0.18f), true);
            DrawRect(bodyRect, _color, false, _lineWidth);

            Vector2 topCenter = new Vector2(0f, -sideHalf);
            Vector2 bottomCenter = new Vector2(0f, sideHalf);
            DrawCircle(topCenter, radius, new Color(_color.R, _color.G, _color.B, 0.18f));
            DrawCircle(bottomCenter, radius, new Color(_color.R, _color.G, _color.B, 0.18f));
            DrawArc(topCenter, radius, Mathf.Pi, Mathf.Tau, 24, _color, _lineWidth);
            DrawArc(bottomCenter, radius, 0f, Mathf.Pi, 24, _color, _lineWidth);
        }
    }
}
