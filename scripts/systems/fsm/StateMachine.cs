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

            if (!CanTransitionTo(stateName))
            {
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

        public void ReenterState(string stateName)
        {
            if (!_states.ContainsKey(stateName))
            {
                GameLogger.Error(nameof(StateMachine), $"State '{stateName}' not found!");
                return;
            }

            if (!CanTransitionTo(stateName))
            {
                return;
            }

            State newState = _states[stateName];

            CurrentState?.Exit();
            CurrentState = newState;
            CurrentState.Enter();
        }

        private bool CanTransitionTo(string stateName)
        {
            if (_actor is GameActor actor)
            {
                bool isDeathState = stateName == "Dying" || stateName == "Dead";
                if ((actor.IsDeathSequenceActive || actor.IsDead) && !isDeathState)
                {
                    return false;
                }
            }

            //这是一个可选的转换检查。当前状态可以通过重写 CanExitTo 来阻止转换，目标状态也可以通过重写 CanEnterFrom 来拒绝进入。默认情况下它们都允许转换。
            if (CurrentState != null && !CurrentState.CanExitTo(stateName))
            {
                return false;
            }

            if (_states.TryGetValue(stateName, out State? nextState))
            {
                string? currentStateName = CurrentState?.Name;
                if (!nextState.CanEnterFrom(currentStateName))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

