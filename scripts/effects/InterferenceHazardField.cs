using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Fx;

namespace Kuros.Effects
{
	public enum HazardSpawnDirection
	{
		/// <summary>世界坐标正下方（+Y）。</summary>
		Below,
		/// <summary>世界坐标正上方（-Y）。</summary>
		Above,
		/// <summary>沿攻击者朝向的左右方向（原行为）。</summary>
		AttackerFacing,
	}

	/// <summary>
	/// 干扰区散布器：由攻击特效生成一次，在自身前方的矩形内随机撒若干个
	/// <see cref="InterferenceZone"/>（世界层兄弟节点，自管理生命周期），随后自毁。
	/// 攻击来源通过 <see cref="IAttackerProvider"/> 注入给每个干扰区。
	/// </summary>
	[GlobalClass]
	public partial class InterferenceHazardField : Node2D, IAttackerProvider
	{
		[ExportCategory("Spawn 生成")]
		[Export(PropertyHint.Range, "1,12,1")] public int ZoneCount { get; set; } = 4;
		/// <summary>散布方向：Below/Above = 世界坐标正下/正上；AttackerFacing = 沿攻击者朝向左右。</summary>
		[Export] public HazardSpawnDirection SpawnDirection { get; set; } = HazardSpawnDirection.Below;
		/// <summary>矩形起点距本节点的纵深偏移（像素）：沿散布方向，避免 zone 刷在脚下。</summary>
		[Export(PropertyHint.Range, "0,3000,10")] public float ForwardOffset { get; set; } = 250f;
		/// <summary>矩形纵深（像素）：沿散布方向展开的长度。</summary>
		[Export(PropertyHint.Range, "100,3000,10")] public float SpawnLength { get; set; } = 1000f;
		/// <summary>矩形横向宽度（像素）：垂直于散布方向。</summary>
		[Export(PropertyHint.Range, "100,3000,10")] public float SpawnWidth { get; set; } = 700f;
		[Export(PropertyHint.Range, "40,600,5")] public float MinZoneRadius { get; set; } = 120f;
		[Export(PropertyHint.Range, "40,600,5")] public float MaxZoneRadius { get; set; } = 200f;
		[Export] public PackedScene? ZoneScene { get; set; }

		[ExportCategory("Damage 伤害")]
		[Export(PropertyHint.Range, "0,200,1")] public int Damage { get; set; } = 20;

		[ExportCategory("Debug 调试")]
		/// <summary>在生成点绘制前方矩形的散布范围与实际落点（仅调试用）。</summary>
		[Export] public bool ShowSpawnDebug { get; set; } = false;
		/// <summary>调试图形保留时长（秒）；开启时生成器节点延后释放。</summary>
		[Export(PropertyHint.Range, "0.5,10,0.1")] public float SpawnDebugDuration { get; set; } = 2f;

		public GameActor? Attacker { get; set; }

		private bool _spawned;
		private float _debugRemaining;
		private Vector2 _debugForwardAxis = Vector2.Down;
		private Vector2 _debugLateralAxis = Vector2.Right;
		private readonly List<Vector2> _debugZoneOffsets = new();

		// 首物理帧再生成：AttackEffectEntry 是 AddChild → 再摆 GlobalPosition，
		// _Ready 里读到的是默认坐标（同 ShockwaveController 的生成时序）。
		public override void _PhysicsProcess(double delta)
		{
			if (_spawned) return;
			_spawned = true;
			SpawnZones();

			if (!ShowSpawnDebug)
			{
				QueueFree();
				return;
			}

			ZIndex = 100;
			ZAsRelative = false;
			_debugRemaining = Mathf.Max(SpawnDebugDuration, 0.1f);
			QueueRedraw();
		}

		public override void _Process(double delta)
		{
			if (_debugRemaining <= 0f) return;
			_debugRemaining -= (float)delta;
			if (_debugRemaining <= 0f)
				QueueFree();
		}

		public override void _Draw()
		{
			if (!_spawned || !ShowSpawnDebug) return;

			float forwardStart = Mathf.Max(ForwardOffset, 0f);
			float length = Mathf.Max(SpawnLength, 0f);
			float halfWidth = Mathf.Max(SpawnWidth, 0f) * 0.5f;

			Vector2 forward = _debugForwardAxis;
			Vector2 lateral = _debugLateralAxis;
			Vector2 a = forward * forwardStart - lateral * halfWidth;
			Vector2 b = forward * (forwardStart + length) - lateral * halfWidth;
			Vector2 c = forward * (forwardStart + length) + lateral * halfWidth;
			Vector2 d = forward * forwardStart + lateral * halfWidth;

			var fill = new Color(1f, 0.2f, 0.8f, 0.12f);
			var line = new Color(1f, 0.3f, 0.85f, 0.95f);

			DrawColoredPolygon(new[] { a, b, c, d }, fill);
			DrawPolyline(new[] { a, b, c, d, a }, line, 2f);

			// 方向箭头（从原点指向矩形起始边）
			Vector2 arrowBase = forward * (forwardStart + 40f);
			Vector2 arrowSide = lateral * 9f;
			DrawLine(Vector2.Zero, arrowBase, line, 1.5f);
			DrawColoredPolygon(new[]
			{
				forward * (forwardStart + 56f),
				arrowBase - arrowSide,
				arrowBase + arrowSide,
			}, line);

			// 实际落点
			foreach (Vector2 offset in _debugZoneOffsets)
			{
				DrawCircle(offset, 7f, new Color(1f, 1f, 0f, 0.9f));
				DrawCircle(offset, 7f, new Color(0f, 0f, 0f, 0.6f), false, 1.5f);
			}

			var font = ThemeDB.FallbackFont;
			string dirLabel = SpawnDirection == HazardSpawnDirection.AttackerFacing
				? $"Facing({(forward.X > 0f ? "R" : "L")})"
				: SpawnDirection.ToString();
			string text = $"hazard spawn {length:0}x{halfWidth * 2f:0}  offset={forwardStart:0}  dir={dirLabel}";
			DrawString(font, a - forward * 8f, text, HorizontalAlignment.Left, -1, 14, line);
		}

		private void SpawnZones()
		{
			if (ZoneScene == null)
			{
				GD.PushWarning($"[{nameof(InterferenceHazardField)}] 未配置 ZoneScene，无法散布干扰区");
				return;
			}

			Node? parent = GetParent();
			if (parent == null)
			{
				GD.PushWarning($"[{nameof(InterferenceHazardField)}] 没有父节点，无法散布干扰区");
				return;
			}

			float minRadius = Mathf.Min(MinZoneRadius, MaxZoneRadius);
			float maxRadius = Mathf.Max(MinZoneRadius, MaxZoneRadius);
			int count = Mathf.Max(ZoneCount, 1);

			// 矩形沿散布方向展开、横向居中；AttackerFacing 模式面朝左时镜像到 -X。
			Vector2 forwardAxis;
			Vector2 lateralAxis;
			switch (SpawnDirection)
			{
				case HazardSpawnDirection.Above:
					forwardAxis = Vector2.Up;
					lateralAxis = Vector2.Right;
					break;
				case HazardSpawnDirection.AttackerFacing:
					forwardAxis = (Attacker?.FacingRight ?? true) ? Vector2.Right : Vector2.Left;
					lateralAxis = Vector2.Down;
					break;
				default:
					forwardAxis = Vector2.Down;
					lateralAxis = Vector2.Right;
					break;
			}
			_debugForwardAxis = forwardAxis;
			_debugLateralAxis = lateralAxis;

			float forwardStart = Mathf.Max(ForwardOffset, 0f);
			float length = Mathf.Max(SpawnLength, 0f);
			float halfWidth = Mathf.Max(SpawnWidth, 0f) * 0.5f;

			for (int i = 0; i < count; i++)
			{
				var zone = ZoneScene.Instantiate<InterferenceZone>();
				if (zone == null) continue;

				zone.Attacker = Attacker;
				zone.Damage = Damage;
				zone.Radius = (float)GD.RandRange(minRadius, maxRadius);

				float forward = forwardStart + GD.Randf() * length;
				float lateral = (float)GD.RandRange(-halfWidth, halfWidth);
				Vector2 offset = forwardAxis * forward + lateralAxis * lateral;

				parent.AddChild(zone);
				zone.GlobalPosition = GlobalPosition + offset;
				if (ShowSpawnDebug)
					_debugZoneOffsets.Add(offset);
			}
		}
	}
}
