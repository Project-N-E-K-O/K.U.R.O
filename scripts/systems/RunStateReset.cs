using Godot;
using Kuros.Effects;
using Kuros.Managers;

namespace Kuros.Systems;

internal static class RunStateReset
{
    // Godot 已移除旧场景，新场景尚未就绪。此时清理常驻状态，避免新玩家恢复上一轮的数据。
    internal static void AfterSceneChange()
    {
        SaveManager.Instance?.ResetRunState();
        BuildSelectionManager.Instance?.ClearBuildState();
        WeaponBatteryManager.Instance?.ResetAll();
        FloatingDamageTextManager.Instance?.ClearAll();
        DialogueManager.Instance?.EndDialogue();
        DamageInterruptSlowMo.Cancel();
        Engine.TimeScale = 1.0;
        PauseManager.Instance?.ClearAllPauses();
    }
}
