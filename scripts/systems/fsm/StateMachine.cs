using Godot;
using System;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Utils;

namespace Kuros.Systems.FSM
{
    public partial class StateMachine : Node
    {
        [Export] public State InitialState { get; set; } = null!;
        
        public State CurrentState { get; private set; } = null!;
        
        private Dictionary<string, State> _states = new Dictionary<string, State>();
        private GameActor _actor = null!;
        
        public override void _Ready()
        {
            // Wait for owner to be ready implies we initialize manually or in Ready if actor is parent
             _actor = GetParentOrNull<GameActor>();
        }

        public void Initialize(GameActor actor)
        {
            _actor = actor;
            
            foreach (Node child in GetChildren())
            {
                if (child is State state)
                {
                    _states[child.Name] = state;
                    state.Initialize(_actor, this);
                }
            }

            if (InitialState != null)
            {
                ChangeState(InitialState.Name);
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            CurrentState?.HandleInput(@event);
        }

        public override void _Process(double delta)
        {
            CurrentState?.Update(delta);
        }

        public override void _PhysicsProcess(double delta)
        {
            CurrentState?.PhysicsUpdate(delta);
        }

        public bool HasState(string stateName)
        {
            return _states.ContainsKey(stateName);
        }

        public void ChangeState(string stateName)
        {
            if (!_states.ContainsKey(stateName))
            {
                GameLogger.Error(nameof(StateMachine), $"State '{stateName}' not found!");
                return;
            }

            State newState = _states[stateName];
            
            // Don't re-enter the same state unless we explicitly want to (omitted for now)
            if (CurrentState == newState) return;

            CurrentState?.Exit();
            
            CurrentState = newState;
            // GD.Print($"Entered State: {stateName}"); // Debug log
            
            CurrentState.Enter();
        }
    }
}

