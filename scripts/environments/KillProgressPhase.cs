using Godot;

namespace Kuros.Environments
{
	/// <summary>
	/// 上升井道里**一段**进度配置（供 <see cref="KillProgressAscendController.Phases"/> 用）。
	/// 一项 = 一段"击杀进度 → 到站"：需求击杀数、到站触发器、以及本段的上升动画驱动参数。
	///
	/// **字段是权威的**：分段模式下以本资源为准——空路径 = 本段不驱动上升视觉（例如没有上升动画的二阶段）。
	/// 控制器节点上那份同名字段只在"不分段（Phases 为空）"时生效，其它关卡因此零迁移。
	/// 段与段之间由 <c>KillProgressAscendController.BeginNextPhase()</c> 接续（默认由"本段到站过场播完"自动触发）。
	/// </summary>
	[GlobalClass]
	public partial class KillProgressPhase : Resource
	{
		/// <summary>段标识（日志/调试用；留空也行）。</summary>
		[Export] public string PhaseId { get; set; } = "";

		[ExportCategory("Progress")]
		/// <summary>本段满进度所需的击杀数（0 = 本段开局即满，直接走到站流程）。</summary>
		[Export(PropertyHint.Range, "0,500,1")] public int TotalKillsForFull { get; set; } = 20;

		[ExportCategory("Arrival")]
		/// <summary>本段的到站触发器（相对控制器节点）。触发区覆盖平台 = 触发即播；放到出口 = 玩家走过去才播。</summary>
		[Export] public NodePath ArrivalTriggerPath { get; set; } = new();
		/// <summary>本段到站触发器的武装延迟（秒）：语义同控制器上的 ArrivalTriggerDelay（满进度后的"堵塞时间"）。</summary>
		[Export(PropertyHint.Range, "0,30,0.1")] public float ArrivalTriggerDelay { get; set; } = 0f;

		[ExportCategory("Ascend Visual")]
		/// <summary>本段的上升动画播放器（相对控制器节点）；**清空 = 本段不驱动上升视觉**
		/// （没有上升动画的阶段就该留空，而不是配一个不存在的路径）。</summary>
		[Export] public NodePath AnimationPlayerPath { get; set; } = new("AnimationPlayer");
		/// <summary>本段的"持续上升"循环动画名。</summary>
		[Export] public string LoopAnimationName { get; set; } = "up_loop";
		/// <summary>速度区间：进度 0 → MinSpeedScale，进度 1 → MaxSpeedScale（本段自己的曲线）。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float MinSpeedScale { get; set; } = 1f;
		[Export(PropertyHint.Range, "0,10,0.05")] public float MaxSpeedScale { get; set; } = 2f;
		/// <summary>本段速度平滑（每秒逼近系数，0 = 立即跳变）——两段可以有各自的手感。</summary>
		[Export(PropertyHint.Range, "0,20,0.1")] public float SpeedScaleLerpSpeed { get; set; } = 2.5f;
	}
}
