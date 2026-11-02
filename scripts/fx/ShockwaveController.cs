using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
	/// <summary>
	/// 断裂环形冲击波：从生成点向四周扩散，波前接触目标时结算一次伤害与径向击退。
	/// 缺口（Gap）在生成时随机一次、全程固定；C# 逻辑与 Shader 共用同一份缺口数据——
	/// Shader 只负责画，命中判定用数学距离 + 环绕角差，不建逐缺口 Area2D。
	/// **命中判定在 shader 的伪 3D 平面空间里做**：C# 复刻 ring_shockwave.gdshader 的投影公式
	/// （世界坐标 → plane_uv → 归一化半径/角度），视觉与判定共享同一个投影，fov / rot_x / rot_y
	/// 怎么调都对得上；投影参数直接从材质读，材质是唯一参数源。投影不可用时回退世界坐标正圆。
	/// </summary>
	[GlobalClass]
	public partial class ShockwaveController : Node2D, IAttackerProvider
	{
		[ExportCategory("Wave 扩散")]
		[Export(PropertyHint.Range, "50,6000,10")] public float MaxRadius { get; set; } = 600f;
		[Export(PropertyHint.Range, "10,5000,10")] public float ExpandSpeed { get; set; } = 800f;
		[Export(PropertyHint.Range, "0,2,0.01")] public float FadeDuration { get; set; } = 0.35f;

		[ExportCategory("Damage 命中")]
		[Export(PropertyHint.Range, "0,500,1")] public int Damage { get; set; } = 20;
		[Export(PropertyHint.Range, "0,2000,10")] public float KnockbackDistance { get; set; } = 150f;
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration { get; set; } = 0.3f;
		/// <summary>目标近似碰撞半径：波前与目标中心距离的区间判定用（d ± r 与上一帧/当前半径比较）。</summary>
		[Export(PropertyHint.Range, "20,400,1")] public float TargetHitRadius { get; set; } = 120f;
		[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
		public TargetableFactions TargetableFactions { get; set; } = TargetableFactions.Enemy;
		/// <summary>目标发现用的物理层（IntersectShape）：Enemy 层 = 2、Player 层 = 4、两者 = 6（EFFECT_STANDARD §二）。</summary>
		[Export(PropertyHint.Layers2DPhysics)] public uint TargetCollisionMask { get; set; } = 2;
		/// <summary>是否允许命中攻击者自身（默认关闭）。</summary>
		[Export] public bool AllowSelfDamage { get; set; } = false;

		[ExportCategory("Gaps 缺口")]
		[Export(PropertyHint.Range, "0,6,1")] public int GapCount { get; set; } = 4;
		[Export(PropertyHint.Range, "0,45,0.5")] public float MinGapHalfWidthDegrees { get; set; } = 8f;
		[Export(PropertyHint.Range, "0,60,0.5")] public float MaxGapHalfWidthDegrees { get; set; } = 18f;
		/// <summary>逻辑安全角额外放宽量（度）。0 = 与视觉缺口严格对齐（默认）；调大则判定比视觉宽容，
		/// 可能出现"环已完整显示但仍免伤"的边缘错觉。</summary>
		[Export(PropertyHint.Range, "0,10,0.5")] public float GapSafetyMarginDegrees { get; set; } = 0f;

		[ExportCategory("Visual 视觉")]
		[Export] public NodePath VisualSpritePath { get; set; } = new("ShockwaveSprite");
		[Export(PropertyHint.Range, "1,120,0.5")] public float RingThickness { get; set; } = 26f;
		[Export(PropertyHint.Range, "0,60,0.5")] public float EdgeSoftness { get; set; } = 10f;
		[Export(PropertyHint.Range, "0,60,0.5")] public float WaveIrregularity { get; set; } = 8f;
		[Export(PropertyHint.Range, "1,40,0.5")] public float WaveFrequency { get; set; } = 7f;
		/// <summary>调试：绘制逻辑波前圆、缺口视觉半宽/安全角与最大半径，用于核对视觉与伤害判定。</summary>
		[Export] public bool ShowDamageDebug { get; set; } = false;
		/// <summary>调试：生成后第一物理帧打印自身/攻击者/父节点坐标，便于排查生成点位。</summary>
		[Export] public bool DebugLogSpawn { get; set; } = false;

		/// <summary>攻击来源（IAttackerProvider：由生成方在 AddChild 前注入）。无来源 = 纯视觉，不结算。</summary>
		public GameActor? Attacker { get; set; }

		/// <summary>本次冲击波生成的缺口中心角/半角（弧度，调试与无头验证用）。</summary>
		public Godot.Collections.Array<float> GapCentersRad { get; } = new();
		public Godot.Collections.Array<float> GapHalfWidthsRad { get; } = new();
		/// <summary>调试：从材质读到的缺口羽化角（弧度），逻辑安全区会加上它。</summary>
		public float GapFeatherRad => _gapFeatherRad;

		private const int MaxGapSlots = 6;
		private readonly struct Gap
		{
			public Gap(float centerAngle, float halfWidth)
			{
				CenterAngle = centerAngle;
				HalfWidth = halfWidth;
			}

			public float CenterAngle { get; }
			public float HalfWidth { get; }
		}

		private readonly List<Gap> _gaps = new();
		private readonly HashSet<ulong> _processedTargets = new();
		private readonly HashSet<ulong> _seenThisFrame = new();
		private Node2D? _visual;
		private ShaderMaterial? _material;
		private ShockwaveDebugDraw? _debugDraw;
		private CircleShape2D? _queryShape;
		private Vector2 _origin;
		private float _radius;
		private float _previousRadius;
		private float _fadeRemaining = -1f;
		private bool _expanding = true;
		private bool _missingAttackerWarned;
		private bool _spawnLogged;
		/// <summary>Shader 缺口羽化角（弧度）：逻辑安全区必须把这段羽化也算进去，否则视觉断口和伤害边界错位。</summary>
		private float _gapFeatherRad;

		/// <summary>伪 3D 投影缓存（与 ring_shockwave.gdshader 的 vertex/fragment 完全同构）。</summary>
		private bool _planeSpaceReady;
		private float _ps, _sx, _cx, _sy, _cy, _S, _ds, _kx, _ky, _projScale;
		/// <summary>目标身体包络的圆周采样点数（平面空间下身体不再是正圆，只能采样求包络）。</summary>
		private const int TargetExtentSamples = 8;
		/// <summary>当前/上一帧波前的平面空间归一化半径（= 世界半径 / MaxRadius，与 wave_radius uniform 同量纲）。</summary>
		private float _planeRadius;
		private float _previousPlaneRadius;

		public override void _Ready()
		{
			_origin = GlobalPosition;
			_queryShape = new CircleShape2D();
			SetupVisual();
			// 缺口羽化由材质控制；逻辑侧读同一个值，保证视觉渐隐区域也被判定为安全。
			_gapFeatherRad = _material != null
				? Mathf.Max(0f, _material.GetShaderParameter("gap_feather").AsSingle())
				: 0.1f;
			SetupProjection();
			GenerateGaps();
			ApplyStaticShaderParams();
			SetShaderParam("wave_radius", 0f);
			SetShaderParam("fade_alpha", 1f);

			if (ShowDamageDebug)
			{
				_debugDraw = new ShockwaveDebugDraw
				{
					Name = "DamageDebug",
					ZIndex = 100,
					Controller = this,
				};
				AddChild(_debugDraw);
			}
		}

		public override void _Process(double delta)
		{
			_debugDraw?.QueueRedraw();
		}

		/// <summary>供 <see cref="ShockwaveDebugDraw"/> 调用的判定可视化。</summary>
		public void DrawDamageDebug(CanvasItem canvas)
		{
			if (!_planeSpaceReady) return;   // 无投影映射时画世界坐标会误导，直接不画

			// 白色：最大可视半径（平面 dist = 1）
			DrawPlaneRing(canvas, 1f, new Color(1f, 1f, 1f, 0.15f), 1.5f);

			if (_planeRadius <= 0.0001f) return;
			// 红色：逻辑波前（与视觉环同一个平面半径）
			DrawPlaneRing(canvas, _planeRadius, new Color(1f, 0.25f, 0.2f, 0.9f), 2.5f);

			float margin = Mathf.DegToRad(GapSafetyMarginDegrees);
			var visualGapColor = new Color(0.2f, 1f, 0.35f, 0.9f);
			var safeColor = new Color(1f, 0.9f, 0.2f, 0.9f);

			foreach (Gap gap in _gaps)
			{
				float half = gap.HalfWidth;
				float safe = half + _gapFeatherRad + margin;

				// 绿色：缺口配置半宽（视觉核心断开区）
				DrawPlaneArc(canvas, _planeRadius,
					gap.CenterAngle - half, gap.CenterAngle + half, visualGapColor, 4f);
				// 黄色：实际安全角（半宽 + 羽化 + 安全边距）
				DrawPlaneArc(canvas, _planeRadius,
					gap.CenterAngle - safe, gap.CenterAngle + safe, safeColor, 2f);

				DrawPlaneRay(canvas, _planeRadius, gap.CenterAngle - half, visualGapColor);
				DrawPlaneRay(canvas, _planeRadius, gap.CenterAngle + half, visualGapColor);
				DrawPlaneRay(canvas, _planeRadius, gap.CenterAngle - safe, safeColor);
				DrawPlaneRay(canvas, _planeRadius, gap.CenterAngle + safe, safeColor);
			}
		}

		private Vector2 ProjectPlanePoint(CanvasItem canvas, float planeRadius, float angle)
		{
			Vector2 world = PlaneToWorld(planeRadius, angle);
			return canvas is Node2D node2D ? node2D.ToLocal(world) : world - GlobalPosition;
		}

		private void DrawPlaneRing(CanvasItem canvas, float planeRadius, Color color, float width)
		{
			const int segments = 128;
			var points = new Vector2[segments + 1];
			for (int i = 0; i <= segments; i++)
				points[i] = ProjectPlanePoint(canvas, planeRadius, Mathf.Tau * i / segments);
			canvas.DrawPolyline(points, color, width, true);
		}

		private void DrawPlaneArc(CanvasItem canvas, float planeRadius,
			float fromAngle, float toAngle, Color color, float width)
		{
			int segments = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(toAngle - fromAngle) / (Mathf.Tau / 128f)));
			var points = new Vector2[segments + 1];
			for (int i = 0; i <= segments; i++)
				points[i] = ProjectPlanePoint(canvas, planeRadius,
					Mathf.Lerp(fromAngle, toAngle, (float)i / segments));
			canvas.DrawPolyline(points, color, width, true);
		}

		private void DrawPlaneRay(CanvasItem canvas, float planeRadius, float angle, Color color)
		{
			const int segments = 24;
			var points = new Vector2[segments + 1];
			for (int i = 0; i <= segments; i++)
				points[i] = ProjectPlanePoint(canvas, planeRadius * i / segments, angle);
			canvas.DrawPolyline(points, color, 1.5f, true);
		}

		public override void _PhysicsProcess(double delta)
		{
			// AttackEffectEntry 的生成顺序是 AddChild → 再摆 GlobalPosition；_Ready 里读坐标会是默认值。
			// 每帧读一次（本效果不移动）确保伤害中心与视觉中心一致。
			_origin = GlobalPosition;

			if (DebugLogSpawn && !_spawnLogged)
			{
				_spawnLogged = true;
				bool attackerValid = Attacker != null && GodotObject.IsInstanceValid(Attacker);
				Vector2 attackerPos = attackerValid ? Attacker!.GlobalPosition : Vector2.Zero;
				Vector2 spritePos = _visual != null && GodotObject.IsInstanceValid(_visual)
					? _visual.GlobalPosition
					: Vector2.Zero;

				string centersDeg = "";
				string halvesDeg = "";
				for (int i = 0; i < _gaps.Count; i++)
				{
					if (i > 0) { centersDeg += ", "; halvesDeg += ", "; }
					centersDeg += $"{Mathf.RadToDeg(_gaps[i].CenterAngle):F1}";
					halvesDeg += $"{Mathf.RadToDeg(_gaps[i].HalfWidth):F1}";
				}
				Node2D? parent2D = GetParent() as Node2D;

				GD.Print($"[Shockwave] wave_pos={GlobalPosition} sprite_pos={spritePos}"
					+ $" attacker={(attackerValid ? Attacker!.Name.ToString() : "<null>")}"
					+ $" attacker_pos={attackerPos} delta={GlobalPosition - attackerPos}"
					+ $" attacker_path={(attackerValid ? Attacker!.GetPath().ToString() : "<null>")}"
					+ $" attacker_id={(attackerValid ? Attacker!.GetInstanceId() : 0)}"
					+ $" parent={GetParent()?.GetPath().ToString() ?? "<none>"}"
					+ $" gaps={_gaps.Count} centers_deg=[{centersDeg}] halves_deg=[{halvesDeg}]"
					+ $" feather_deg={Mathf.RadToDeg(_gapFeatherRad):F1} margin_deg={GapSafetyMarginDegrees:F1}"
					+ $" self_rot_deg={Mathf.RadToDeg(GlobalRotation):F1} self_scale={GlobalScale}"
					+ $" parent_rot_deg={(parent2D != null ? Mathf.RadToDeg(parent2D.GlobalRotation) : 0f):F1}"
					+ $" parent_scale={(parent2D != null ? parent2D.GlobalScale : Vector2.One)}"
					+ $" t_ms={Time.GetTicksMsec()}");
			}

			if (_expanding)
			{
				_previousRadius = _radius;
				_radius = Mathf.Min(MaxRadius, _radius + Mathf.Max(ExpandSpeed, 0f) * (float)delta);
				float radiusDenom = Mathf.Max(MaxRadius, 0.001f);
				_previousPlaneRadius = _previousRadius / radiusDenom;
				_planeRadius = _radius / radiusDenom;
				SetShaderParam("wave_radius", _planeRadius);
				ProcessTargets();

				if (_radius >= MaxRadius - 0.001f)
				{
					_expanding = false;
					_fadeRemaining = Mathf.Max(FadeDuration, 0f);
					if (_fadeRemaining <= 0f)
						QueueFree();
				}
				return;
			}

			_fadeRemaining -= (float)delta;
			float alpha = FadeDuration > 0f ? Mathf.Clamp(_fadeRemaining / FadeDuration, 0f, 1f) : 0f;
			SetShaderParam("fade_alpha", alpha);
			if (_fadeRemaining <= 0f)
				QueueFree();
		}

		// ── 视觉 ──────────────────────────────────────────────────

		private void SetupVisual()
		{
			_visual = GetNodeOrNull<Node2D>(VisualSpritePath);
			if (_visual == null)
			{
				GD.PushWarning($"[{nameof(ShockwaveController)}] 未找到视觉节点：{VisualSpritePath}");
				return;
			}

			// 每个冲击波独立材质：共享 ShaderMaterial 会让多个实例互相改写参数
			if (_visual.Material is ShaderMaterial shared)
			{
				_material = (ShaderMaterial)shared.Duplicate();
				_visual.Material = _material;
			}
			else
			{
				GD.PushWarning($"[{nameof(ShockwaveController)}] 视觉节点未挂 ShaderMaterial");
			}

			_visual.Position = Vector2.Zero;

			// 视觉必须是 MeshInstance2D + 2×2 QuadMesh：顶点以原点为中心、UV 0..1。
			// Sprite2D + 1×1 纹理的 centered/offset 实测不可靠（绘制矩形从节点向右下展开，
			// 导致 Shader 的 UV 中心——环形扩散中心——偏离节点），不再支持。
			if (_visual is not MeshInstance2D)
			{
				GD.PushWarning($"[{nameof(ShockwaveController)}] 视觉节点应为 MeshInstance2D + 2×2 QuadMesh");
				return;
			}

			// QuadMesh 尺寸 2 → 缩放 MaxRadius 后覆盖 2×MaxRadius
			_visual.Scale = new Vector2(MaxRadius, MaxRadius);
		}

		/// <summary>
		/// 读取材质的伪 3D 参数并缓存投影常量（材质是唯一参数源，改 fov/rot 不需要同步导出）。
		/// 公式与 ring_shockwave.gdshader 的 vertex/fragment 逐项对应；shader 改了这里必须同步。
		/// </summary>
		private void SetupProjection()
		{
			if (_visual == null || _material == null || _visual is not MeshInstance2D)
			{
				_planeSpaceReady = false;
				GD.PushWarning($"[{nameof(ShockwaveController)}] 缺少 MeshInstance2D/材质：命中判定回退世界坐标");
				return;
			}

			float fov = Mathf.Clamp(GetShaderFloat("fov", 60f), 1f, 179f);
			float rotYDeg = GetShaderFloat("rot_y_deg", 0f);
			float rotXDeg = GetShaderFloat("rot_x_deg", 25f);
			float inset = Mathf.Clamp(GetShaderFloat("inset", 0f), 0f, 1f);
			float quadSize = Mathf.Max(GetShaderFloat("quad_size", 2f), 0.001f);

			float rotY = Mathf.DegToRad(rotYDeg);
			float rotX = Mathf.DegToRad(rotXDeg);
			_ps = Mathf.Tan(Mathf.DegToRad(fov) * 0.5f);
			_sx = Mathf.Sin(rotX); _cx = Mathf.Cos(rotX);
			_sy = Mathf.Sin(rotY); _cy = Mathf.Cos(rotY);
			_S = 0.5f / Mathf.Max(_ps, 1e-4f);
			_ds = _S + 0.5f;

			// QuadMesh 的 V 轴与世界 Y 相反：UV 顶点位移在 Y 上表现为收缩（shader 里 (UV-0.5) 的符号）
			float shift = quadSize * _ps * (1f - inset);
			_kx = 2f + shift;
			_ky = 2f - shift;
			_projScale = _ds * _cy * _cx;

			_planeSpaceReady = Mathf.Abs(_kx) > 1e-6f
				&& Mathf.Abs(_ky) > 1e-6f
				&& Mathf.Abs(_projScale) > 1e-6f;
			if (!_planeSpaceReady)
				GD.PushWarning($"[{nameof(ShockwaveController)}] 投影参数退化（kx/ky/scale≈0）：命中判定回退世界坐标");
		}

		private float GetShaderFloat(string parameter, float fallback)
		{
			if (_material == null) return fallback;
			Variant value = _material.GetShaderParameter(parameter);
			return value.VariantType == Variant.Type.Nil ? fallback : value.AsSingle();
		}

		/// <summary>世界坐标 → shader 平面空间（归一化半径 + 角）。公式与 shader 的 vertex/fragment 对称。</summary>
		private bool WorldToPlane(Vector2 worldPos, out float dist, out float angle)
		{
			dist = 0f;
			angle = 0f;
			if (!_planeSpaceReady || _visual == null || !GodotObject.IsInstanceValid(_visual))
				return false;

			Vector2 local = _visual.ToLocal(worldPos);
			float qx = local.X / _kx;          // u - 0.5
			float qy = -local.Y / _ky;         // v - 0.5（QuadMesh V 轴翻转补偿）

			float wx = _cy * qx + _sy * _sx * qy + _sy * _cx * _S;
			float wy = _cx * qy - _sx * _S;
			float wz = -_sy * qx + _cy * _sx * qy + _cy * _cx * _S;
			if (Mathf.Abs(wz) < 1e-6f) return false;

			float pu = wx * _projScale / wz - _ds * _sy * _cx + 0.5f;
			float pv = 0.5f - wy * _projScale / wz - _ds * _sx;

			Vector2 p = new(pu - 0.5f, pv - 0.5f);
			dist = p.Length() * 2f;
			angle = p.Angle();
			return true;
		}

		/// <summary>平面空间 → 世界坐标（调试绘制用）。逆投影：解 3×3 线性方程组求 (qx, qy, wz)。</summary>
		private Vector2 PlaneToWorld(float dist, float angle)
		{
			float pu = 0.5f + Mathf.Cos(angle) * dist * 0.5f;
			float pv = 0.5f + Mathf.Sin(angle) * dist * 0.5f;

			float a = (pu - 0.5f + _ds * _sy * _cx) / _projScale;
			float b = (-_ds * _sx - (pv - 0.5f)) / _projScale;

			float r00 = _cy, r01 = _sy * _sx, r02 = _sy * _cx;
			float r10 = 0f, r11 = _cx, r12 = -_sx;
			float r20 = -_sy, r21 = _cy * _sx, r22 = _cy * _cx;

			float e1 = r00 - a * r20, f1 = r01 - a * r21, g1 = _S * (a * r22 - r02);
			float e2 = r10 - b * r20, f2 = r11 - b * r21, g2 = _S * (b * r22 - r12);
			float det = e1 * f2 - f1 * e2;
			if (Mathf.Abs(det) < 1e-8f || _visual == null) return GlobalPosition;

			float qx = (g1 * f2 - f1 * g2) / det;
			float qy = (e1 * g2 - g1 * e2) / det;
			return _visual.ToGlobal(new Vector2(_kx * qx, -_ky * qy));
		}

		private void ApplyStaticShaderParams()
		{
			if (_material == null) return;

			float denom = Mathf.Max(MaxRadius, 0.001f);
			_material.SetShaderParameter("ring_thickness", RingThickness / denom);
			_material.SetShaderParameter("edge_softness", EdgeSoftness / denom);
			_material.SetShaderParameter("wave_irregularity", WaveIrregularity / denom);
			_material.SetShaderParameter("wave_frequency", WaveFrequency);
		}

		private void SetShaderParam(string name, Variant value)
		{
			_material?.SetShaderParameter(name, value);
		}

		// ── 缺口 ──────────────────────────────────────────────────

		private void GenerateGaps()
		{
			_gaps.Clear();
			GapCentersRad.Clear();
			GapHalfWidthsRad.Clear();

			int count = Mathf.Clamp(GapCount, 0, MaxGapSlots);
			if (count > 0)
			{
				// 每个缺口占一个扇区、扇区内随机中心与宽度：天然保证最小角间距，不会连成整段空波
				float sector = Mathf.Tau / count;
				float maxHalf = Mathf.DegToRad(Mathf.Max(MinGapHalfWidthDegrees, MaxGapHalfWidthDegrees));
				float minHalf = Mathf.DegToRad(Mathf.Min(MinGapHalfWidthDegrees, MaxGapHalfWidthDegrees));
				maxHalf = Mathf.Min(maxHalf, sector * 0.4f);
				minHalf = Mathf.Min(minHalf, maxHalf);

				float offset = (float)GD.RandRange(0.0f, Mathf.Tau);
				for (int i = 0; i < count; i++)
				{
					float center = offset + sector * i
						+ (float)GD.RandRange(sector * 0.2f, sector * 0.8f);
					center = Mathf.Wrap(center, 0f, Mathf.Tau);
					float half = (float)GD.RandRange(minHalf, maxHalf);

					_gaps.Add(new Gap(center, half));
					GapCentersRad.Add(center);
					GapHalfWidthsRad.Add(half);
				}
			}

			if (_material == null) return;
			for (int i = 0; i < MaxGapSlots; i++)
			{
				float center = i < _gaps.Count ? _gaps[i].CenterAngle : 0f;
				float half = i < _gaps.Count ? _gaps[i].HalfWidth : 0f;
				_material.SetShaderParameter($"gap{i}_center", center);
				_material.SetShaderParameter($"gap{i}_half", half);
			}
			_material.SetShaderParameter("gap_count", _gaps.Count);
		}

		private bool IsAngleInGap(float angle, float angularHalfWidth)
		{
			float margin = Mathf.DegToRad(GapSafetyMarginDegrees);
			// 视觉上"安全"= 整个身体都在缺口扇区内。只要身体有一部分落进实心环，
			// 视觉上就是环已经压到身上——此时必须判定为命中，才能与视觉一致。
			// angularHalfWidth 由 GetTargetExtent 在平面空间采样得到（含 QuadMesh V 翻转与透视畸变）。
			foreach (Gap gap in _gaps)
			{
				// 环绕角差：正确处理 ±PI 两侧
				float delta = Mathf.Atan2(
					Mathf.Sin(angle - gap.CenterAngle),
					Mathf.Cos(angle - gap.CenterAngle));
				if (Mathf.Abs(delta) + angularHalfWidth <= gap.HalfWidth + _gapFeatherRad + margin)
					return true;
			}
			return false;
		}

		/// <summary>
		/// 目标身体的平面空间包络：8 点采样身体圆周（世界半径 = TargetHitRadius），
		/// 得到径向 min/max（与 wave_radius 同量纲）和相对中心角的角半径。
		/// 投影不可用时回退世界坐标正圆（径向往平面量纲换算）。
		/// </summary>
		private void GetTargetExtent(Vector2 worldPos,
			out float minRadius, out float maxRadius, out float angle, out float angularHalfWidth)
		{
			if (!_planeSpaceReady || !WorldToPlane(worldPos, out float centerDist, out float centerAngle))
			{
				Vector2 offset = worldPos - _origin;
				float distance = offset.Length();
				float denom = Mathf.Max(MaxRadius, 0.001f);
				minRadius = Mathf.Max(0f, distance - TargetHitRadius) / denom;
				maxRadius = (distance + TargetHitRadius) / denom;
				angle = offset.Angle();
				angularHalfWidth = Mathf.Atan2(TargetHitRadius, Mathf.Max(distance, 1f));
				return;
			}

			minRadius = centerDist;
			maxRadius = centerDist;
			angle = centerAngle;
			angularHalfWidth = 0f;

			for (int i = 0; i < TargetExtentSamples; i++)
			{
				float sampleAngle = Mathf.Tau * i / TargetExtentSamples;
				Vector2 sample = worldPos
					+ new Vector2(Mathf.Cos(sampleAngle), Mathf.Sin(sampleAngle)) * TargetHitRadius;
				if (!WorldToPlane(sample, out float d, out float a)) continue;

				minRadius = Mathf.Min(minRadius, d);
				maxRadius = Mathf.Max(maxRadius, d);
				float delta = Mathf.Wrap(a - centerAngle, -Mathf.Pi, Mathf.Pi);
				angularHalfWidth = Mathf.Max(angularHalfWidth, Mathf.Abs(delta));
			}
		}

		// ── 命中 ──────────────────────────────────────────────────

		private void ProcessTargets()
		{
			if (Attacker == null || !GodotObject.IsInstanceValid(Attacker))
			{
				if (!_missingAttackerWarned)
				{
					_missingAttackerWarned = true;
					GD.PushWarning($"[{nameof(ShockwaveController)}] 未设置 Attacker，冲击波只播放视觉、不结算伤害");
				}
				return;
			}

			if (_queryShape == null) return;

			// 目标发现走 IntersectShape（EFFECT_STANDARD §二），不扫组；
			// 查询圆跟随波前推进，只覆盖当前波前附近，避免每帧扫全场。
			_queryShape.Radius = Mathf.Max(_radius + TargetHitRadius, 1f);
			var query = new PhysicsShapeQueryParameters2D
			{
				Shape = _queryShape,
				Transform = new Transform2D(0f, GlobalPosition),
				CollisionMask = TargetCollisionMask,
				CollideWithAreas = true,
				CollideWithBodies = true,
			};

			_seenThisFrame.Clear();
			foreach (Godot.Collections.Dictionary hit in GetWorld2D().DirectSpaceState.IntersectShape(query, 64))
			{
				Node? collider = hit["collider"].As<Node>();
				if (collider == null) continue;
				if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(collider, Attacker)) continue;

				// 接收点可以是 GameActor（玩家/敌人），也可以是非 GameActor 的 world item
				// （WorldItem / 可破坏物走 TakeDamage(float)；目标面板的 WorldItem 分支即可命中）
				Node? receiver = DamageDispatcher.ResolveDamageReceiver(collider, TargetableFactions);
				if (receiver is not Node2D target) continue;
				if (target is GameActor actor && actor.IsDeadOrDying) continue;
				if (!AllowSelfDamage && target == Attacker) continue;

				ulong id = target.GetInstanceId();
				if (!_seenThisFrame.Add(id)) continue;
				if (_processedTargets.Contains(id)) continue;

				TryHit(target, id);
			}
		}

		private void TryHit(Node2D target, ulong id)
		{
			Vector2 offset = target.GlobalPosition - _origin;

			// 平面空间的波前是否在这一帧扫过目标：min..max 是身体圆周投影后的径向包络
			GetTargetExtent(target.GlobalPosition,
				out float minRadius, out float maxRadius, out float angle, out float angularHalfWidth);

			if (maxRadius < _previousPlaneRadius) return;
			if (minRadius > _planeRadius) return;

			// 缺口内也标记已处理：同一道波对同一目标只判定一次
			_processedTargets.Add(id);

			if (IsAngleInGap(angle, angularHalfWidth)) return;

			Vector2 direction = offset.LengthSquared() > 0.0001f
				? offset.Normalized()
				: Vector2.Right;

			if (Damage > 0)
			{
				bool dealt = DamageDispatcher.DealDamage(target, Damage, _origin, Attacker,
					DamageSource.AreaEffect, TargetableFactions, AllowSelfDamage);
				if (!dealt) return;
			}

			// 击退只适用于 GameActor；world item 没有位移接口，只结算伤害
			if (KnockbackDistance > 0f && target is GameActor actor)
			{
				actor.ApplyKnockbackDisplacement(direction, KnockbackDistance,
					Mathf.Max(KnockbackDuration, 0.01f));
			}
		}
	}
}
