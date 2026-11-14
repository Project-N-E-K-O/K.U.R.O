using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Actors.Heroes;
using Kuros.Items;
using Kuros.Items.Attributes;
using Kuros.Items.Weapons;
using Kuros.Systems.Memory;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// Collects world/runtime data and exposes AI-friendly state snapshots.
    /// </summary>
    [GlobalClass]
    public partial class GameStateProvider : Node
    {
        [Export] public NodePath PlayerPath { get; set; } = new();
        [Export] public Godot.Collections.Array<NodePath> CompanionPaths { get; set; } = new();
        [Export] public string EnemyGroupName { get; set; } = "enemies";

        [ExportGroup("Under Attack")]
        [Export] public float UnderAttackWindowSeconds { get; set; } = 0.75f;
        [Export] public bool TreatHitStateAsUnderAttack { get; set; } = true;

        private SamplePlayer? _cachedPlayer;
        private Kuros.Scenes.BattleSceneManager? _cachedBattleScene;

        // ── 距离差分状态（接近方向判定用）：保存上次快照的最近敌人距离与时间戳 ──
        private float _lastNearestDistance = -1f;
        private ulong _lastCaptureAtMs;
        /// <summary>速度投影阈值（px/秒）：Velocity 在目标方向上的分量超过此值视为"正在接近"。</summary>
        private const float ApproachVelocityThreshold = 50f;
        /// <summary>距离变化率阈值（px/秒）：超过视为"接近中"，低于负值视为"拉开"。</summary>
        private const float ClosingDeltaThreshold = 30f;
        private const float RecedingDeltaThreshold = -30f;

        public GameState CaptureGameState()
        {
            var player = ResolvePlayer();
            if (player == null)
            {
                return new GameState
                {
                    TimestampMs = Time.GetTicksMsec(),
                    PlayerStateName = "missing_player",
                    NearestEnemyDistance = -1f,
                    AverageEnemyDistance = -1f
                };
            }

            var companions = ResolveCompanions(player);
            var (enemyCount, nearestDistance, averageDistance, nearestEnemy, enemies) = ResolveEnemyMetrics(player);
            float distanceDelta = ComputeNearestDistanceDelta(nearestDistance);
            string approachSituation = ResolveApproachSituation(player, nearestEnemy, nearestDistance, distanceDelta);
            var (levelName, levelDescription) = ResolveLevelInfo();
            var (backpackItemCount, backpackOccupiedSlots, backpackSlots) = ResolveBackpack(player);
            var quickBarState = ResolveQuickBarState(player);
            var memory = GameMemoryService.Instance;

            return new GameState
            {
                TimestampMs = Time.GetTicksMsec(),
                PlayerHp = player.CurrentHealth,
                PlayerMaxHp = player.MaxHealth,
                PlayerUnderAttack = ResolvePlayerUnderAttack(player),
                PlayerStateName = player.StateMachine?.CurrentState?.Name ?? string.Empty,
                AliveEnemyCount = enemyCount,
                NearestEnemyDistance = nearestDistance,
                NearestEnemyDistanceDelta = distanceDelta,
                ApproachSituation = approachSituation,
                AverageEnemyDistance = averageDistance,
                Enemies = enemies,
                LevelName = levelName,
                LevelDescription = levelDescription,
                BackpackItemCount = backpackItemCount,
                BackpackOccupiedSlots = backpackOccupiedSlots,
                BackpackSlots = backpackSlots,
                FlyingThrowWeaponCount = player.InventoryComponent?.ReservedQuickBarSlots.Count ?? 0,
                QuickBarSlotCount = quickBarState.slotCount,
                QuickBarOccupiedSlots = quickBarState.occupiedSlots,
                SelectedQuickBarSlotIndex = quickBarState.selectedSlotIndex,
                SelectedQuickBarItemId = quickBarState.selectedItemId,
                SelectedQuickBarItemName = quickBarState.selectedItemName,
                QuickBarSlots = quickBarState.slots,
                Companions = companions,
                MemoryEvents = memory?.LatestSessionTexts(8) ?? new List<string>(),
                PersistentMemorySummary = memory?.PersistentSummaryText() ?? string.Empty
            };
        }

        public Godot.Collections.Dictionary<string, Variant> GetAiInputDictionary()
        {
            return CaptureGameState().ToAiInputDictionary();
        }

        /// <summary>
        /// GDScript-friendly alias — avoids "AI" abbreviation mapping ambiguity.
        /// GDScript calls this as: provider.get_state_dict()
        /// </summary>
        public Godot.Collections.Dictionary<string, Variant> GetStateDict()
        {
            return GetAiInputDictionary();
        }

        public string GetAiInputJson(bool pretty = true)
        {
            return CaptureGameState().ToAiInputJson(pretty);
        }

        public string GetAiPromptText()
        {
            return CaptureGameState().ToAiPromptText();
        }

        private SamplePlayer? ResolvePlayer()
        {
            if (_cachedPlayer != null && IsInstanceValid(_cachedPlayer) && _cachedPlayer.IsInsideTree())
            {
                return _cachedPlayer;
            }

            if (!PlayerPath.IsEmpty)
            {
                _cachedPlayer = GetNodeOrNull<SamplePlayer>(PlayerPath);
                if (_cachedPlayer != null)
                {
                    return _cachedPlayer;
                }

                _cachedPlayer = GetNodeOrNull<SamplePlayer>($"../{PlayerPath}");
                if (_cachedPlayer != null)
                {
                    return _cachedPlayer;
                }
            }

            _cachedPlayer = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
            return _cachedPlayer;
        }

        private List<CompanionState> ResolveCompanions(SamplePlayer player)
        {
            var result = new List<CompanionState>();

            if (CompanionPaths.Count > 0)
            {
                foreach (var path in CompanionPaths)
                {
                    if (path == null || path.IsEmpty) continue;
                    var node = GetNodeOrNull<Node>(path) ?? GetNodeOrNull<Node>($"../{path}");
                    if (node is GameActor actor && actor != player && !actor.IsDead && !actor.IsDeathSequenceActive)
                    {
                        result.Add(new CompanionState
                        {
                            Name = actor.Name,
                            CurrentHp = actor.CurrentHealth,
                            MaxHp = actor.MaxHealth
                        });
                    }
                }

                return result;
            }

            var fallbackGroups = new[] { "companions", "allies", "ally", "companion" };
            foreach (string group in fallbackGroups)
            {
                foreach (Node node in GetTree().GetNodesInGroup(group))
                {
                    if (node is not GameActor actor || actor == player) continue;
                    if (actor.IsDead || actor.IsDeathSequenceActive) continue;

                    result.Add(new CompanionState
                    {
                        Name = actor.Name,
                        CurrentHp = actor.CurrentHealth,
                        MaxHp = actor.MaxHealth
                    });
                }

                if (result.Count > 0)
                {
                    break;
                }
            }

            return result;
        }

        private (int count, float nearestDistance, float averageDistance, GameActor? nearestEnemy, List<EnemyState> enemies) ResolveEnemyMetrics(SamplePlayer player)
        {
            var enemies = new List<EnemyState>();
            if (string.IsNullOrWhiteSpace(EnemyGroupName))
            {
                return (0, -1f, -1f, null, enemies);
            }

            int count = 0;
            float distanceSum = 0f;
            float nearest = float.MaxValue;
            GameActor? nearestEnemy = null;

            foreach (Node node in GetTree().GetNodesInGroup(EnemyGroupName))
            {
                if (node is not GameActor actor) continue;
                if (actor.IsDead || actor.IsDeathSequenceActive) continue;

                count++;
                float distance = player.GlobalPosition.DistanceTo(actor.GlobalPosition);
                distanceSum += distance;
                if (distance < nearest)
                {
                    nearest = distance;
                    nearestEnemy = actor;
                }

                enemies.Add(new EnemyState
                {
                    Name = actor.Name,
                    TypeName = actor.GetType().Name,
                    AiDescription = actor.AiDescription,
                    CurrentHp = actor.CurrentHealth,
                    MaxHp = actor.MaxHealth,
                    Distance = distance
                });
            }

            if (count == 0)
            {
                return (0, -1f, -1f, null, enemies);
            }

            return (count, nearest, distanceSum / count, nearestEnemy, enemies);
        }

        /// <summary>关卡信息：BattleSceneManager 的 LevelName/AiLevelDescription 优先（战斗场景配置），
        /// 缺失时回退当前场景名。BattleSceneManager 节点按类型递归查找并缓存（每场景一次）。</summary>
        private (string name, string description) ResolveLevelInfo()
        {
            if (Kuros.Systems.Stage.StageSession.Current?.CurrentConfig is { } stage)
                return (stage.ArrivalName, stage.LevelDescription);

            if (_cachedBattleScene == null || !IsInstanceValid(_cachedBattleScene) || !_cachedBattleScene.IsInsideTree())
            {
                _cachedBattleScene = FindBattleSceneManager(GetTree().CurrentScene);
            }

            var battleScene = _cachedBattleScene;
            if (battleScene != null)
            {
                string name = string.IsNullOrWhiteSpace(battleScene.LevelName)
                    ? (GetTree().CurrentScene?.Name ?? string.Empty)
                    : battleScene.LevelName;
                return (name, battleScene.AiLevelDescription);
            }

            return (GetTree().CurrentScene?.Name ?? string.Empty, string.Empty);
        }

        private static Kuros.Scenes.BattleSceneManager? FindBattleSceneManager(Node? root)
        {
            if (root == null)
            {
                return null;
            }
            if (root is Kuros.Scenes.BattleSceneManager bsm)
            {
                return bsm;
            }
            foreach (Node child in root.GetChildren())
            {
                var found = FindBattleSceneManager(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>最近敌人距离变化率（px/秒，正=接近）：与上次快照差分；无上次记录返回 0。</summary>
        private float ComputeNearestDistanceDelta(float nearestDistance)
        {
            ulong now = Time.GetTicksMsec();
            float delta = 0f;
            if (_lastNearestDistance >= 0f && _lastCaptureAtMs != 0 && now > _lastCaptureAtMs)
            {
                float dt = (now - _lastCaptureAtMs) / 1000f;
                delta = (_lastNearestDistance - nearestDistance) / Mathf.Max(dt, 0.001f);
            }

            _lastNearestDistance = nearestDistance;
            _lastCaptureAtMs = now;
            return delta;
        }

        /// <summary>接近方向判定：组合距离变化率与双方 Velocity 在连线方向上的投影，
        /// 区分"玩家主动接近"与"敌人接近"（战术含义相反，LLM 台词据此定语气）。</summary>
        private static string ResolveApproachSituation(SamplePlayer player, GameActor? nearestEnemy,
            float nearestDistance, float distanceDelta)
        {
            if (nearestEnemy == null || nearestDistance <= 0f)
            {
                return "none";
            }

            Vector2 toEnemy = nearestEnemy.GlobalPosition - player.GlobalPosition;
            if (toEnemy == Vector2.Zero)
            {
                toEnemy = Vector2.Right;
            }
            Vector2 dirToEnemy = toEnemy.Normalized();
            Vector2 dirToPlayer = -dirToEnemy;

            bool playerToward = player.Velocity.Dot(dirToEnemy) > ApproachVelocityThreshold;
            bool enemyToward = nearestEnemy.Velocity.Dot(dirToPlayer) > ApproachVelocityThreshold;

            if (distanceDelta > ClosingDeltaThreshold)
            {
                if (enemyToward && !playerToward) return "enemy_approaching";
                if (playerToward && !enemyToward) return "player_approaching";
                return (enemyToward || playerToward) ? "mutual" : "static";
            }

            return distanceDelta < RecedingDeltaThreshold ? "receding" : "static";
        }

        /// <summary>背包单次遍历：同时产出计数（总物品数/占用槽数）与语义列表（AI 可读），避免二次遍历。</summary>
        private static (int totalItemCount, int occupiedSlots, List<BackpackSlotState> slots) ResolveBackpack(SamplePlayer player)
        {
            var slots = new List<BackpackSlotState>();
            var backpack = player.InventoryComponent?.Backpack;
            if (backpack == null)
            {
                return (0, 0, slots);
            }

            int totalItemCount = 0;
            int occupiedSlots = 0;
            foreach (var stack in backpack.Slots)
            {
                if (stack == null || stack.IsEmpty) continue;
                if (stack.Item.ItemId == "empty_item") continue;

                occupiedSlots++;
                totalItemCount += stack.Quantity;

                slots.Add(new BackpackSlotState
                {
                    ItemId = stack.Item.ItemId,
                    ItemName = stack.Item.DisplayName,
                    Category = stack.Item.Category,
                    Quantity = stack.Quantity,
                    IsWeapon = stack.Item.IsThrowWeapon
                        || string.Equals(stack.Item.Category, "Weapon", StringComparison.OrdinalIgnoreCase),
                    ThrowCooldownRemaining = stack.ThrowCooldownRemaining
                });
            }

            return (totalItemCount, occupiedSlots, slots);
        }

        private static (int slotCount, int occupiedSlots, int selectedSlotIndex, string selectedItemId, string selectedItemName, List<QuickBarSlotState> slots) ResolveQuickBarState(SamplePlayer player)
        {
            var quickBar = player.InventoryComponent?.QuickBar;
            int selectedSlotIndex = player.InventoryComponent?.SelectedQuickBarSlot ?? -1;
            var slots = new List<QuickBarSlotState>();

            if (quickBar == null)
            {
                return (0, 0, selectedSlotIndex, string.Empty, string.Empty, slots);
            }

            int occupiedSlots = 0;
            string selectedItemId = string.Empty;
            string selectedItemName = string.Empty;

            for (int index = 0; index < quickBar.Slots.Count; index++)
            {
                var stack = quickBar.GetStack(index);
                bool hasItem = stack != null && !stack.IsEmpty && stack.Item.ItemId != "empty_item";
                if (hasItem)
                {
                    occupiedSlots++;
                }

                if (index == selectedSlotIndex && hasItem)
                {
                    selectedItemId = stack!.Item.ItemId;
                    selectedItemName = stack.Item.DisplayName;
                }

                slots.Add(new QuickBarSlotState
                {
                    SlotIndex = index,
                    IsSelected = index == selectedSlotIndex,
                    IsOccupied = hasItem,
                    ItemId = hasItem ? stack!.Item.ItemId : string.Empty,
                    ItemName = hasItem ? stack!.Item.DisplayName : string.Empty,
                    Quantity = hasItem ? stack!.Quantity : 0,
                    Category = hasItem ? stack!.Item.Category : string.Empty,
                    Description = hasItem ? TruncateForAi(stack.Item.Description, 80) : string.Empty,
                    AttackPower = hasItem ? ResolveAttackPower(stack.Item) : 0,
                    SkillName = hasItem ? ResolveSkillName(stack.Item) : string.Empty,
                    SkillDescription = hasItem ? ResolveSkillDescription(stack.Item) : string.Empty,
                    IsThrowWeapon = hasItem && stack.Item.IsThrowWeapon,
                    ThrowCooldownRemaining = hasItem ? stack!.ThrowCooldownRemaining : 0f,
                    BatteryCharge = hasItem ? ResolveBatteryCharge(stack.Item) : -1f,
                    BatteryMax = hasItem ? ResolveBatteryMax(stack.Item) : -1f
                });
            }

            return (quickBar.Slots.Count, occupiedSlots, selectedSlotIndex, selectedItemId, selectedItemName, slots);
        }

        /// <summary>武器攻击力（attack_power 属性值；解析失败返回 0）。</summary>
        private static int ResolveAttackPower(ItemDefinition item)
        {
            if (item.TryResolveAttribute(ItemAttributeIds.AttackPower, 1, out var attr))
            {
                return Mathf.RoundToInt(attr.Value);
            }

            return 0;
        }

        /// <summary>首个武器技能名（无技能返回空串）。</summary>
        private static string ResolveSkillName(ItemDefinition item)
        {
            foreach (var resource in item.WeaponSkillResources)
            {
                if (resource is WeaponSkillDefinition skill && !string.IsNullOrWhiteSpace(skill.DisplayName))
                {
                    return skill.DisplayName;
                }
            }

            return string.Empty;
        }

        /// <summary>首个武器技能描述（截断到 AI 可读长度；无技能返回空串）。</summary>
        private static string ResolveSkillDescription(ItemDefinition item)
        {
            foreach (var resource in item.WeaponSkillResources)
            {
                if (resource is WeaponSkillDefinition skill && !string.IsNullOrWhiteSpace(skill.Description))
                {
                    return TruncateForAi(skill.Description, 80);
                }
            }

            return string.Empty;
        }

        /// <summary>武器电池当前电量：遍历武器技能查 WeaponBatteryManager（按 SkillId），
        /// 未注册电池系统返回 -1（与"无电池"区分——注册后电量可为 0）。</summary>
        private static float ResolveBatteryCharge(ItemDefinition item)
        {
            foreach (var resource in item.WeaponSkillResources)
            {
                if (resource is WeaponSkillDefinition skill && !string.IsNullOrWhiteSpace(skill.SkillId))
                {
                    float charge = Kuros.Managers.WeaponBatteryManager.Instance.GetCharge(skill.SkillId);
                    if (charge >= 0f)
                    {
                        return charge;
                    }
                }
            }

            return -1f;
        }

        /// <summary>武器电池最大电量：无电池系统返回 -1。</summary>
        private static float ResolveBatteryMax(ItemDefinition item)
        {
            foreach (var resource in item.WeaponSkillResources)
            {
                if (resource is WeaponSkillDefinition skill && !string.IsNullOrWhiteSpace(skill.SkillId))
                {
                    float max = Kuros.Managers.WeaponBatteryManager.Instance.GetMaxCharge(skill.SkillId);
                    if (max >= 0f)
                    {
                        return max;
                    }
                }
            }

            return -1f;
        }

        /// <summary>截断文本到 AI 可读长度（超长加省略号），避免 prompt 膨胀。</summary>
        private static string TruncateForAi(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
        }

        private bool ResolvePlayerUnderAttack(SamplePlayer player)
        {
            if (TreatHitStateAsUnderAttack)
            {
                string stateName = player.StateMachine?.CurrentState?.Name ?? string.Empty;
                if (string.Equals(stateName, "Hit", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return player.GetSecondsSinceLastDamageTaken() <= UnderAttackWindowSeconds;
        }
    }
}
