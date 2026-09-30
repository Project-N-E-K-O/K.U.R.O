using System;
using Godot;
using Kuros.Core;
using Kuros.Effects;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 能量弹投射物（EnergyOrbShot 专用）：出手瞬间**快照**玩家位置作为落点 → 在落点显示缩圈预警 →
    /// 沿**直线**飞向落点 → 到达后生成落地特效并销毁自身。
    ///
    /// 为什么不复用 <c>EnemyWaiterAThrowProjectile</c>：那是投掷物——抛物线轨迹 + 自旋 + 茶壶专属配置，
    /// 而能量弹是"直着打出去"的（直线、出手即定向、无自旋），两者的运动学与可调项都不同。
    ///
    /// 三条与项目一致的约定：
    ///   1. **快照而非追踪**：落点在生成瞬间就定死，玩家看到预警后跑开就能躲——不做追踪，追踪会让预警失去意义；
    ///   2. **不做碰撞/伤害判定**：伤害（与爆炸、特效组）放在 <see cref="ImpactEffectScenes"/> 里，
    ///      由落地那一刻生成——与投掷物、导弹同一套约定，`EnemyMissileProjectile` 用的是同款 `AttackEffectEntry`；
    ///   3. **匀速飞行**：速度由 <see cref="Speed"/> 定死，飞行时间 = 距离 ÷ 速度——
    ///      所以远处自然飞得久（预警圈也跟着拉长），而不是"固定时长、越远越快"。
    ///
    /// 实现 <see cref="Kuros.Fx.IAttackerProvider"/>：生成链会把敌人注入进来，落地特效据此做伤害归属。
    /// </summary>
    public partial class EnergyOrbShot : Node2D, Kuros.Fx.IAttackerProvider
    {
        /// <summary>飞行速度（px/s，匀速）。飞行时间 = 起点到落点的距离 ÷ 本值。</summary>
        [Export(PropertyHint.Range, "50,5000,10")] public float Speed { get; set; } = 1200f;

        /// <summary>出手时把节点转向飞行方向（目标在左/在右都会正确指向）。关掉则保持 0 度。</summary>
        [Export] public bool FaceTarget { get; set; } = true;

        /// <summary>落点缩圈预警预制体（可选，一般用 LandingIndicatorA）。</summary>
        [Export] public PackedScene? LandingIndicatorPrefab { get; set; }

        /// <summary>到达后依次生成的特效场景（伤害/爆炸放这里）。为空 = 只销毁自身。</summary>
        [Export] public PackedScene[] ImpactEffectScenes { get; set; } = Array.Empty<PackedScene>();

        public GameActor? Attacker { get; set; }

        private Vector2 _startPos;
        private Vector2 _targetPos;
        private float _elapsed;
        private float _flightTime;   // 本次飞行时长 = 起点到落点距离 ÷ Speed（出手那一帧才定得下来）
        private bool _launched;
        private Node? _landingIndicator;

        public override void _Ready()
        {
            // 不继承父节点变换：生成方把特效挂在敌人的父节点下，而炮台/磁铁臂这类敌人的父节点是
            // **滑槽的 Mount**（会随滑槽纵向滑动）——不设 TopLevel 的话落点与落地特效会跟着滑槽一起漂。
            // （与 PlayerAttackTemplate.HitEffectForceTopLevel 同一套处理。）
            TopLevel = true;

            // 快照玩家当前位置作为落点（此刻自身的 GlobalPosition 还没被生成方写入，不要用它）
            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            _targetPos = player?.GlobalPosition ?? GlobalPosition;

            SetPhysicsProcess(true);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (!_launched)
            {
                // 第一帧：生成方（攻击模板）已经把 GlobalPosition 摆到炮口，这时才记录起点、
                // 定下本次飞行时长，并按方向摆好预警（预警时长 = 飞行时长，预警结束那一刻就是落地）
                _startPos = GlobalPosition;
                _launched = true;

                // 炮口只能朝前打：快照落在射手背后时，按同一距离镜像到身前（表现成"打空了"）
                _targetPos = MirrorTargetToFront(_targetPos);

                _flightTime = Mathf.Max(0.05f, _startPos.DistanceTo(_targetPos) / Mathf.Max(1f, Speed));

                if (FaceTarget)
                {
                    Vector2 toTarget = _targetPos - _startPos;
                    if (toTarget.LengthSquared() > 0.0001f)
                        Rotation = toTarget.Angle();
                }

                SpawnLandingIndicator(_flightTime);
                return;
            }

            _elapsed += (float)delta;
            float t = Mathf.Clamp(_elapsed / _flightTime, 0f, 1f);
            GlobalPosition = _startPos.Lerp(_targetPos, t);   // 直线：没有抛物线抬升

            if (t >= 1f) OnArrived();
        }

        /// <summary>落点落到射手**背后**时，沿 X 按同一距离镜像到身前。
        ///
        /// 起因：快照发生在开火那一刻，而蓄力（WarmupDuration，本炮台 2 秒）期间玩家可以绕到背后——
        /// 起手时他确实在身前，所以这次开火是合法的，但快照点已经在身后了；不处理的话炮弹会反着飞、
        /// 从炮管里往回窜。镜像后表现为"炮台朝前打了一发但打空了"，方向永远与朝向一致。
        /// 非 GameActor 的射手（没有朝向概念）不处理。</summary>
        private Vector2 MirrorTargetToFront(Vector2 target)
        {
            if (Attacker is not GameActor shooter) return target;

            bool behind = shooter.FacingRight ? target.X < _startPos.X : target.X > _startPos.X;
            if (!behind) return target;

            return new Vector2(2f * _startPos.X - target.X, target.Y);
        }

        /// <summary>落点预警：时长与本次飞行时长一致（预警结束的那一刻就是落地）。
        /// 同样 TopLevel——它是"世界坐标上的一个点"，不能跟着滑槽/敌人一起动。</summary>
        private void SpawnLandingIndicator(float warningDuration)
        {
            if (LandingIndicatorPrefab == null) return;

            var indicator = LandingIndicatorPrefab.Instantiate<Node>();
            _landingIndicator = indicator;
            GetParent()?.AddChild(indicator);

            if (indicator is Node2D indicator2D)
            {
                indicator2D.TopLevel = true;
                indicator2D.GlobalPosition = _targetPos;
            }

            var landing = indicator as LandingIndicator ?? indicator.GetNodeOrNull<LandingIndicator>(".");
            if (landing != null)
            {
                landing.WarningDuration = warningDuration;
                landing.Start();
            }
        }

        /// <summary>到达落点：生成落地特效（伤害/爆炸组），收起预警，销毁自身。</summary>
        private void OnArrived()
        {
            SetPhysicsProcess(false);

            foreach (var scene in ImpactEffectScenes)
            {
                if (scene == null) continue;
                var fx = scene.Instantiate<Node>();

                // ActorEffect（走 EffectController 生命周期）与投掷物同一约定
                if (fx is Kuros.Core.Effects.ActorEffect actorEffect && Attacker?.EffectController != null)
                {
                    if (actorEffect is Kuros.Core.Effects.IWorldSpawnable worldSpawnable)
                        worldSpawnable.WorldSpawnPosition = GlobalPosition;
                    Attacker.ApplyEffect(actorEffect);
                    continue;
                }

                // 攻击者在 AddChild（触发 _Ready）之前注入，保证特效 _Ready 能读到（如朝敌翻转）
                if (fx is Kuros.Fx.IAttackerProvider provider)
                    provider.Attacker = Attacker;

                GetParent()?.AddChild(fx);
                if (fx is Node2D fx2D)
                {
                    // 落地特效也要 TopLevel：它属于"落点"这个世界位置，不该继承滑槽的位移
                    fx2D.TopLevel = true;
                    fx2D.GlobalPosition = GlobalPosition;
                }
            }

            _landingIndicator?.QueueFree();
            QueueFree();
        }
    }
}
