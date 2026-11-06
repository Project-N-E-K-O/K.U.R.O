using Godot;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 敌人死亡过渡状态：**进入时**播放击倒动画（特效可在进入时或 `effect` 帧事件生成，见 <see cref="Effects"/>），
    /// 计时结束后切换到 Dead 状态（收尸特效在 <see cref="EnemyDeadState.DeadEffect"/>，两个时点分开）。
    /// **帧事件特效**：death 动画里的 spine `effect` 事件到达时只生成事件帧类型的条目
    /// （信号链 SpineController.gd → effect_triggered → 本状态自订阅，与 PlayerRunState 同款模式）。
    /// </summary>
    public partial class EnemyDyingState : EnemyState
    {
        /// <summary>击倒动画停留时长（秒）：&gt;0 到时切 Dead（收尸特效 + FinalizeDeath 销毁）；
        /// &lt;=0 = 持续尸体：**永不进入 Dead、不销毁**，death 动画是否循环由动画控制器决定
        /// （RogueAI 控制器为循环播放，其余控制器播一次并保持末帧）。</summary>
        [Export(PropertyHint.Range, "0,10,0.01")] public float DeathDuration = 0.8f;
        [Export] public bool FreezeMotion = true;

        /// <summary>状态特效：每条独立选择进入状态立即生成，或 death 动画 effect 事件生成。
        /// 事件次数限制仅作用于动画事件条目；进入状态条目每次 Enter 生成一次。</summary>
        [Export] public Godot.Collections.Array<StateEffectEntry> Effects { get; set; } = new();

        /// <summary>本次 Dying 内帧事件最多生成几轮特效；0 = 不限制（默认，保持旧行为）。
        /// 持续尸体（DeathDuration&lt;=0）循环播放 death 时事件会反复到达，可用它压成只生成 N 次。</summary>
        [Export(PropertyHint.Range, "0,20,1")] public int MaxEffectTriggers = 0;

        private float _timer;
        private int _effectTriggerCount;
        // 帧事件订阅：状态自己持有——进 Dying 连接、退出断开（与 PlayerRunState 的 effect_triggered 用法同款）
        private Node? _spineEventSource;
        private Callable _effectTriggeredCallable;
        private bool _effectSignalConnected;

        public override void Enter()
        {
            _timer = DeathDuration;
            _effectTriggerCount = 0;
            Enemy.AttackTimer = 0f;
            _spawnMarkerIndex = 0;   // 多锚点轮换从头开始（语义同攻击模板 TryStart 归零）

            // 禁用敌人碰撞体
            Enemy.DisableCollisionShape();

            // 帧事件特效：订阅 death 动画的 `effect` 事件。连接先于播放——death 动画由动画控制器
            // 在发现 Dying 状态后的下一帧才切换，本处 Enter 先执行，事件不会错过
            if (HasAnimationEffects())
                ConnectSpineEffectSignal();

            if (FreezeMotion)
            {
                Enemy.Velocity = Vector2.Zero;
                Enemy.MoveAndSlide();
            }

            if (Enemy.GetNodeOrNull<EnemyFallController>("FallController")?.HasCorpseFallen != true)
                SpawnEffects(StateEffectTiming.OnStateEnter);
        }

        public override void Exit()
        {
            DisconnectSpineEffectSignal();
        }

        public override void PhysicsUpdate(double delta)
        {
            if (FreezeMotion)
            {
                Enemy.Velocity = Vector2.Zero;
                Enemy.MoveAndSlide();
            }

            // DeathDuration <= 0：持续尸体模式，停留在 Dying，永不进入 Dead（因此不销毁）。
            if (DeathDuration <= 0f)
            {
                // 致死 Hit 期间已落坑的尸体，随后才进入 Dying 时也必须完成收尾。
                if (Enemy.GetNodeOrNull<EnemyFallController>("FallController")?.HasCorpseFallen == true)
                    Enemy.FinalizeDeath();
                return;
            }

            _timer -= (float)delta;
            if (_timer <= 0f)
                ChangeState("Dead");
        }

        // ── 帧事件特效 ────────────────────────────────────────────────────

        /// <summary>spine `effect` 帧事件到达（SpineController.gd 对骨架里命名 `effect` 的事件发出）：
        /// 只生成配置为 OnAnimationEffect 的条目。</summary>
        private void OnSpineEffectTriggered(string animationName, float eventTime)
        {
            if (Enemy.GetNodeOrNull<EnemyFallController>("FallController")?.HasCorpseFallen == true) return;
            // 0 = 不限制；>0 时本次 Dying 累计触发到上限后不再生成（循环 death 防刷屏）
            if (MaxEffectTriggers > 0 && _effectTriggerCount >= MaxEffectTriggers) return;
            _effectTriggerCount++;

            SpawnEffects(StateEffectTiming.OnAnimationEffect);
        }

        private bool HasAnimationEffects()
        {
            if (Effects == null) return false;
            foreach (var entry in Effects)
                if (entry?.Scene != null && entry.SpawnTiming == StateEffectTiming.OnAnimationEffect)
                    return true;
            return false;
        }

        private void SpawnEffects(StateEffectTiming timing)
        {
            if (Effects == null) return;
            foreach (var entry in Effects)
                if (entry != null && entry.SpawnTiming == timing)
                    SpawnEffectEntry(entry);
        }

        private void ConnectSpineEffectSignal()
        {
            _spineEventSource = FindSpineEventSource();
            if (_spineEventSource == null)
            {
                GD.PushWarning($"{Name}: 配置了死亡帧特效但找不到带 effect_triggered 信号的 SpineSprite，帧事件不会触发");
                return;
            }

            _effectTriggeredCallable = Callable.From<string, float>(OnSpineEffectTriggered);
            _spineEventSource.Connect("effect_triggered", _effectTriggeredCallable);
            _effectSignalConnected = true;
        }

        private void DisconnectSpineEffectSignal()
        {
            if (_effectSignalConnected && _spineEventSource != null && GodotObject.IsInstanceValid(_spineEventSource)
                && _spineEventSource.IsConnected("effect_triggered", _effectTriggeredCallable))
                _spineEventSource.Disconnect("effect_triggered", _effectTriggeredCallable);

            _effectSignalConnected = false;
            _spineEventSource = null;
        }

        /// <summary>寻找敌人主 Spine（约定名 "SpineSprite"；描边镜像实例带同名信号但自行短路不 emit）——
        /// 直接子节点优先、找不到再递归兜底；没有该信号 = 返回 null（非 Spine 敌人自然不触发）。</summary>
        private Node? FindSpineEventSource()
        {
            var node = Enemy.GetNodeOrNull<Node>("SpineSprite")
                ?? Enemy.FindChild("SpineSprite", recursive: true, owned: false);
            return node != null && node.HasSignal("effect_triggered") ? node : null;
        }
    }
}
