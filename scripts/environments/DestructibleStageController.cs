using System;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Environments
{
    /// <summary>伤害阵营声明（对应 effect 准则的既有对象类别，经组声明被 DamageDispatcher.GetFaction 识别）。</summary>
    public enum DamageFactionDeclaration
    {
        /// <summary>不加组——GetFaction 返回 None，每道阵营过滤都放行（旧行为）。</summary>
        None = 0,
        /// <summary>敌人类：加 enemies 组。玩家攻击列表（默认 Enemy|WorldItem）天然命中；
        /// 敌侧特效默认 Player|WorldItem → 被过滤（敌人打不碎它）。</summary>
        Enemy = 1,
        /// <summary>可破坏物类：加 world_items + damage_receivable（与 DestructibleObject 同语义——
        /// 任何攻击方都能打）。</summary>
        WorldItem = 2,
    }

    /// <summary>
    /// 多阶段受损物动画控制器（Gate / Jar_mozhua 共用）。**GameActor 子类**——世界里一切可受击物统一走
    /// GameActor 契约（effect 准则的默认通道）：组扫描/瞄准/命中事件总线/ActorEffect 等 GameActor-gated
    /// 系统全部天然可达，不再有"非 actor 受体"的解析缺口（Boomerang/ECore 的 HitArea→GameActor 解析、
    /// 声波/爆炸的 enemies 组扫描等都直接命中本物）。
    ///
    /// 血条体系即阶段体系：MaxHealth = 最后一段阈值（_Ready 从 Stages 推导）；累计伤害 = MaxHealth − CurrentHealth。
    /// 规则（含防跳段保护）：
    ///   · 受击（非破壳动画期）→ 累计伤害 + 播本段受击动画（空 = 不播）；**每次命中都从头重播受击动画**
    ///     （高频伤害持续刷新反馈，受击动画不构成免伤窗口）、动画播完自动回本段常态；
    ///   · 累计伤害 ≥ 本段阈值 → 播本段破壳动画（**破壳动画播放期间 = 免伤窗口**）→ 进入下一段常态；
    ///     **单次受击最多推进一段、溢出伤害作废**（钳制保证累计恰好落在阈值）——任何单次伤害都不可能
    ///     跳过中间段/动画，击杀所需累计恒等于最后一段阈值（即配置的"总血量"口径）；
    ///   · **阈值 &lt;= 0 = 免命中自动推进**：进入本段（_Ready 首段 / 上一段动画结束）立即播本段
    ///     BreakAnimation 并前进到下一段；末段 &lt;= 0 进入即走终态（Die → 播末段 BreakAnimation）；
    ///   · 达到最后一段阈值 → base 血量归零走 Die（IsDeadOrDying 全部生死门语义正常）→
    ///     <see cref="OnDeathFinalized"/> 覆写接管：播**最后一段的 <see cref="DestructibleStage.BreakAnimation"/>**
    ///     （终态动画 = 最后一段的 break，不再有独立字段）、**关闭命中判定**（HitArea 全禁）、
    ///     **尸体保留**（与旧 IBarrier 版行为一致；控制台 KillForced 可回收）；
    ///   · **免伤窗口** = 仅破壳动画播放期间——期间新命中被拒绝（不累计、不结算）。
    ///
    /// 契约收窄：不吃任何 ActorEffect（<see cref="CanBeAffected"/> 只放行 null=纯伤害）、不吃击退
    /// （<see cref="AllowsKnockback"/>）、伤害**不走合并窗口**（保持逐击节奏，传 bypassMergeWindow=true）。
    /// 本体 CollisionShape2D 不在这里动：是否/何时关闭属于动画编排，各物件在自死亡动画里用轨道 key
    /// （如 Gate 的 gate_knockback 在 0.1s 处关闭；Jar 不配即保持碰撞）。
    ///
    /// IBarrier 接口保留（投掷物屏障拦截 ResolveBarrier 依赖 `is IBarrier`）；其 TakeDamage(float)
    /// 委托到 int 主管线。伤害经 DamageDispatcher 的 GameActor 分支直达（DealToGameActor → TakeDamage(int)）。
    /// </summary>
    public partial class DestructibleStageController : GameActor, IBarrier
    {
        [ExportCategory("Animation 动画")]
        /// <summary>动画播放器路径（相对本节点）。</summary>
        [Export] public NodePath AnimationPlayerPath { get; set; } = new("AnimationPlayer");
        /// <summary>阶段列表（**顺序 = 推进顺序**）：每段带自己的动画名与累计伤害阈值。
        /// 单段关卡就填 1 项（该项阈值 = 死亡线）。</summary>
        [Export] public Godot.Collections.Array<DestructibleStage> Stages { get; set; } = new();
        /// <summary>伤害阵营声明（组注册，见 <see cref="DamageFactionDeclaration"/>）：gate=WorldItem、
        /// jar=Enemy 在各自场景设置；不配置 = None（不加组，旧行为）。</summary>
        [Export] public DamageFactionDeclaration DeclaredFaction { get; set; } = DamageFactionDeclaration.None;

        private AnimationPlayer? _animPlayer;
        /// <summary>权威累计伤害（int 计数，无浮点误差）；血量同步镜像为 MaxHealth − 本值。</summary>
        private int _cumulativeDamage;
        private int _stageIndex;
        /// <summary>当前正在播的一次性动画：只有破壳动画锁伤害；受击动画每击重播、不锁。</summary>
        private enum OneShotKind { None, Hit, Break }
        private OneShotKind _oneShot;
        /// <summary>终态：不再响应任何命中（尸体保留直到 KillForced / 场景卸载）。</summary>
        private bool _dead;
        /// <summary>KillForced 强杀期间跳过防跳段钳制。</summary>
        private bool _forceKill;

        public override void _Ready()
        {
            // 血条口径 = 最后一段阈值（必须在 base._Ready 记录 BaseMaxHealth/CurrentHealth 之前生效）
            if (Stages != null && Stages.Count > 0)
                MaxHealth = Mathf.RoundToInt(Stages[Stages.Count - 1].Threshold);

            base._Ready();              // 血量初始化 + GameActor 组件解析（Spine/Sprite/StateMachine/EffectController）
            ApplyFactionDeclaration();  // 组声明独立于动画配置——Stages/动画缺失也要先声明阵营

            _animPlayer = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
            if (_animPlayer == null)
            {
                GD.PushWarning($"[{nameof(DestructibleStageController)}] 未找到 AnimationPlayer，路径：{AnimationPlayerPath}");
                return;
            }

            if (Stages == null || Stages.Count == 0)
            {
                GD.PushWarning($"[{nameof(DestructibleStageController)}] 未配置 Stages，本物不会响应任何动画/阶段推进");
                return;
            }

            _animPlayer.AnimationFinished += OnAnimationFinished;
            EnterCurrentStage();
        }

        public override void _ExitTree()
        {
            if (_animPlayer != null)
                _animPlayer.AnimationFinished -= OnAnimationFinished;
            base._ExitTree();
        }

        // ── 伤害 ──────────────────────────────────────────────────

        /// <summary>
        /// 伤害接收入口（GameActor int 主管线：DamageDispatcher 的 GameActor 分支直接调这里）。
        /// 防跳段：非终段把单次伤害钳到"当前段阈值以内"（溢出作废）——任何单次伤害都不可能跳过中间段/动画。
        /// 钳制按 base 同口径预折算攻击者放大（floor 难度系数），预除回填 base 避免二次放大失真；
        /// base 结算后把残余误差还回血量，保证累计恰好落在阈值。合并窗口不适用（恒 bypassMergeWindow=true）。
        /// </summary>
        public override bool TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null,
            DamageSource damageSource = DamageSource.DirectAttack, bool bypassMergeWindow = false)
        {
            if (_dead || _oneShot == OneShotKind.Break || Stages == null || Stages.Count == 0) return false;

            int last = Stages.Count - 1;
            int cap = Mathf.RoundToInt(Stages[_stageIndex].Threshold) - _cumulativeDamage;

            // 与 base.TakeDamage 同口径的攻击者放大（见 GameActor.cs 楼层难度段：唯一必经在受击侧）
            float mult = attacker is SampleEnemy se ? se.OutgoingDamageMultiplier : 1f;
            if (mult < 0.01f) mult = 0.01f;
            int scaled = (int)Math.Min(int.MaxValue, Math.Round(damage * mult));

            int allowed = _forceKill || _stageIndex >= last ? scaled : Mathf.Max(1, Math.Min(scaled, cap));

            // 预除回填：base 会对参数再乘一次 mult（ceil 保证换算后 ≥ allowed，余量由下面的"还血"修平）
            int input = mult == 1f ? allowed : Mathf.Max(1, (int)Math.Ceiling(allowed / mult));

            int before = CurrentHealth;
            if (!base.TakeDamage(input, attackOrigin, attacker, damageSource, true)) return false;

            _cumulativeDamage += before - CurrentHealth;   // 实际扣血；血量与累计同步

            if (IsDeadOrDying) return true;   // 终段打穿：Die → OnDeathFinalized 已接管终态动画

            if (_stageIndex < last && _cumulativeDamage >= Mathf.RoundToInt(Stages[_stageIndex].Threshold))
            {
                // 钳到阈值（ceil 预除的残余误差还回血量）——单次受击最多推进一段、溢出作废
                int over = _cumulativeDamage - Mathf.RoundToInt(Stages[_stageIndex].Threshold);
                if (over > 0)
                {
                    _cumulativeDamage -= over;
                    CurrentHealth += over;
                }

                string breakAnim = Stages[_stageIndex].BreakAnimation;
                _stageIndex++;
                _oneShot = OneShotKind.None;
                if (PlayAnim(breakAnim))
                    _oneShot = OneShotKind.Break;
                else
                    EnterCurrentStage();   // 破壳动画缺省：进入新段（Threshold<=0 会继续自动推进）
                return true;
            }

            // 同段受击：每次都从头重播（PlayAnim 内部先 Stop 再 Play）；空受击动画 = 不播不锁
            _oneShot = PlayAnim(Stages[_stageIndex].HitAnimation) ? OneShotKind.Hit : OneShotKind.None;
            return true;
        }

        /// <summary>IBarrier 兼容通道（投掷物屏障拦截等遗留调用）：委托到 int 主管线。
        /// GameActor 化后 DamageDispatcher 走 int 分支，此路基本不再触发。
        /// **必须显式传齐参数**——单参调用会因重载裁决再次绑定回本 float 版（无限递归）。</summary>
        public void TakeDamage(float damage)
        {
            TakeDamage(Mathf.Max(1, Mathf.RoundToInt(damage)), null, null);
        }

        // ── GameActor 契约收窄 ────────────────────────────────────

        /// <summary>只接受纯伤害（effect == null）；任何 ActorEffect 一律拒绝——受损物不吃眩晕/灼烧/减速，
        /// 与转换前"只能结算伤害"的行为一致。null 恒 true 也保证不被瞄准系统的免疫剔除误判（§八准则）。</summary>
        public override bool CanBeAffected(ActorEffect? effect) => effect == null;

        /// <summary>受损物不吃击退/位移（击退写入会被 base 的静默门拦下）。</summary>
        protected override bool AllowsKnockback() => false;

        /// <summary>供 AnimationPlayer 方法轨道调用：直接销毁自身（不做终态动画/掉落/事件）。
        /// 与 <see cref="KillForced"/> 不同，不经过 Die/FinalizeDeath；适合临时生成物“演完即退场”。</summary>
        public void DestroySelf()
        {
            _dead = true;
            DisableHitDetection();
            QueueFree();
        }

        /// <summary>强制击杀（控制台清场）：跳过防跳段钳制直接打满；已死亡则回收尸体
        /// （尸体保留策略下，这是唯一的内存回收通道）。</summary>
        public override void KillForced()
        {
            if (IsDeadOrDying)
            {
                if (IsInstanceValid(this)) QueueFree();
                return;
            }

            _forceKill = true;
            try { DeliverKillDamage(); }
            finally { _forceKill = false; }

            if (!IsDeadOrDying && IsInstanceValid(this)) QueueFree();
        }

        // ── 动画 ──────────────────────────────────────────────────

        /// <summary>进入当前段的常态：Threshold &gt; 0 → 播本段 idle；
        /// Threshold &lt;= 0 → **无需命中自动推进**——非末段播本段 BreakAnimation 并前进一段
        /// （动画结束后由 <see cref="OnAnimationFinished"/> 继续处理下一段；下一段若也是 0 会继续链式推进），
        /// 末段则视为进入即达死亡线，直接走终态（Die → <see cref="OnDeathFinalized"/> 播末段 BreakAnimation）。</summary>
        private void EnterCurrentStage()
        {
            if (_dead || Stages == null || Stages.Count == 0) return;

            int last = Stages.Count - 1;
            if (Stages[_stageIndex].Threshold > 0f)
            {
                PlayAnim(Stages[_stageIndex].IdleAnimation);
                return;
            }

            if (_stageIndex >= last)
            {
                GD.PushWarning($"[{nameof(DestructibleStageController)}] {Name}: 最后一段 Threshold<=0，进入即走终态");
                Die();
                return;
            }

            // 免命中自动破壳：播本段 BreakAnimation 并推进；缺省动画则直接落到下一段（可能连续推进）
            string breakAnim = Stages[_stageIndex].BreakAnimation;
            _stageIndex++;
            _oneShot = OneShotKind.None;
            if (PlayAnim(breakAnim))
                _oneShot = OneShotKind.Break;
            else
                EnterCurrentStage();
        }

        /// <summary>一次性动画播完：解锁并**自动回本段常态**（受击动画 → 本段 idle；破壳动画 → 新段 idle——
        /// 跨段时 _stageIndex 已先行推进）。idle 自身播完不处理。终态后不再路由（尸体停在终态动画末帧）。</summary>
        private void OnAnimationFinished(StringName animName)
        {
            if (_dead || _oneShot == OneShotKind.None) return;

            _oneShot = OneShotKind.None;
            EnterCurrentStage();
        }

        /// <summary>终态：**不复用 base**（base 会立刻 QueueFree + 掉落 + 清效果）——播最后一段的
        /// BreakAnimation（= 终态动画）、关闭命中判定、尸体保留（与旧 IBarrier 版行为一致：
        /// gate 停在 knockback 末帧、罐子留残骸）。
        /// 死亡旗标（IsDeadOrDying 等）已由 base 的 Die/FinalizeDeath 在进入本方法前置位。</summary>
        protected override void OnDeathFinalized()
        {
            _dead = true;
            DisableHitDetection();
            string deathAnim = Stages is { Count: > 0 } ? Stages[Stages.Count - 1].BreakAnimation : "";
            if (!PlayAnim(deathAnim))
                GD.PushWarning($"[{nameof(DestructibleStageController)}] 终态动画（最后一段 BreakAnimation）\"{deathAnim}\" 不存在，保持锁死");
        }

        /// <summary>终态：关闭命中判定——HitArea 的形状/监听全禁（玩家攻击再也找不到它）。
        /// 本体 CollisionShape2D **不在这里动**：是否/何时关闭属于动画编排，各物件在自死亡动画里用
        /// 轨道 key（如 Gate 的 gate_knockback 在 0.1s 处关闭；Jar 不配即保持碰撞）。</summary>
        private void DisableHitDetection()
        {
            if (GetNodeOrNull<Area2D>("HitArea") is { } hitArea)
            {
                hitArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
                hitArea.SetDeferred(Area2D.PropertyName.Monitorable, false);
                foreach (var child in hitArea.GetChildren())
                    if (child is CollisionShape2D hitShape)
                        hitShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
            }
        }

        /// <summary>播放动画；动画不存在 / 播放器缺失返回 false（调用方据此决定是否锁）。</summary>
        private bool PlayAnim(string animName)
        {
            if (_animPlayer == null || string.IsNullOrEmpty(animName) || !_animPlayer.HasAnimation(animName))
                return false;

            _animPlayer.Stop();
            _animPlayer.Play(animName);
            return true;
        }

        /// <summary>按声明加组（组声明 = effect 准则的类别注册；进入树后尽早执行，
        /// 让所有 GetNodesInGroup 消费者立即看到本物）。</summary>
        private void ApplyFactionDeclaration()
        {
            switch (DeclaredFaction)
            {
                case DamageFactionDeclaration.Enemy:
                    AddToGroup("enemies");
                    break;
                case DamageFactionDeclaration.WorldItem:
                    AddToGroup("world_items");
                    AddToGroup("damage_receivable");   // 可破坏物类语义：无视攻击方分类过滤
                    break;
            }
        }
    }
}
