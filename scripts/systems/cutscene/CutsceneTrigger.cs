using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 放在各房间场景中的 Area2D 触发器，玩家进入后自动播放指定过场动画序列。
    /// 在 Inspector 中设置 Sequence（.tres 资源）即可。
    /// </summary>
    [GlobalClass]
    public partial class CutsceneTrigger : Area2D
    {
        [Export] public CutsceneSequence? Sequence { get; set; }

        /// <summary>只触发一次。</summary>
        [Export] public bool TriggerOnce { get; set; } = true;

        [Export] public string PlayerGroup { get; set; } = "player";

        private bool _triggered = false;

        public override void _Ready()
        {
            AreaEntered += OnAreaEntered;
            GD.Print($"[Cutscene] CutsceneTrigger Ready — 节点: {Name}, Sequence: {(Sequence != null ? Sequence.SequenceId : "null")}");
        }

        private void OnAreaEntered(Area2D area)
        {
            GD.Print($"[Cutscene] AreaEntered — area: {area.Name}, parent: {area.GetParent()?.Name}");

            if (TriggerOnce && _triggered)
            {
                GD.Print("[Cutscene] 已触发过，跳过");
                return;
            }
            if (Sequence == null)
            {
                GD.PrintErr("[Cutscene] Sequence 未设置！");
                return;
            }

            // 检测玩家的 HitArea：自身在 player 组，或其父节点在 player 组
            var owner = area.IsInGroup(PlayerGroup) ? (Node)area : area.GetParent();
            if (owner == null || !owner.IsInGroup(PlayerGroup))
            {
                GD.Print($"[Cutscene] 非玩家 area，忽略（PlayerGroup={PlayerGroup}）");
                return;
            }

            GD.Print($"[Cutscene] 玩家进入触发区，准备播放: {Sequence.SequenceId}");

            var manager = GetTree().GetFirstNodeInGroup("cutscene_manager") as CutsceneManager;
            if (manager == null)
            {
                GD.PrintErr("[Cutscene] 未找到 CutsceneManager！确认 Stage_2 中已添加该节点。");
                return;
            }
            if (manager.IsPlaying)
            {
                GD.Print("[Cutscene] CutsceneManager 正在播放中，忽略");
                return;
            }

            _triggered = true;
            GD.Print($"[Cutscene] 开始播放过场: {Sequence.SequenceId}");
            _ = manager.PlayCutscene(Sequence);
        }
    }
}
