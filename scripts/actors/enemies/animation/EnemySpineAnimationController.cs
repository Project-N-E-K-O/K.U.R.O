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
    /// 敌人 Spine 动画控制基础模板，负责统一处理 SpineSprite 的查找与播放。
    /// 继承该类即可专注于具体敌人的动画切换规则。
    /// </summary>
    public abstract partial class EnemySpineAnimationController : Node
    {
        [Export] public NodePath SpineSpritePath { get; set; } = new("SpineSprite");
        [Export(PropertyHint.Range, "0,4,1")] public int TrackIndex { get; set; } = 0;
        [Export(PropertyHint.Range, "0,4,1")] public int QueueTrackIndex { get; set; } = 0;
        [Export] public string DefaultLoopAnimation { get; set; } = string.Empty;

        protected SampleEnemy? Enemy { get; private set; }
        protected Node? SpineSprite { get; private set; }
        protected GodotObject? AnimationState => _animationState;

        private GodotObject? _animationState;

        public override void _Ready()
        {
            SetProcess(true);
            base._Ready();
            Enemy = Owner as SampleEnemy ?? GetParent() as SampleEnemy ?? GetNodeOrNull<SampleEnemy>("..");
            RefreshSpineSpriteReference();
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
        /// 重新查找 SpineSprite，并刷新 AnimationState。
        /// </summary>
        protected bool RefreshSpineSpriteReference()
        {
            var resolvedSprite = ResolveSpineSprite();
            SpineSprite = resolvedSprite;
            _animationState = ResolveAnimationState(SpineSprite);

            return SpineSprite != null || _animationState != null;
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
            if (string.IsNullOrEmpty(animationName))
            {
                return false;
            }

            if (!EnsureAnimationInterface())
            {
                return false;
            }

            if (!TryCallAddAnimation(animationName, mode == SpineAnimationPlaybackMode.Loop, delaySeconds, mixDuration, timeScale))
            {
                GD.PushWarning($"[{Name}] 无法队列 Spine 动画 '{animationName}'。");
                return false;
            }

            return true;
        }

        protected bool PlayEmpty(float mixDuration = 0.1f)
        {
            if (!EnsureAnimationInterface())
            {
                return false;
            }

            if (TryCallEmptyAnimation(mixDuration))
            {
                return true;
            }

            GD.PushWarning($"[{Name}] 无法播放空动画。");
            return false;
        }

        private bool PlayInternal(string animationName, SpineAnimationPlaybackMode mode, float mixDuration, float timeScale)
        {
            if (string.IsNullOrEmpty(animationName))
            {
                return false;
            }

            if (!EnsureAnimationInterface())
            {
                return false;
            }

            if (!TryCallSetAnimation(animationName, mode == SpineAnimationPlaybackMode.Loop, mixDuration, timeScale))
            {
                GD.PushWarning($"[{Name}] 无法播放 Spine 动画 '{animationName}'。");
                return false;
            }

            return true;
        }

        private void ApplyTrackEntrySettings(Variant entryVariant, float mixDuration, float timeScale)
        {
            if (entryVariant.VariantType != Variant.Type.Object)
            {
                return;
            }

            var entry = entryVariant.AsGodotObject();
            entry?.Set("mix_duration", mixDuration);
            entry?.Set("time_scale", timeScale);
        }

        private Node? ResolveSpineSprite()
        {
            Node? sprite = null;
            if (!SpineSpritePath.IsEmpty)
            {
                sprite = GetNodeOrNull<Node>(SpineSpritePath);
                if (sprite == null && Owner is Node ownerNode)
                {
                    sprite = ownerNode.GetNodeOrNull<Node>(SpineSpritePath);
                }
            }

            sprite ??= GetNodeOrNull<Node>("SpineSprite");
            if (sprite == null && Owner is Node owner)
            {
                sprite = owner.GetNodeOrNull<Node>("SpineSprite");
            }

            return sprite;
        }

        private GodotObject? ResolveAnimationState(Node? sprite)
        {
            if (sprite == null)
            {
                return null;
            }

            try
            {
                if (sprite.HasMethod("get_animation_state"))
                {
                    var result = sprite.Call("get_animation_state");
                    return result.VariantType == Variant.Type.Nil ? null : result.AsGodotObject();
                }

                Variant stateVariant = sprite.Get("animation_state");
                return stateVariant.VariantType == Variant.Type.Nil ? null : stateVariant.AsGodotObject();
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[{Name}] 无法获取 Spine animation_state: {ex.Message}");
                return null;
            }
        }

        private bool EnsureAnimationInterface()
        {
            if (SpineSprite != null)
            {
                if (_animationState == null)
                {
                    _animationState = ResolveAnimationState(SpineSprite);
                }

                return true;
            }

            return RefreshSpineSpriteReference();
        }

        private bool TryCallSetAnimation(string animationName, bool loop, float mixDuration, float timeScale)
        {
            if (_animationState != null && _animationState.HasMethod("set_animation"))
            {
                try
                {
                    Variant result = _animationState.Call("set_animation", animationName, loop);
                    ApplyTrackEntrySettings(result, mixDuration, timeScale);
                    return true;
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"[{Name}] 调用 animation_state.set_animation 失败: {ex.Message}");
                }
            }

            if (SpineSprite != null && SpineSprite.HasMethod("set_animation"))
            {
                try
                {
                    Variant result = SpineSprite.Call("set_animation", animationName, loop);
                    ApplyTrackEntrySettings(result, mixDuration, timeScale);
                    return true;
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"[{Name}] 调用 SpineSprite.set_animation 失败: {ex.Message}");
                }
            }

            return false;
        }

        private bool TryCallAddAnimation(string animationName, bool loop, float delaySeconds, float mixDuration, float timeScale)
        {
            if (_animationState != null && _animationState.HasMethod("add_animation"))
            {
                try
                {
                    Variant result = _animationState.Call("add_animation", animationName, loop, delaySeconds);
                    ApplyTrackEntrySettings(result, mixDuration, timeScale);
                    return true;
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"[{Name}] 调用 animation_state.add_animation 失败: {ex.Message}");
                }
            }

            if (SpineSprite != null && SpineSprite.HasMethod("add_animation"))
            {
                try
                {
                    Variant result = SpineSprite.Call("add_animation", animationName, loop, delaySeconds);
                    ApplyTrackEntrySettings(result, mixDuration, timeScale);
                    return true;
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"[{Name}] 调用 SpineSprite.add_animation 失败: {ex.Message}");
                }
            }

            return false;
        }

        private bool TryCallEmptyAnimation(float mixDuration)
        {
            if (_animationState != null && _animationState.HasMethod("set_empty_animation"))
            {
                try
                {
                    _animationState.Call("set_empty_animation", mixDuration);
                    return true;
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"[{Name}] 调用 animation_state.set_empty_animation 失败: {ex.Message}");
                }
            }

            if (SpineSprite != null && SpineSprite.HasMethod("set_empty_animation"))
            {
                try
                {
                    SpineSprite.Call("set_empty_animation", mixDuration);
                    return true;
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"[{Name}] 调用 SpineSprite.set_empty_animation 失败: {ex.Message}");
                }
            }

            return false;
        }
    }
}


