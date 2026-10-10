using System;
using Godot;
using Kuros.Actors.Enemies.States;

namespace Kuros.Actors.Enemies.Animation
{
	public enum SpineAnimationPlaybackMode
	{
		Loop,
		Once,
		PartialLoop,
		PartialOnce
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
		[Export(PropertyHint.Range, "0,1,0.01")] public float IdleMixDuration = 0.05f;
		[Export(PropertyHint.Range, "0,1,0.01")] public float WalkMixDuration = 0.05f;
		[Export(PropertyHint.Range, "0,1,0.01")] public float HitMixDuration = 0.05f;
		[Export(PropertyHint.Range, "0,1,0.01")] public float DieMixDuration = 0.05f;
		[Export(PropertyHint.Range, "0,1,0.01")] public float AttackMixDuration = 0.5f;
		[Export(PropertyHint.Range, "0,1,0.01")] public float SkillMixDuration = 0.5f;

		protected SampleEnemy? Enemy { get; private set; }

		// GDScript Helper
		private Node _spineHelper = null!;
		protected string _currentKey = string.Empty;
		protected SpineAnimationPlaybackMode _currentMode = SpineAnimationPlaybackMode.Loop;
		protected float _activeLoopStart;
		protected float _activeLoopEnd;

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

		// ── 受击动画管线（由 EnemyHitState 双时间轴驱动；子类 case "Hit" 调 DriveHitPhaseAnimation）──

		private EnemyHitState.HitPhase _lastHitPhase;
		private int _lastHitPhaseTick;
		private bool _animHoldApplied;

		/// <summary>
		/// 受击动画驱动（完整播放 hit + time_scale 停帧/续播——与玩家 Hit 同机制，不依赖 partial）：
		///   Impact：重入/阶段变化时从头完整播放；位移未完（NeedsAnimHold）→ 停帧在受击段末帧
		///   Recover：解除定格续播回正段
		/// </summary>
		protected void DriveHitPhaseAnimation(string hitAnimation, float hitMixDuration)
		{
			if (Enemy?.StateMachine?.CurrentState is not EnemyHitState hitState)
			{
				return;
			}

			bool reentered = hitState.PhaseTick != _lastHitPhaseTick;

			if (hitState.CurrentPhase == EnemyHitState.HitPhase.Impact)
			{
				if (reentered || _lastHitPhase != EnemyHitState.HitPhase.Impact)
				{
					// 受击段从头完整播放（强制重播——连击重入时 PlayOnceIfNeeded 的 key 去重会跳过）
					PlayOnceForced("Hit", hitAnimation, hitMixDuration);
					_animHoldApplied = false;
				}

				// 位移未完 → 动画播完受击段后定格；到位/无位移 → 动画正常播
				if (hitState.NeedsAnimHold && !_animHoldApplied)
				{
					SetSpineTimeScale(0f);
					_animHoldApplied = true;
				}
				else if (!hitState.NeedsAnimHold && _animHoldApplied)
				{
					SetSpineTimeScale(1f);
					_animHoldApplied = false;
				}
			}
			else if (hitState.CurrentPhase == EnemyHitState.HitPhase.Recover)
			{
				// 回正：解除定格，续播剩余回正段（动画此刻在受击段末帧，无缝续播）
				if (_lastHitPhase != EnemyHitState.HitPhase.Recover || reentered)
				{
					SetSpineTimeScale(1f);
					_animHoldApplied = false;
				}
			}

			_lastHitPhase = hitState.CurrentPhase;
			_lastHitPhaseTick = hitState.PhaseTick;
		}

		/// <summary>
		/// 供子类覆写的初始化钩子。
		/// </summary>
		protected virtual void OnControllerReady()
		{
			if (!string.IsNullOrEmpty(DefaultLoopAnimation))
			{
				PlayLoop(DefaultLoopAnimation, GetPreferredMixDuration());
			}
		}

		/// <summary>
		/// 子类可覆写该方法，统一提供当前控制器期望的默认混合时长。
		/// </summary>
		protected virtual float GetPreferredMixDuration()
		{
			return 0.5f;
		}

		/// <summary>
		/// 为了保持 API 兼容性保留此方法，但实际上不再需要手动刷新引用，因为每次调用 helper 都会重新查找。
		/// </summary>
		protected bool RefreshSpineSpriteReference()
		{
			return true;
		}

		protected bool PlayLoop(string animationName, float mixDuration = 0.5f, float timeScale = 1f)
		{
			return PlayInternal(animationName, SpineAnimationPlaybackMode.Loop, mixDuration, timeScale);
		}

		protected bool PlayOnce(string animationName, float mixDuration = 0.5f, float timeScale = 1f, string? followUpAnimation = null)
		{
			if (!PlayInternal(animationName, SpineAnimationPlaybackMode.Once, mixDuration, timeScale))
			{
				return false;
			}

			var fallback = followUpAnimation ?? DefaultLoopAnimation;
			if (!string.IsNullOrEmpty(fallback))
			{
				QueueAnimation(fallback, SpineAnimationPlaybackMode.Loop, 0f, GetPreferredMixDuration());
			}

			return true;
		}

		protected bool QueueAnimation(string animationName, SpineAnimationPlaybackMode mode, float delaySeconds = 0f, float mixDuration = 0.5f, float timeScale = 1f)
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

		protected bool PlayEmpty(float mixDuration = 0.5f)
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

		protected bool PlayPartialLoop(string animationName, float loopStart, float loopEnd, float mixDuration = 0.5f, float timeScale = 1f)
		{
			if (string.IsNullOrEmpty(animationName) || _spineHelper == null || loopEnd <= loopStart)
			{
				return false;
			}

			Node targetRoot = Owner ?? (Node?)Enemy ?? this;
			try
			{
				var result = _spineHelper.Call("play_partial_loop_animation", targetRoot, animationName, loopStart, loopEnd, mixDuration, timeScale);
				return result.AsBool();
			}
			catch (Exception ex)
			{
				GD.PushWarning($"[{Name}] PlayPartialLoop Failed: {ex.Message}");
				return false;
			}
		}

		protected bool PlayPartialOnce(string animationName, float partStart, float partEnd, float mixDuration = 0.5f, float timeScale = 1f)
		{
			if (string.IsNullOrEmpty(animationName) || _spineHelper == null || partEnd <= partStart)
			{
				return false;
			}

			Node targetRoot = Owner ?? (Node?)Enemy ?? this;
			try
			{
				var result = _spineHelper.Call("play_partial_once_animation", targetRoot, animationName, partStart, partEnd, mixDuration, timeScale);
				return result.AsBool();
			}
			catch (Exception ex)
			{
				GD.PushWarning($"[{Name}] PlayPartialOnce Failed: {ex.Message}");
				return false;
			}
		}

		/// <summary>统一设置全部 Spine 节点当前动画的时间缩放（0 = 停帧保持当前帧）。</summary>
		protected void SetSpineTimeScale(float timeScale)
		{
			if (_spineHelper == null) return;
			Node targetRoot = Owner ?? (Node?)Enemy ?? this;
			try
			{
				_spineHelper.Call("change_time_scale_all", targetRoot, timeScale);
			}
			catch (Exception ex)
			{
				GD.PushWarning($"[{Name}] SetSpineTimeScale Failed: {ex.Message}");
			}
		}

		protected bool UpdatePartialLoop(float loopStart, float loopEnd)
		{
			if (_spineHelper == null || loopEnd <= loopStart)
			{
				return false;
			}

			Node targetRoot = Owner ?? (Node?)Enemy ?? this;
			try
			{
				var result = _spineHelper.Call("update_partial_loop_animation", targetRoot, TrackIndex, loopStart, loopEnd);
				return result.AsBool();
			}
			catch (Exception ex)
			{
				GD.PushWarning($"[{Name}] UpdatePartialLoop Failed: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// 强制从头播放一次性动画并登记 key（忽略去重——连击重入时受击动画必须重播；
		/// 播放后 _currentKey=_key，后续 PlayOnceIfNeeded 正常去重不再重播）。
		/// </summary>
		protected bool PlayOnceForced(string key, string animationName, float mixDuration, float timeScale = 1f)
		{
			if (string.IsNullOrEmpty(animationName))
				return false;

			if (PlayOnce(animationName, mixDuration, timeScale, string.Empty))
			{
				_currentKey = key;
				_currentMode = SpineAnimationPlaybackMode.Once;
				return true;
			}
			return false;
		}

		/// <summary>
		/// 循环播放动画，仅在 key 或模式变化时才重新发起播放。
		/// </summary>
		protected void PlayLoopIfNeeded(string key, string animationName, float mixDuration, float timeScale = 1f)
		{
			if (string.IsNullOrEmpty(animationName))
				return;

			if (_currentKey == key && _currentMode == SpineAnimationPlaybackMode.Loop)
				return;

			if (PlayLoop(animationName, mixDuration, timeScale))
			{
				_currentKey = key;
				_currentMode = SpineAnimationPlaybackMode.Loop;
			}
		}

		/// <summary>
		/// 一次性播放动画，仅在 key 或模式变化时才重新发起播放。
		/// </summary>
		protected void PlayOnceIfNeeded(string key, string animationName, float mixDuration, float timeScale = 1f)
		{
			if (string.IsNullOrEmpty(animationName))
				return;

			if (_currentKey == key && _currentMode == SpineAnimationPlaybackMode.Once)
				return;

			if (PlayOnce(animationName, mixDuration, timeScale, string.Empty))
			{
				_currentKey = key;
				_currentMode = SpineAnimationPlaybackMode.Once;
			}
		}

		/// <summary>
		/// 循环播放动画的指定片段，仅在 key 或片段参数变化时才重新发起播放。
		/// </summary>
		protected void PlayPartLoopIfNeeded(string key, string animationName, float loopStart, float loopEnd, float mixDuration, float timeScale = 1f)
		{
			if (string.IsNullOrEmpty(animationName))
				return;

			if (loopEnd <= loopStart)
			{
				PlayLoopIfNeeded(key, animationName, mixDuration, timeScale);
				return;
			}

			bool samePartialLoop = _currentKey == key
				&& _currentMode == SpineAnimationPlaybackMode.PartialLoop
				&& Mathf.IsEqualApprox(_activeLoopStart, loopStart)
				&& Mathf.IsEqualApprox(_activeLoopEnd, loopEnd);

			if (samePartialLoop)
				return;

			if (PlayPartialLoop(animationName, loopStart, loopEnd, mixDuration, timeScale))
			{
				_currentKey = key;
				_currentMode = SpineAnimationPlaybackMode.PartialLoop;
				_activeLoopStart = loopStart;
				_activeLoopEnd = loopEnd;
			}
		}

		/// <summary>
		/// 一次性播放动画的指定片段，仅在 key 或片段参数变化时才重新发起播放。
		/// </summary>
		protected void PlayPartOnceIfNeeded(string key, string animationName, float partStart, float partEnd, float mixDuration, float timeScale = 1f)
		{
			if (string.IsNullOrEmpty(animationName))
				return;

			if (partEnd <= partStart)
			{
				PlayOnceIfNeeded(key, animationName, mixDuration, timeScale);
				return;
			}

			bool samePartialOnce = _currentKey == key
				&& _currentMode == SpineAnimationPlaybackMode.PartialOnce
				&& Mathf.IsEqualApprox(_activeLoopStart, partStart)
				&& Mathf.IsEqualApprox(_activeLoopEnd, partEnd);

			if (samePartialOnce)
				return;

			if (PlayPartialOnce(animationName, partStart, partEnd, mixDuration, timeScale))
			{
				_currentKey = key;
				_currentMode = SpineAnimationPlaybackMode.PartialOnce;
				_activeLoopStart = partStart;
				_activeLoopEnd = partEnd;
			}
		}

		/// <summary>
		/// 播放动画的指定片段一次，段播完后**姿态停留在段尾帧**（不回弹到段首）——收招/起身类动作段用。
		/// </summary>
		protected bool PlayPartialOnceHoldEnd(string animationName, float partStart, float partEnd, float mixDuration = 0.5f, float timeScale = 1f)
		{
			if (string.IsNullOrEmpty(animationName) || _spineHelper == null || partEnd <= partStart)
			{
				return false;
			}

			Node targetRoot = Owner ?? (Node?)Enemy ?? this;
			try
			{
				var result = _spineHelper.Call("play_partial_once_animation_hold_end", targetRoot, animationName, partStart, partEnd, mixDuration, timeScale);
				return result.AsBool();
			}
			catch (Exception ex)
			{
				GD.PushWarning($"[{Name}] PlayPartialOnceHoldEnd Failed: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// 片段一次性播放并定格段尾，仅在 key 或片段参数变化时才重新发起播放（与 PlayPartOnceIfNeeded 同款去重）。
		/// </summary>
		protected void PlayPartOnceHoldEndIfNeeded(string key, string animationName, float partStart, float partEnd, float mixDuration, float timeScale = 1f)
		{
			if (string.IsNullOrEmpty(animationName))
				return;

			if (partEnd <= partStart)
			{
				PlayOnceIfNeeded(key, animationName, mixDuration, timeScale);
				return;
			}

			bool samePartialOnce = _currentKey == key
				&& _currentMode == SpineAnimationPlaybackMode.PartialOnce
				&& Mathf.IsEqualApprox(_activeLoopStart, partStart)
				&& Mathf.IsEqualApprox(_activeLoopEnd, partEnd);

			if (samePartialOnce)
				return;

			if (PlayPartialOnceHoldEnd(animationName, partStart, partEnd, mixDuration, timeScale))
			{
				_currentKey = key;
				_currentMode = SpineAnimationPlaybackMode.PartialOnce;
				_activeLoopStart = partStart;
				_activeLoopEnd = partEnd;
			}
		}

		/// <summary>
		/// 逐帧更新 PartialLoop 片段的循环边界。
		/// </summary>
		protected void TickPartialLoop()
		{
			if (_currentMode != SpineAnimationPlaybackMode.PartialLoop)
				return;

			UpdatePartialLoop(_activeLoopStart, _activeLoopEnd);
		}

		/// <summary>
		/// 播放空动画（用于死亡等不需要 Spine 渲染的状态）。
		/// </summary>
		protected void PlayEmptyIfNeeded()
		{
			if (_currentKey == "Empty")
				return;

			if (PlayEmpty(DieMixDuration))
			{
				_currentKey = "Empty";
				_currentMode = SpineAnimationPlaybackMode.Loop;
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
