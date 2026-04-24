namespace Kuros.Core.Effects
{
    public interface ICooldownEffect
    {
        float CooldownDuration { get; }
        float CooldownRemaining { get; }
        bool IsOnCooldown { get; }
    }
}
