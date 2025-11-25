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
	private float _escapeTimer;
		private bool _escapeResolved;

	public override void _Ready()
	{
		base._Ready();
		if (EscapeWindowSeconds <= 0f)
		{
			EscapeWindowSeconds = 2.0f;
		}
	}

		public EnemyChargeEscapeAttack()
		{
			EscapeWindowSeconds = 2.0f;
		}

		protected override void OnEscapeSequenceStarted(SamplePlayer player)
		{
			_leftCount = 0;
			_rightCount = 0;
			_escapeTimer = EscapeWindowSeconds;
			_escapeResolved = false;
		}

		protected override void UpdateEscapeSequence(SamplePlayer player, double delta)
		{
			if (_escapeResolved) return;

			_escapeTimer -= (float)delta;

			if (Input.IsActionJustPressed("move_left"))
			{
				_leftCount++;
			}

			if (Input.IsActionJustPressed("move_right"))
			{
				_rightCount++;
			}

			if (_leftCount >= RequiredLeftInputs && _rightCount >= RequiredRightInputs)
			{
				GD.Print($"[EnemyChargeEscapeAttack] {player.Name} escaped by inputs L:{_leftCount}/R:{_rightCount}.");
				_escapeResolved = true;
				ResolveEscape(true);
			}
			else if (_escapeTimer <= 0f)
			{
				GD.Print($"[EnemyChargeEscapeAttack] {player.Name} failed to escape (inputs L:{_leftCount}/R:{_rightCount}).");
				_escapeResolved = true;
				ResolveEscape(false);
			}
		}

		protected override void OnEscapeSequenceFinished(SamplePlayer player, bool escaped) { }
	}
}
