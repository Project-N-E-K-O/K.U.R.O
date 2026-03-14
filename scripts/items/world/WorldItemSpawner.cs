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
        private const string DefaultSceneDirectory = "res://scenes/items/";
        private static readonly Dictionary<string, PackedScene> CachedScenes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 清除场景缓存（用于开发调试）
        /// </summary>
        public static void ClearCache()
        {
            CachedScenes.Clear();
        }

        public static IWorldItemEntity? SpawnFromStack(Node context, InventoryItemStack stack, Vector2 globalPosition)
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

            GameLogger.Info(nameof(WorldItemSpawner), $"Instantiating scene: {scene.ResourcePath}");
            var rootNode = scene.Instantiate();
            if (rootNode == null)
            {
                GameLogger.Error(nameof(WorldItemSpawner), $"无法实例化场景 {scene.ResourcePath}。");
                return null;
            }

            // 支持两种世界物品实体：WorldItemEntity (CharacterBody2D) 或 RigidBodyWorldItemEntity (Node2D wrapper)
            if (rootNode is WorldItemEntity entity)
            {
                worldNode.AddChild(entity);
                entity.GlobalPosition = globalPosition;
                entity.InitializeFromStack(stack);
                return entity;
            }

            if (rootNode is RigidBodyWorldItemEntity rigidEntity)
            {
                worldNode.AddChild(rigidEntity);
                rigidEntity.GlobalPosition = globalPosition;
                rigidEntity.InitializeFromStack(stack);
                return rigidEntity;
            }

            // 如果根节点类型不符合预期，记录详细错误并清理实例
            GameLogger.Error(nameof(WorldItemSpawner),
                $"场景 {scene.ResourcePath} 的根节点类型为 {rootNode.GetType().Name}，必须是 WorldItemEntity 或 RigidBodyWorldItemEntity。考虑检查场景的根节点和附加的脚本资源。");
            rootNode.QueueFree();
            return null;
        }

        public static PackedScene? ResolveScene(ItemDefinition definition)
        {
            if (definition == null) return null;

            // Try the explicit path first
            var rawPath = definition.ResolveWorldScenePath();

            // If the resolved path is not a file path (e.g. ExtResource(...) or uid://),
            // fall back to the default convention using ItemId.
            string[] tryPaths;
            if (!string.IsNullOrWhiteSpace(rawPath) && rawPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                tryPaths = new[] { rawPath, $"{DefaultSceneDirectory}{definition.ItemId}.tscn" };
            }
            else
            {
                tryPaths = new[] { $"{DefaultSceneDirectory}{definition.ItemId}.tscn" };
            }

            foreach (var path in tryPaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                if (CachedScenes.TryGetValue(path, out var cached))
                {
                    return cached;
                }

                var scene = ResourceLoader.Load<PackedScene>(path);
                if (scene != null)
                {
                    CachedScenes[path] = scene;
                    return scene;
                }
            }

            return null;
        }
    }
}
