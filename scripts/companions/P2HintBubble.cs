using Godot;
using System.Collections.Generic;

namespace Kuros.Companions
{
    /// <summary>
    /// Non-blocking world-space hint bubble for P2 companion.
    /// Draws simple text above the companion without pausing or intercepting input.
    /// </summary>
    public partial class P2HintBubble : Node2D
    {
        [ExportCategory("Display")]
        [Export] public Vector2 BubbleOffset { get; set; } = new(0f, -500f);
        [Export(PropertyHint.Range, "10,256,1")] public int FontSize { get; set; } = 126;
        [Export(PropertyHint.Range, "0.2,8,0.1")] public float VisibleSeconds { get; set; } = 2.2f;
        [Export(PropertyHint.Range, "0.05,2,0.05")] public float FadeSeconds { get; set; } = 0.35f;
        [Export(PropertyHint.Range, "0,20,0.1")] public float MinIntervalSeconds { get; set; } = 0.35f;

        [ExportCategory("Style")]
        [Export] public Color BubbleColor { get; set; } = new(0.08f, 0.1f, 0.15f, 0.82f);
        [Export] public Color BorderColor { get; set; } = new(0.96f, 0.98f, 1f, 0.9f);
        [Export] public Color TextColor { get; set; } = new(1f, 1f, 1f, 1f);
        [Export] public Vector2 Padding { get; set; } = new(84f, 54f);
        [Export] public int MaxQueueSize { get; set; } = 6;

        private readonly Queue<string> _queue = new();
        private string _currentText = string.Empty;
        private float _remaining;
        private float _cooldown;
        private float _alpha = 0f;

        public bool IsShowing => !string.IsNullOrEmpty(_currentText) && _remaining > 0f;
        public string CurrentMessage => _currentText;
        public int QueueCount => _queue.Count;

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            if (_cooldown > 0f)
            {
                _cooldown = Mathf.Max(0f, _cooldown - dt);
            }

            if (IsShowing)
            {
                _remaining = Mathf.Max(0f, _remaining - dt);
                if (_remaining <= FadeSeconds)
                {
                    _alpha = FadeSeconds > 0.0001f ? _remaining / FadeSeconds : 0f;
                }
                else
                {
                    _alpha = 1f;
                }

                QueueRedraw();
                return;
            }

            _alpha = 0f;
            if (_queue.Count > 0 && _cooldown <= 0f)
            {
                StartShow(_queue.Dequeue());
            }

            QueueRedraw();
        }

        public void EnqueueHint(string text)
        {
            string safe = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(safe))
            {
                return;
            }

            if (!IsShowing && _cooldown <= 0f)
            {
                StartShow(safe);
                return;
            }

            if (_queue.Count >= Mathf.Max(1, MaxQueueSize))
            {
                return;
            }

            _queue.Enqueue(safe);
        }

        public override void _Draw()
        {
            if (!IsShowing || _alpha <= 0f)
            {
                return;
            }

            Font font = ThemeDB.FallbackFont;
            if (font == null)
            {
                return;
            }

            Vector2 textSize = font.GetStringSize(_currentText, HorizontalAlignment.Left, -1, FontSize);
            Vector2 size = textSize + new Vector2(Padding.X * 2f, Padding.Y * 2f);
            Vector2 topLeft = BubbleOffset - new Vector2(size.X * 0.5f, size.Y);
            Rect2 bubbleRect = new(topLeft, size);

            Color fill = BubbleColor;
            fill.A *= _alpha;
            DrawRect(bubbleRect, fill, true);

            Color border = BorderColor;
            border.A *= _alpha;
            DrawRect(bubbleRect, border, false, 2f);

            Vector2 textPos = new(topLeft.X + Padding.X, topLeft.Y + Padding.Y + textSize.Y);
            Color text = TextColor;
            text.A *= _alpha;
            DrawString(font, textPos, _currentText, HorizontalAlignment.Left, -1, FontSize, text);
        }

        private void StartShow(string text)
        {
            _currentText = text;
            _remaining = Mathf.Max(0.2f, VisibleSeconds);
            _alpha = 1f;
            _cooldown = Mathf.Max(0f, MinIntervalSeconds);
            QueueRedraw();
        }
    }
}
