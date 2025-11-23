using Godot;

namespace Kuros.Controllers
{
    /// <summary>
    /// 编辑器辅助节点：展示敌人出生点与朝向。
    /// 运行时会自动移除。
    /// </summary>
    [Tool]
    public partial class EnemySpawnMarker : Node2D
    {
        [Export] public PackedScene PreviewScene { get; set; } = null!;
        [Export] public string LabelText = "Enemy Spawn";
        [Export] public Color LabelColor = new Color(1, 1, 1, 0.85f);
        [Export] public bool ShowPreview = true;
        [Export] public int MarkerZIndex = 1000;

        private Node2D? _previewInstance;
        private Label? _label;

        public override void _Ready()
        {
            if (!Engine.IsEditorHint())
            {
                QueueFree();
                return;
            }

            ZIndex = MarkerZIndex;

            if (ShowPreview && PreviewScene != null)
            {
                var preview = PreviewScene.Instantiate<Node2D>();
                if (preview != null)
                {
                    preview.Name = "Preview";
                    preview.ProcessMode = ProcessModeEnum.Disabled;
                    preview.ZIndex = -5;
                    _previewInstance = preview;
                    AddChild(preview);
                }
            }

            _label = new Label
            {
                Name = "SpawnLabel",
                Text = LabelText,
                Modulate = LabelColor,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Position = new Vector2(0, -40),
                RotationDegrees = 0
            };
            AddChild(_label);
        }

        public override void _ExitTree()
        {
            _previewInstance = null;
            _label = null;
        }

        public override void _Process(double delta)
        {
            if (!Engine.IsEditorHint()) return;
            ZIndex = MarkerZIndex;
            if (_label != null)
            {
                _label.Text = LabelText;
                _label.Modulate = LabelColor;
            }
        }
    }
}

