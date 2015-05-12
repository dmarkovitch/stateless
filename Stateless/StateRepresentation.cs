using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Stateless
{
	public partial class StateMachine<TState, TTrigger>
	{
		internal class StateRepresentation
		{
			readonly TState _state;

			readonly IDictionary<TTrigger, ICollection<TriggerBehaviour>> _triggerBehaviours =
				new Dictionary<TTrigger, ICollection<TriggerBehaviour>>();

			readonly ICollection<Action<Transition, object[]>> _entryActions = new List<Action<Transition, object[]>>();
			readonly ICollection<Action<Transition, object[]>> _exitActions = new List<Action<Transition, object[]>>();

			StateRepresentation _superstate; // null

			readonly ICollection<StateRepresentation> _substates = new List<StateRepresentation>();
			private bool _isTerminal;

			public StateRepresentation(TState state)
			{
				_state = state;
			}

			/// <summary>
			/// Terminal states will ignore ALL triggers. If current state is a sub-state of a terminal state, it will also
			/// ignore ALL triggers.
			/// </summary>
			public bool IsTerminal
			{
				get
				{
					return _superstate != null ? _superstate.IsTerminal : _isTerminal;
				}
				internal set { _isTerminal = value; }
			}

			public bool CanHandle(TTrigger trigger)
			{
				TriggerBehaviour unused;
				return TryFindHandler(trigger, out unused);
			}

			public bool TryFindHandler(TTrigger trigger, out TriggerBehaviour handler)
			{
				return (TryFindLocalHandler(trigger, out handler) ||
					(Superstate != null && Superstate.TryFindHandler(trigger, out handler)));
			}

			bool TryFindLocalHandler(TTrigger trigger, out TriggerBehaviour handler)
			{
				ICollection<TriggerBehaviour> possible;
				if (!_triggerBehaviours.TryGetValue(trigger, out possible))
				{
					handler = null;
					return false;
				}

				var actual = possible.Where(at => at.IsGuardConditionMet).ToArray();

				if (actual.Count() > 1)
					throw new InvalidOperationException(
						string.Format(StateRepresentationResources.MultipleTransitionsPermitted,
						trigger, _state));

				handler = actual.FirstOrDefault();
				return handler != null;
			}

			public void AddEntryAction(TTrigger trigger, Action<Transition, object[]> action)
			{
				Enforce.ArgumentNotNull(action, "action");
				_entryActions.Add((t, args) =>
				{
					if (t.Trigger.Equals(trigger))
						action(t, args);
				});
			}

			public void AddExitAction(TTrigger trigger, Action<Transition, object[]> action)
			{
				Enforce.ArgumentNotNull(action, "action");
				_exitActions.Add((t, args) =>
				{
					if (t.Trigger.Equals(trigger))
						action(t, args);
				});
			}

			public void AddEntryAction(Action<Transition, object[]> action)
			{
				_entryActions.Add(Enforce.ArgumentNotNull(action, "action"));
			}

			public void AddExitAction(Action<Transition, object[]> action)
			{
				_exitActions.Add(Enforce.ArgumentNotNull(action, "action"));
			}

			public void Enter(Transition transition, params object[] entryArgs)
			{
				Enforce.ArgumentNotNull(transition, "transtion");

				if (transition.IsReentry)
				{
					ExecuteEntryActions(transition, entryArgs);
				}
				else if (!Includes(transition.Source))
				{
					if (_superstate != null)
						_superstate.Enter(transition, entryArgs);

					ExecuteEntryActions(transition, entryArgs);
				}
			}

			public void Exit(Transition transition, params object[] exitArgs)
			{
				Enforce.ArgumentNotNull(transition, "transtion");

				if (transition.IsReentry)
				{
					ExecuteExitActions(transition, exitArgs);
				}
				else if (!Includes(transition.Destination))
				{
					ExecuteExitActions(transition, exitArgs);
					if (_superstate != null)
						_superstate.Exit(transition);
				}
			}

			void ExecuteEntryActions(Transition transition, object[] entryArgs)
			{
				Enforce.ArgumentNotNull(transition, "transtion");
				Enforce.ArgumentNotNull(entryArgs, "entryArgs");
				foreach (var action in _entryActions)
					action(transition, entryArgs);
			}

			void ExecuteExitActions(Transition transition, object[] exitArgs)
			{
				Enforce.ArgumentNotNull(transition, "transtion");
				Enforce.ArgumentNotNull(exitArgs, "exitArgs");
				foreach (var action in _exitActions)
					action(transition, exitArgs);
			}

			public void AddTriggerBehaviour(TriggerBehaviour triggerBehaviour)
			{
				ICollection<TriggerBehaviour> allowed;
				if (!_triggerBehaviours.TryGetValue(triggerBehaviour.Trigger, out allowed))
				{
					allowed = new List<TriggerBehaviour>();
					_triggerBehaviours.Add(triggerBehaviour.Trigger, allowed);
				}
				allowed.Add(triggerBehaviour);
			}

			public StateRepresentation Superstate
			{
				get
				{
					return _superstate;
				}
				set
				{
					_superstate = value;
				}
			}

			public TState UnderlyingState
			{
				get
				{
					return _state;
				}
			}

			public void AddSubstate(StateRepresentation substate)
			{
				Enforce.ArgumentNotNull(substate, "substate");
				_substates.Add(substate);
			}

			public bool Includes(TState state)
			{
				return _state.Equals(state) || _substates.Any(s => s.Includes(state));
			}

			public bool IsIncludedIn(TState state)
			{
				return
					_state.Equals(state) ||
					(_superstate != null && _superstate.IsIncludedIn(state));
			}

			public IEnumerable<TTrigger> PermittedTriggers
			{
				get
				{
					var result = _triggerBehaviours
						.Where(t => t.Value.Any(a => a.IsGuardConditionMet))
						.Select(t => t.Key);

					if (Superstate != null)
						result = result.Union(Superstate.PermittedTriggers);

					return result.ToArray();
				}
			}
		}
	}
}
