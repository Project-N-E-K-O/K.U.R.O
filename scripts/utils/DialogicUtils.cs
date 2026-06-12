using Godot;

namespace Kuros.Utils
{
    /// <summary>
    /// Dialogic 生命周期管理工具。
    /// 在 ChangeSceneToFile/Packed 之前调用 CleanupPersistentState() 以清除跨场景泄漏的 Node 引用。
    /// </summary>
    public static class DialogicUtils
    {
        /// <summary>
        /// 清理 Dialogic 跨场景持久化状态，防止旧场景的 Node 引用泄漏到新创建的 Layout 中。
        /// </summary>
        public static void CleanupPersistentState(Node context)
        {
            var bridge = context.GetNodeOrNull<Node>("/root/DialogicBridge");
            bridge?.Call("clear_dialogic_persistent_info");

            var dialogic = context.GetNodeOrNull("/root/Dialogic");
            if (dialogic != null && GodotObject.IsInstanceValid(dialogic) && dialogic.HasMethod("clear"))
                dialogic.Call("clear", 0); // FULL_CLEAR
        }
    }
}
