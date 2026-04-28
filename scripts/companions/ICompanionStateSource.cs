namespace Kuros.Companions
{
    /// <summary>
    /// Lightweight contract for non-GameActor companions that still need to be visible in GameState.
    /// </summary>
    public interface ICompanionStateSource
    {
        string CompanionName { get; }
        int CurrentHp { get; }
        int MaxHp { get; }
        bool IsCompanionAvailable { get; }
        string CompanionRole { get; }
    }
}