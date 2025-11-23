using Godot;
using System.Collections.Generic;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 根据权重随机选择子攻击模板并触发。
    /// </summary>
    public partial class EnemyAttackController : EnemyAttackTemplate
    {
        [Export] public NodePath PlayerDetectionAreaPath = new NodePath();

        private readonly List<Entry> _entries = new();
        private EnemyAttackTemplate? _currentAttack;
        private EnemyAttackTemplate? _queuedAttack;
        private Area2D? _playerDetectionArea;

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            _entries.Clear();
            _playerDetectionArea = !PlayerDetectionAreaPath.IsEmpty
                ? Enemy.GetNodeOrNull<Area2D>(PlayerDetectionAreaPath)
                : AttackArea;

            foreach (Node child in GetChildren())
            {
                if (child is EnemyAttackTemplate template)
                {
                    float weight = 1f;
                    if (template.HasMeta("attack_weight"))
                    {
                        Variant meta = template.GetMeta("attack_weight");
                        if (meta.VariantType == Variant.Type.Float || meta.VariantType == Variant.Type.Int)
                        {
                            weight = (float)meta;
                        }
                    }

                    _entries.Add(new Entry { Template = template, Weight = Mathf.Max(weight, 0f) });
                }
            }

            QueueNextAttack();
        }

        public override bool CanStart()
        {
            if (_entries.Count == 0) return false;
            if (!base.CanStart()) return false;

            var player = Enemy.PlayerTarget;
            if (player == null) return false;

            if (_playerDetectionArea != null)
            {
                return _playerDetectionArea.OverlapsBody(player);
            }

            return true;
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
            if (_queuedAttack == null)
            {
                QueueNextAttack();
            }

            _currentAttack = _queuedAttack;
            _queuedAttack = null;

            if (_currentAttack == null)
            {
                GD.Print("[EnemyAttackController] No attack selected, cancelling.");
                Cancel(clearCooldown: true);
                return;
            }

            _currentAttack.Initialize(Enemy);
            GD.Print($"[EnemyAttackController] Starting {_currentAttack.Name} for enemy {Enemy.Name}.");
            _currentAttack.TryStart();
        }

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
            _currentAttack?.Cancel();
            _currentAttack = null;
            QueueNextAttack(reason: "Recovery");
        }

        protected override void OnAttackFinished()
        {
            _currentAttack?.Cancel();
            _currentAttack = null;

            if (_queuedAttack == null)
            {
                QueueNextAttack(reason: "AttackFinished");
            }

            base.OnAttackFinished();
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);
            _currentAttack?.Tick(delta);
        }

        private EnemyAttackTemplate? PickAttack()
        {
            float totalWeight = 0f;
            foreach (var entry in _entries)
            {
                totalWeight += entry.Weight;
            }
            if (totalWeight <= 0f) return null;

            float roll = (float)GD.RandRange(0, totalWeight);
            float cumulative = 0f;

            foreach (var entry in _entries)
            {
                cumulative += entry.Weight;
                if (roll <= cumulative)
                {
                    return entry.Template;
                }
            }

            return null;
        }

        private void QueueNextAttack(string reason = "Auto")
        {
            _queuedAttack = PickAttack();
            if (_queuedAttack != null)
            {
                GD.Print($"[EnemyAttackController] ({reason}) Queued {_queuedAttack.Name} for enemy {Enemy.Name}.");
            }
            else
            {
                GD.Print($"[EnemyAttackController] ({reason}) No attack available to queue for {Enemy.Name}.");
            }
        }

        public EnemyAttackTemplate? PeekQueuedAttack() => _queuedAttack;

        public void ForceQueueNextAttack(string reason = "Forced")
        {
            GD.Print($"[EnemyAttackController] Force queue requested ({reason}) for {Enemy.Name}.");
            _currentAttack?.Cancel(clearCooldown: true);
            _currentAttack = null;
            _queuedAttack = null;
            QueueNextAttack(reason);
        }

        private class Entry
        {
            public EnemyAttackTemplate Template = null!;
            public float Weight;
        }
    }
}

