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

            entity.ApplyThrowImpulse(direction.Normalized() * impulse);
        }
    }
}

