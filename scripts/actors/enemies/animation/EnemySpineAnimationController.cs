using System;
using Godot;

namespace Kuros.Actors.Enemies.Animation
{
    public enum SpineAnimationPlaybackMode
    {
        Loop,
        Once
    }

    /// <summary>
    /// 敌人 Spine 动画控制基础模板，使用 GDScript Helper 绕过 C# GDExtension 绑定问题。
    /// </summary>
    public abstract partial class EnemySpineAnimationController : Node
    {
        [Export] public NodePath SpineSpritePath { get; set; } = new("SpineSprite");
        [Export(PropertyHint.Range, "0,4,1")] public int TrackIndex { get; set; } = 0;
        [Export(PropertyHint.Range, "0,4,1")] public int QueueTrackIndex { get; set; } = 0;
        [Export] public string DefaultLoopAnimation { get; set; } = string.Empty;

        protected SampleEnemy? Enemy { get; private set; }
        
        // GDScript Helper
        private Node _spineHelper = null!;

        public override void _Ready()
        {
            SetProcess(true);
            base._Ready();
            Enemy = Owner as SampleEnemy ?? GetParent() as SampleEnemy ?? GetNodeOrNull<SampleEnemy>("..");
            
            // Load helper
            var spineScript = GD.Load<GDScript>("res://scripts/utils/SpineWrapper.gd");
            if (spineScript != null)
            {
                _spineHelper = (Node)spineScript.New();
                AddChild(_spineHelper);
            }
            
            OnControllerReady();
        }

        /// <summary>
        /// 供子类覆写的初始化钩子。
        /// </summary>
        protected virtual void OnControllerReady()
        {
            if (!string.IsNullOrEmpty(DefaultLoopAnimation))
            {
                PlayLoop(DefaultLoopAnimation);
            }
        }

        /// <summary>
        /// 为了保持 API 兼容性保留此方法，但实际上不再需要手动刷新引用，因为每次调用 helper 都会重新查找。
        /// </summary>
        protected bool RefreshSpineSpriteReference()
        {
            return true; 
        }

        protected bool PlayLoop(string animationName, float mixDuration = 0.1f, float timeScale = 1f)
        {
            return PlayInternal(animationName, SpineAnimationPlaybackMode.Loop, mixDuration, timeScale);
        }

        protected bool PlayOnce(string animationName, float mixDuration = 0.1f, float timeScale = 1f, string? followUpAnimation = null)
        {
            if (!PlayInternal(animationName, SpineAnimationPlaybackMode.Once, mixDuration, timeScale))
            {
                return false;
            }

            var fallback = followUpAnimation ?? DefaultLoopAnimation;
            if (!string.IsNullOrEmpty(fallback))
            {
                QueueAnimation(fallback, SpineAnimationPlaybackMode.Loop, 0f);
            }

            return true;
        }

        protected bool QueueAnimation(string animationName, SpineAnimationPlaybackMode mode, float delaySeconds = 0f, float mixDuration = 0.1f, float timeScale = 1f)
        {
            if (string.IsNullOrEmpty(animationName) || _spineHelper == null)
            {
                return false;
            }

            // Call GDScript helper: add_animation(root, anim_name, loop, delay, mix_duration, time_scale)
            // Pass 'Owner' or 'Enemy' as the root to search for SpineSprite
            Node targetRoot = Owner ?? (Node?)Enemy ?? this;
            
            try
            {
                var result = _spineHelper.Call("add_animation", targetRoot, animationName, mode == SpineAnimationPlaybackMode.Loop, delaySeconds, mixDuration, timeScale);
                return result.AsBool();
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[{Name}] QueueAnimation Failed: {ex.Message}");
                return false;
            }
        }

        protected bool PlayEmpty(float mixDuration = 0.1f)
        {
            if (_spineHelper == null) return false;
            
            Node targetRoot = Owner ?? (Node?)Enemy ?? this;
            try
            {
                var result = _spineHelper.Call("set_empty_animation", targetRoot, TrackIndex, mixDuration);
                return result.AsBool();
            }
            catch
            {
                return false;
            }
        }

        private bool PlayInternal(string animationName, SpineAnimationPlaybackMode mode, float mixDuration, float timeScale)
        {
            if (string.IsNullOrEmpty(animationName) || _spineHelper == null)
            {
                return false;
            }

            Node targetRoot = Owner ?? (Node?)Enemy ?? this;
            
            try
            {
                // Call GDScript helper: play_animation(root, anim_name, loop, mix_duration, time_scale)
                var result = _spineHelper.Call("play_animation", targetRoot, animationName, mode == SpineAnimationPlaybackMode.Loop, mixDuration, timeScale);
                return result.AsBool();
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[{Name}] PlayInternal Failed: {ex.Message}");
                return false;
            }
        }
    }
}
