namespace Kuros.Systems.FSM;

/// <summary>受准入守卫约束的高优先级转换，仍执行原状态 Exit 和目标 CanEnterFrom。</summary>
public interface IPriorityInterruptState
{
    bool CanInterrupt(string? previousState);
}
