using Godot;
using System;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 黑洞效果（ActorEffect）：
    /// Area2D 为伤害区域（从 .tscn 获取），敌人进入后每隔 DamageInterval 秒受到 DamagePerTick 点伤害。
    /// AdsorbRadius 为吸附范围，范围内的敌人被物理吸附和直接位移牵引向黑洞中心。
    /// 支持多个黑洞重叠。
    /// </summary>
    [GlobalClass]
    public partial class BlackHoleEffect : ActorEffect, Kuros.Core.Effects.IWorldSpawnable
    {
        private const uint EnemiesLayerMask = 2u;

        /// <summary>
        /// 由 SpawnThrowDestroyEffects 在应用前设置，将 Area2D 定位到抛物落点。
        /// </summary>
        public Vector2 WorldSpawnPosition { get; set; } = Vector2.Zero;

        [ExportGroup("Damage")]
        /// <summary>每次造成的伤害量。</summary>
        [Export(PropertyHint.Range, "1,999,1")]
        public int DamagePerTick { get; set; } = 10;

        /// <summary>伤害间隔（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float DamageInterval { get; set; } = 1.0f;

        [ExportGroup("Adsorb")]
        /// <summary>吸附范围半径（物理吸附）。</summary>
        [Export(PropertyHint.Range, "50,1000,10")]
        public float AdsorbRadius { get; set; } = 300f;

        /// <summary>吸附速度拉扯力量。</summary>
        [Export(PropertyHint.Range, "100,2000,50")]
        public float AdsorbForce { get; set; } = 400f;

        /// <summary>吸附时的速度衰减（<1 减速）。</summary>
        [Export(PropertyHint.Range, "0.5,1,0.01")]
        public float AdsorbVelocityDamping { get; set; } = 0.95f;

        /// <summary>直接位置牵引最小速度（像素/秒），保证可见吸附效果。</summary>
        [Export(PropertyHint.Range, "10,2000,10")]
        public float PullMinPixelsPerSecond { get; set; } = 420f;

        /// <summary>进入中心区后快速收束的半径。</summary>
        [Export(PropertyHint.Range, "0,200,1")]
        public float CenterSnapRadius { get; set; } = 72f;

        /// <summary>中心区额外速度衰减。</summary>
        [Export(PropertyHint.Range, "0.5,0.99,0.01")]
        public float CenterSnapDamping { get; set; } = 0.72f;

        /// <summary>初始强吸阶段持续时间（不造成伤害）。</summary>
        [Export(PropertyHint.Range, "0,3,0.05")]
        public float PullOnlyDuration { get; set; } = 0.0f;

        /// <summary>初始强吸阶段吸附力倍率。</summary>
        [Export(PropertyHint.Range, "1,6,0.1")]
        public float PullBurstMultiplier { get; set; } = 3.6f;

        private Area2D? _damageArea;
        private double _damageTickTimer = 0.0;
        private double _elapsed = 0.0;
        // 区域内的敌人 → 独立计时器
        private readonly Dictionary<GameActor, float> _damageTimers = new();
        private bool _isWorldSpawned = false;
        private Vector2 _blackHoleCenter = Vector2.Zero;

        protected override void OnApply()
        {
            base.OnApply();

            ProcessPhysicsPriority = 100;

            EffectId = $"black_hole_{Guid.NewGuid()}";

            _isWorldSpawned = WorldSpawnPosition != Vector2.Zero;
            _blackHoleCenter = _isWorldSpawned ? WorldSpawnPosition : Actor?.GlobalPosition ?? Vector2.Zero;

            _damageArea = GetNodeOrNull<Area2D>("Area2D");
            if (_damageArea == null) return;

            if (_isWorldSpawned)
                _damageArea.GlobalPosition = _blackHoleCenter;

            _damageArea.CollisionMask = EnemiesLayerMask;
            _damageArea.Monitoring = true;
            _damageArea.BodyEntered += OnBodyEntered;
            _damageArea.BodyExited += OnBodyExited;
        }

        protected override void OnTick(double delta)
        {
            _elapsed += delta;

            // 非投掷场景：同步伤害区域位置跟随 Actor
            if (!_isWorldSpawned && _damageArea != null && Actor != null)
            {
                _damageArea.GlobalPosition = Actor.GlobalPosition;
                _blackHoleCenter = Actor.GlobalPosition;
            }

            // 更新伤害计时
            _damageTickTimer += delta;
            if (_elapsed >= PullOnlyDuration && _damageTimers.Count > 0)
            {
                var toRemove = new List<GameActor>();
                foreach (var kvp in _damageTimers)
                {
                    var enemy = kvp.Key;
                    if (!IsInstanceValid(enemy) || enemy!.IsDead)
                    {
                        toRemove.Add(enemy!);
                        continue;
                    }
                    _damageTimers[enemy] = kvp.Value + (float)delta;
                    if (_damageTimers[enemy] >= DamageInterval)
                    {
                        _damageTimers[enemy] = 0f;
                        enemy.TakeDamage(DamagePerTick, _blackHoleCenter, Actor, Kuros.Core.Events.DamageSource.AreaEffect);
                    }
                }
                foreach (var e in toRemove) RemoveEnemy(e);
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            if (IsExpired) return;
            AttractNearbyActors(delta);
        }

        protected override void OnExpire()
        {
            Cleanup();
            base.OnExpire();
        }

        public override void OnRemoved()
        {
            Cleanup();
            base.OnRemoved();
        }

        private void OnBodyEntered(Node2D body)
        {
            if (body is not GameActor enemy) return;
            if (_damageTimers.ContainsKey(enemy)) return;
            _damageTimers[enemy] = 0f;

            // 立刻造成首次伤害（跳过纯吸附阶段）
            if (_elapsed >= PullOnlyDuration && !enemy.IsDead)
                enemy.TakeDamage(DamagePerTick, _blackHoleCenter, Actor, Kuros.Core.Events.DamageSource.AreaEffect);
        }

        private void OnBodyExited(Node2D body)
        {
            if (body is not GameActor enemy) return;
            RemoveEnemy(enemy);
        }

        private void RemoveEnemy(GameActor enemy)
        {
            _damageTimers.Remove(enemy);
        }

        /// <summary>
        /// 吸附 AdsorbRadius 范围内的敌人（物理帧执行，参考 GravityGrenadeBlackHole）。
        /// 同时使用直接位移牵引和速度拉扯，保证可见吸附效果。
        /// </summary>
        private void AttractNearbyActors(double delta)
        {
            var actors = CollectActorsInRadius();
            bool inPullOnlyPhase = _elapsed < PullOnlyDuration;
            float dt = (float)delta;

            foreach (var actor in actors)
            {
                if (!IsInstanceValid(actor) || actor.IsDead) continue;

                Vector2 direction = (_blackHoleCenter - actor.GlobalPosition).Normalized();
                float distance = _blackHoleCenter.DistanceTo(actor.GlobalPosition);

                float effectiveRadius = Mathf.Max(AdsorbRadius, 1f);
                float t = 1.0f - Mathf.Clamp(distance / effectiveRadius, 0, 1);
                float attractForce = Mathf.Lerp(AdsorbForce * 0.38f, AdsorbForce, t);
                if (inPullOnlyPhase) attractForce *= PullBurstMultiplier;

                // 直接位移牵引：确保可见吸附，不会被敌人 AI 的速度覆盖
                float pullSpeed = Mathf.Lerp(PullMinPixelsPerSecond, Mathf.Max(PullMinPixelsPerSecond, AdsorbForce), t);
                if (inPullOnlyPhase) pullSpeed *= PullBurstMultiplier;
                actor.GlobalPosition = actor.GlobalPosition.MoveToward(_blackHoleCenter, pullSpeed * dt);

                if (actor is CharacterBody2D characterBody)
                {
                    characterBody.Velocity += direction * attractForce * dt;
                    characterBody.Velocity *= AdsorbVelocityDamping;

                    if (distance <= CenterSnapRadius * 1.5f)
                    {
                        characterBody.GlobalPosition = characterBody.GlobalPosition.Lerp(_blackHoleCenter, 0.5f);
                        characterBody.Velocity *= CenterSnapDamping;
                    }
                }
            }
        }

        /// <summary>
        /// 收集 AdsorbRadius 范围内的敌人（组扫描 + 物理查询兜底）。
        /// </summary>
        private List<GameActor> CollectActorsInRadius()
        {
            var actors = new List<GameActor>();
            var seen = new HashSet<ulong>();

            // 先扫描 enemies 组
            var tree = GetTree();
            if (tree != null)
            {
                foreach (Node node in tree.GetNodesInGroup("enemies"))
                {
                    if (node is not GameActor actor) continue;
                    if (!IsInstanceValid(actor) || actor == Actor || actor.IsDead) continue;
                    if (_blackHoleCenter.DistanceTo(actor.GlobalPosition) > AdsorbRadius) continue;
                    if (seen.Add(actor.GetInstanceId())) actors.Add(actor);
                }
            }

            // 物理查询兜底：防止未加入 enemies 组的敌人漏检
            var space = GetTree()?.Root.GetWorld2D()?.DirectSpaceState;
            if (space != null)
            {
                var query = new PhysicsShapeQueryParameters2D
                {
                    Shape = new CircleShape2D { Radius = AdsorbRadius },
                    Transform = new Transform2D(0, _blackHoleCenter),
                    CollisionMask = EnemiesLayerMask,
                    CollideWithBodies = true,
                    CollideWithAreas = false
                };

                foreach (var hit in space.IntersectShape(query, 64))
                {
                    if (!hit.TryGetValue("collider", out Variant v)) continue;
                    var collider = v.As<GodotObject>();
                    GameActor? actor = collider as GameActor;
                    if (actor == null && collider is Node n)
                    {
                        Node? cur = n;
                        while (cur != null && actor == null) { actor = cur as GameActor; cur = cur.GetParent(); }
                    }
                    if (actor == null || !IsInstanceValid(actor) || actor == Actor || actor.IsDead) continue;
                    if (seen.Add(actor.GetInstanceId())) actors.Add(actor);
                }
            }

            return actors;
        }

        private void Cleanup()
        {
            if (_damageArea != null && IsInstanceValid(_damageArea))
            {
                _damageArea.BodyEntered -= OnBodyEntered;
                _damageArea.BodyExited -= OnBodyExited;
            }
            _damageTimers.Clear();
        }
    }
}

