using System;
using System.Collections.Generic;
using Godot;
using Kuros.Systems.Inventory;
using Kuros.Utils;

namespace Kuros.Items.World
{
    /// <summary>
    /// 根据物品定义在场景中生成对应的 <see cref="WorldItemEntity"/>。
    /// </summary>
    public static class WorldItemSpawner
    {
        private const string DefaultSceneDirectory = "res://scenes/properties/";
        private static readonly Dictionary<string, PackedScene> CachedScenes = new(StringComparer.OrdinalIgnoreCase);

        public static WorldItemEntity? SpawnFromStack(Node context, InventoryItemStack stack, Vector2 globalPosition)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (stack == null) throw new ArgumentNullException(nameof(stack));

            var scene = ResolveScene(stack.Item);
            if (scene == null)
            {
                GameLogger.Error(nameof(WorldItemSpawner), $"未找到 {stack.Item.ItemId} 对应的世界场景。");
                return null;
            }

            var worldNode = context.GetTree().CurrentScene ?? context;
            var entity = scene.Instantiate<WorldItemEntity>();
            worldNode.AddChild(entity);
            entity.GlobalPosition = globalPosition;
            entity.InitializeFromStack(stack);
            return entity;
        }

        public static PackedScene? ResolveScene(ItemDefinition definition)
        {
            if (definition == null) return null;
            var path = definition.ResolveWorldScenePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (CachedScenes.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var scene = ResourceLoader.Load<PackedScene>(path);
            if (scene != null)
            {
                CachedScenes[path] = scene;
            }

            return scene;
        }
    }
}
