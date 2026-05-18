using System;
using Godot;
using Kuros.Core;
using Kuros.Items.World;
using Kuros.Systems.Inventory;

namespace Kuros.Systems.Loot
{
    /// <summary>
    /// 负责根据掉落表在场景中生成对应的世界掉落物。
    /// </summary>
    public static class LootDropSystem
    {
        public static void SpawnLootForActor(GameActor actor, LootDropTable? table)
        {
            if (actor == null || table == null)
            {
                return;
            }

            var entries = table.Entries;
            if (entries == null || entries.Length == 0)
            {
                return;
            }

            var rng = new RandomNumberGenerator();
            rng.Randomize();

            if (!table.ShouldRoll(rng))
            {
                return;
            }

            // PickOne 模式：按权重从池中选一条，跳过 Sequential 逻辑
            if (table.SelectionMode == LootDropTable.LootSelectionMode.PickOne)
            {
                SpawnLootPickOne(actor, table, rng);
                return;
            }

            int spawned = 0;
            foreach (var entry in entries)
            {
                if (entry == null || !entry.IsValid || entry.Item == null)
                {
                    continue;
                }

                if (!entry.ShouldDrop(rng))
                {
                    continue;
                }

                int stackCopies = entry.RollStackCount(rng);
                for (int i = 0; i < stackCopies; i++)
                {
                    int quantity = entry.RollQuantity(rng);
                    if (quantity <= 0)
                    {
                        continue;
                    }

                    var stack = new InventoryItemStack(entry.Item, quantity);
                    Vector2 spawnPos = actor.GlobalPosition + table.SpawnOffset + entry.PositionOffset + GetScatterOffset(table, rng);
                    var entity = WorldItemSpawner.SpawnFromStack(actor, stack, spawnPos);
                    if (entity != null)
                    {
                        ApplyImpulse(entity, entry, table, rng);
                    }

                    spawned++;
                    if (table.MaxDrops > 0 && spawned >= table.MaxDrops)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// PickOne 模式：按 DropChance 权重加权随机选一条条目生成掉落物。
        /// 所有 DropChance 之和为总权重，各条目被选中的概率 = 自身 DropChance / 总权重。
        /// 若总权重为 0 则不掉落。
        /// </summary>
        private static void SpawnLootPickOne(GameActor actor, LootDropTable table, RandomNumberGenerator rng)
        {
            var entries = table.Entries;

            // 计算所有有效条目的总权重
            float totalWeight = 0f;
            foreach (var entry in entries)
            {
                if (entry != null && entry.IsValid)
                    totalWeight += entry.DropChance;
            }

            if (totalWeight <= 0f)
                return;

            // 在 [0, totalWeight] 内随机取一点，落在哪个区间就掉哪个
            float roll = rng.RandfRange(0f, totalWeight);
            float cumulative = 0f;

            foreach (var entry in entries)
            {
                if (entry == null || !entry.IsValid || entry.Item == null)
                    continue;

                cumulative += entry.DropChance;
                if (roll <= cumulative)
                {
                    int quantity = entry.RollQuantity(rng);
                    if (quantity <= 0)
                        return;

                    var stack = new InventoryItemStack(entry.Item, quantity);
                    Vector2 spawnPos = actor.GlobalPosition + table.SpawnOffset + entry.PositionOffset + GetScatterOffset(table, rng);
                    var entity = WorldItemSpawner.SpawnFromStack(actor, stack, spawnPos);
                    if (entity != null)
                        ApplyImpulse(entity, entry, table, rng);
                    return;
                }
            }
        }

        private static Vector2 GetScatterOffset(LootDropTable table, RandomNumberGenerator rng)
        {
            if (table.ScatterRadius <= 0f)
            {
                return Vector2.Zero;
            }

            float radius = rng.RandfRange(0f, table.ScatterRadius);
            float angle = rng.RandfRange(0f, Mathf.Tau);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private static void ApplyImpulse(Kuros.Items.World.IWorldItemEntity entity, LootDropEntry entry, LootDropTable table, RandomNumberGenerator rng)
        {
            float impulse = entry.ImpulseStrength > 0f ? entry.ImpulseStrength : table.DefaultImpulse;
            if (impulse <= 0f)
            {
                return;
            }

            float spreadDegrees = Mathf.Clamp(entry.ImpulseSpreadDegrees, 0f, 360f);
            float angleRad = spreadDegrees >= 360f
                ? rng.RandfRange(0f, Mathf.Tau)
                : rng.RandfRange(-Mathf.DegToRad(spreadDegrees) * 0.5f, Mathf.DegToRad(spreadDegrees) * 0.5f);

            Vector2 direction = new(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
            if (direction == Vector2.Zero)
            {
                direction = Vector2.Right;
            }

            entity.ApplyScatterImpulse(direction.Normalized() * impulse);
        }
    }
}

