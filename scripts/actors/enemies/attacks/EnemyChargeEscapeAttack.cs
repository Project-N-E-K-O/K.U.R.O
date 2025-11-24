using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// 冲刺抓取攻击示例：玩家需在逃脱时间内左右输入各若干次才能脱困。
	/// </summary>
	public partial class EnemyChargeEscapeAttack : EnemyChargeGrabAttack
	{
		[Export(PropertyHint.Range, "1,20,1")]
		public int RequiredLeftInputs = 4;

		[Export(PropertyHint.Range, "1,20,1")]
		public int RequiredRightInputs = 4;

		private int _leftCount;
		private int _rightCount;

		public EnemyChargeEscapeAttack()
		{
			EscapeWindowSeconds = 2.0f;
		}

		protected override void OnEscapeSequenceStarted(SamplePlayer player)
		{
			_leftCount = 0;
			_rightCount = 0;
		}

		protected override void UpdateEscapeSequence(SamplePlayer player, double delta)
		{
			// Escape disabled for current testing scenario.
		}

		protected override void OnEscapeSequenceFinished(SamplePlayer player, bool escaped)
		{
		}
	}
}
