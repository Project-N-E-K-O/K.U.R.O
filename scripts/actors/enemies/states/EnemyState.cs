using Godot;
using Kuros.Systems.FSM;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyState : State
    {
        protected SampleEnemy Enemy => (SampleEnemy)Actor;
        protected SamplePlayer? Player => Enemy.PlayerTarget;
        
        protected bool HasPlayer => Player != null;
    }
}

