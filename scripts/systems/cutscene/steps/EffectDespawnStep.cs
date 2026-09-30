using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 销毁**之前由过场生成**的场景/特效（生成端 = EffectSpawnStep / EffectGroupSpawnStep 的 SpawnTag 登记）。
    /// 补上"生成端只有句柄、没有回收"的那一半：同一条滑槽可以生成在过场开头、清理在过场结尾，
    /// 中间隔着任意多步骤。
    ///
    /// 用法：
    ///   - Tags：要销毁的标签集合；**留空 = 销毁所有登记过的生成物**（整段关卡换幕时最省事）。
    ///     列表里留空/空白的条目会被忽略（要清全部就留空整个数组，不要填空字符串）。
    ///   - 销毁 = 对**根节点** QueueFree：子树里的东西（滑槽 Mount 下自动入驻的 CarriagePrefab、
    ///     挂载在其下的机械/敌人）随父节点一起释放，不需要各自登记。
    ///   - 跨序列可用：登记表在管理器上、不随序列结束清空，所以后一段过场可以清掉前一段过场生成的东西。
    ///   - 销毁后即从登记表移除，同一步骤重复执行不会处理第二次。
    ///
    /// 边界：被生成物挂到**别处**的子孙（如敌人召唤攻击生成到世界节点下的怪）不在子树里，清不掉——
    /// 那类对象要自己加组，或由各自的存活逻辑收尾。
    ///
    /// 跳过语义：用默认的 ExecuteOnSkip（跳过 = 快进到终态）——**跳过时同样执行清理**，
    /// 否则玩家按一次跳过就会漏掉一次回收（生成端 GenerateOnSkip 默认 true，跳过时东西是存在的）。
    /// </summary>
    [GlobalClass]
    public partial class EffectDespawnStep : CutsceneStep
    {
        [ExportCategory("Despawn")]
        /// <summary>要销毁的标签（对应生成端 SpawnTag）。**空数组 = 销毁全部登记项**（含无标签的）。</summary>
        [Export] public Godot.Collections.Array<string> Tags { get; set; } = new();

        public override Task Execute(CutsceneContext ctx)
        {
            bool all = Tags == null || Tags.Count == 0;

            var roots = new System.Collections.Generic.List<Node>();
            if (all)
            {
                roots = ctx.Manager.CollectSpawnedRoots(null);
            }
            else
            {
                foreach (var tag in Tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;

                    foreach (var root in ctx.Manager.CollectSpawnedRoots(tag))
                        if (!roots.Contains(root)) roots.Add(root);
                }
            }

            if (roots.Count == 0)
            {
                // 指定了标签却一个都没匹配 = 多半是标签写错；"清全部但没有登记项"是正常无操作
                if (!all)
                    GD.PushWarning($"[Cutscene] EffectDespawnStep: 标签 {string.Join(", ", Tags!)} 没有匹配的生成物");
                return Task.CompletedTask;
            }

            foreach (var root in roots)
            {
                ctx.Manager.UnregisterSpawnedRoot(root);
                GD.Print($"[Cutscene] EffectDespawnStep: 销毁生成物 {root.Name}（{root.GetType().Name}）");
                root.QueueFree();
            }

            GD.Print($"[Cutscene] EffectDespawnStep 完成，共销毁 {roots.Count} 个生成物");
            return Task.CompletedTask;
        }
    }
}
