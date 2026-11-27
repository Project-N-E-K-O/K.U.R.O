using Godot;

namespace Kuros.Fx
{
    /// <summary>
    /// 粒子特效预热器（Autoload）。
    /// 战斗场景就绪后，对常用粒子场景各静默渲染两帧（相机位置、完全透明、无任何视觉），
    /// 把一次性成本（shader 编译、湍流噪音纹理生成、粒子缓冲初始化）从战斗中首次触发
    /// 挪到开局——之后本会话内触发同款特效不再出现首次卡顿。
    /// 预热对象通过本节点的 ScenesToWarm 数组配置（以场景方式 Autoload，编辑器里可直接改）。
    /// </summary>
    public partial class ParticleEffectWarmer : Node
    {
        [Export] public Godot.Collections.Array<PackedScene> ScenesToWarm { get; set; } = new();
        [Export] public bool WarmOnReady { get; set; } = true;

        private bool _warmed;

        public override void _Ready()
        {
            if (WarmOnReady)
                CallDeferred(nameof(WarmUpAll));   // 延迟到战斗场景就绪（相机存在）后再预热
        }

        /// <summary>对配置列表中的所有粒子场景执行一次静默预热（重复调用安全）。</summary>
        public void WarmUpAll()
        {
            if (_warmed) return;
            _warmed = true;
            foreach (var scene in ScenesToWarm)
            {
                if (scene != null)
                    WarmUp(scene);
            }
        }

        /// <summary>静默预渲染单个粒子场景（透明 2 帧后释放），供加载界面等主动调用。</summary>
        public async void WarmUp(PackedScene scene)
        {
            var instance = scene.Instantiate<Node>();
            DisableCollisions(instance);
            AddChild(instance);

            // 放到当前相机位置（视野内才会被提交渲染，才会真正触发编译）；
            // 调制全透明——粒子照常模拟/绘制，但完全不可见
            var camPos = GetViewport().GetCamera2D()?.GlobalPosition ?? Vector2.Zero;
            if (instance is Node2D n2d)
            {
                n2d.GlobalPosition = camPos;
                n2d.Modulate = new Color(1f, 1f, 1f, 0f);
            }

            // 强制所有粒子系统发射（部分场景默认 emitting=false，不发射则不提交渲染）
            foreach (var node in instance.FindChildren("*", "", recursive: true, owned: false))
            {
                if (node is GpuParticles2D gp)
                {
                    gp.Emitting = true;
                    gp.Restart();
                }
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            instance.QueueFree();
        }

        private static void DisableCollisions(Node node)
        {
            // 同时禁用形状，避免场景的 _Ready 重设碰撞层后重新参与物理检测。
            switch (node)
            {
                case CollisionObject2D collisionObject:
                    collisionObject.CollisionLayer = 0;
                    collisionObject.CollisionMask = 0;
                    break;
                case CollisionShape2D shape:
                    shape.Disabled = true;
                    break;
                case CollisionPolygon2D polygon:
                    polygon.Disabled = true;
                    break;
            }

            foreach (Node child in node.GetChildren(includeInternal: true))
                DisableCollisions(child);
        }
    }
}
