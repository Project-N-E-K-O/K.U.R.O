using Godot;
using Kuros.UI;
using Kuros.Utils;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
	/// 冲刺抓取攻击示例：玩家需在逃脱时间内左右输入各若干次才能脱困。
    /// </summary>
    public partial class EnemyChargeEscapeAttack : EnemyChargeGrabAttack
    {
        [Export(PropertyHint.Range, "1,20,1")]
        public int RequiredLeftInputs = 3;

        [Export(PropertyHint.Range, "1,20,1")]
        public int RequiredRightInputs = 3;

		private int _leftCount;
		private int _rightCount;
		private bool _escapeResolved;
		private bool _areEscapeCountersCleared = true;
		private EscapeHUD? _escapeHud;

		public override bool AreEscapeCountersCleared => _areEscapeCountersCleared;

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
			_escapeResolved = false;
			_areEscapeCountersCleared = false;
			_escapeHud = ResolveEscapeHud(player);
			_escapeHud?.ShowSequence(RequiredLeftInputs, RequiredRightInputs, EscapeWindowSeconds);
        }

		protected override void UpdateEscapeSequence(SamplePlayer player, double delta)
        {
			if (_escapeResolved) return;

			GD.Print($"[EscapeSeq] IsEvaluatingEscape={IsEvaluatingEscape} AreEscapeCountersCleared={AreEscapeCountersCleared}  timer={EscapeTimerRemaining:F2}s  L={_leftCount}/{RequiredLeftInputs}  R={_rightCount}/{RequiredRightInputs}");

            if (Input.IsActionJustPressed("move_left"))
            {
                _leftCount++;
            }

            if (Input.IsActionJustPressed("move_right"))
            {
                _rightCount++;
            }

			_escapeHud?.UpdateSequence(_leftCount, _rightCount, EscapeTimerRemaining);

			if (_leftCount >= RequiredLeftInputs && _rightCount >= RequiredRightInputs)
			{
				GameLogger.Info(nameof(EnemyChargeEscapeAttack), $"{player.Name} escaped by inputs L:{_leftCount}/R:{_rightCount}.");
				_escapeResolved = true;
				ResolveEscape(true);
			}
		}

		protected override void OnEscapeSequenceFinished(SamplePlayer player, bool escaped)
		{
			_leftCount = 0;
			_rightCount = 0;
			_areEscapeCountersCleared = true;
			_escapeHud?.HideSequence();
			_escapeHud = null;
		}

		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			if (!IsEvaluatingEscape)
			{
				_leftCount = 0;
				_rightCount = 0;
				_areEscapeCountersCleared = true;
				_escapeHud?.HideSequence();
				_escapeHud = null;
			}
		}

		private EscapeHUD? ResolveEscapeHud(SamplePlayer player)
		{
			if (_escapeHud != null && GodotObject.IsInstanceValid(_escapeHud))
			{
				return _escapeHud;
			}

			return player.GetNodeOrNull<EscapeHUD>("EscapeHud");
		}
	}
}
