using Godot;
using Kuros.Core;
using Kuros.Fx;

namespace Kuros.Effects
{
	/// <summary>
	/// 动画驱动的固定爆炸链：AnimationPlayer 的方法轨道按时间调用
	/// <see cref="SpawnExplosionAt"/>(下标)，在对应 Marker2D 的世界位置实例化导出的爆炸场景。
	/// 落点由 Marker 摆放决定，不随敌人/玩家移动；每发爆炸各自结算伤害与击退。
	/// </summary>
	public partial class ExplosionChainAnimator : Node2D, IAttackerProvider
	{
		/// <summary>每个落点实例化的爆炸场景（视觉与伤害/击退由该场景自己负责）。</summary>
		[Export] public PackedScene? ExplosionScene { get; set; }
		/// <summary>落点预警场景（一般用 LandingIndicatorA）；空 = 不预警。</summary>
		[Export] public PackedScene? IndicatorScene { get; set; }
		/// <summary>预警时长（秒）：与动画里"预警与爆炸的时间差"保持一致（默认 1 秒）。</summary>
		[Export(PropertyHint.Range, "0.1,5,0.05")] public float IndicatorLeadTime { get; set; } = 1f;
		/// <summary>按顺序排列的落点（通常是本节点的子 Marker2D）。</summary>
		[Export] public Marker2D[] Markers { get; set; } = System.Array.Empty<Marker2D>();
		/// <summary>编辑器中播放动画时也生成；关闭时方法轨道在编辑器内被忽略。</summary>
		[Export] public bool PreviewInEditor { get; set; }
		/// <summary>生成后自动播放（作为攻击特效挂载时用）。</summary>
		[Export] public bool AutoPlayOnSpawn { get; set; } = true;
		/// <summary>自动播放的动画名（chainA / chainB，可经 PropertyOverrides 按招式选择）。
		/// <see cref="RandomizeAnimation"/> 开启时忽略此项。</summary>
		[Export] public string AnimationName { get; set; } = "chainA";
		/// <summary>自动播放时从 <see cref="AnimationPool"/> 随机选一条链。</summary>
		[Export] public bool RandomizeAnimation { get; set; }
		/// <summary>随机候选动画名（默认 chainA / chainB）。</summary>
		[Export] public string[] AnimationPool { get; set; } = { "chainA", "chainB" };
		/// <summary>整条链播完后销毁自身（每次攻击生成一次时避免残留）。</summary>
		[Export] public bool QueueFreeAfterPlay { get; set; } = true;

		/// <summary>伤害归属，由攻击模板在 AddChild 前注入。</summary>
		public GameActor? Attacker { get; set; }

		private bool _finishedConnected;

		public override void _Ready()
		{
			// AttackEffectEntry 的生成时序是 AddChild → 再摆 GlobalPosition，
			// 延后到本帧末尾再开始，确保首帧落点已就位。
			if (AutoPlayOnSpawn)
				Callable.From(AutoPlay).CallDeferred();
		}

		private void AutoPlay()
		{
			if (!IsInsideTree() || IsQueuedForDeletion()) return;

			string name = AnimationName;
			if (RandomizeAnimation && AnimationPool != null && AnimationPool.Length > 0)
				name = AnimationPool[GD.Randi() % (uint)AnimationPool.Length];

			Play(name, Attacker);
		}

		/// <summary>由攻击调用：记录伤害归属并按 <see cref="AnimationName"/> 播放爆炸链。</summary>
		public void Play(GameActor? attacker) => Play(AnimationName, attacker);

		/// <summary>播放指定链（chainA / chainB）。</summary>
		public void Play(string animationName, GameActor? attacker)
		{
			Attacker = attacker;
			var player = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
			if (player == null) return;

			string name = string.IsNullOrEmpty(animationName) ? "chainA" : animationName;
			if (!player.HasAnimation(name)) return;

			if (QueueFreeAfterPlay && !_finishedConnected)
			{
				player.AnimationFinished += OnAnimationFinished;
				_finishedConnected = true;
			}
			player.Stop();
			player.Play(name);
		}

		private void OnAnimationFinished(StringName animationName)
		{
			if (QueueFreeAfterPlay) QueueFree();
		}

		/// <summary>AnimationPlayer 方法轨道入口：在落点播放预警（LandingIndicatorA）。
		/// 预警时长取 <see cref="IndicatorLeadTime"/>，应等于动画里"预警 → 爆炸"的间隔。</summary>
		public void SpawnIndicatorAt(int index)
		{
			if (Engine.IsEditorHint() && !PreviewInEditor) return;
			if (IndicatorScene == null || Markers == null || index < 0 || index >= Markers.Length) return;

			var marker = Markers[index];
			if (marker == null || !IsInstanceValid(marker)) return;

			var instantiated = IndicatorScene.Instantiate();
			if (instantiated is not Node2D indicator)
			{
				instantiated.Free();
				return;
			}

			Node parent = GetParent() ?? this;
			parent.AddChild(indicator);
			indicator.TopLevel = true;   // 世界坐标上的一个点，不跟滑槽/敌人移动
			indicator.GlobalPosition = marker.GlobalPosition;

			var landing = indicator as LandingIndicator
				?? indicator.GetNodeOrNull<LandingIndicator>(".");
			if (landing != null)
			{
				landing.WarningDuration = Mathf.Max(0.1f, IndicatorLeadTime);
				landing.Start();
			}

			if (indicator is IAttackerProvider provider)
				provider.Attacker = Attacker;
		}

		/// <summary>AnimationPlayer 方法轨道入口（参数只能是基础 Variant，所以传下标）。</summary>
		public void SpawnExplosionAt(int index)
		{
			if (Engine.IsEditorHint() && !PreviewInEditor) return;
			if (ExplosionScene == null || Markers == null || index < 0 || index >= Markers.Length) return;

			var marker = Markers[index];
			if (marker == null || !IsInstanceValid(marker)) return;

			var instantiated = ExplosionScene.Instantiate();
			if (instantiated is not Node2D visual)
			{
				instantiated.Free();
				return;
			}

			// 挂到父级（世界层），不继承本节点的变换；落点取固定 Marker 的世界坐标。
			Node parent = GetParent() ?? this;
			parent.AddChild(visual);
			visual.GlobalPosition = marker.GlobalPosition;

			// 伤害归属：兼容接口与同族既有属性。
			if (visual is IAttackerProvider provider)
				provider.Attacker = Attacker;
			else if (visual is EffectAutoDestroy autoDestroy)
				autoDestroy.Attacker = Attacker;
			else if (visual is BoomDmgEffect boom)
				boom.Attacker = Attacker;
		}
	}
}
