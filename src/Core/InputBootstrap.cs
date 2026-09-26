using Godot;

namespace Driftworks.Core;

/// <summary>Registers all input actions in code so bindings live in one readable place.</summary>
public partial class InputBootstrap : Node
{
	public override void _EnterTree()
	{
		Bind("move_forward", Key.W);
		Bind("move_back", Key.S);
		Bind("move_left", Key.A);
		Bind("move_right", Key.D);
		Bind("move_up", Key.Space);
		Bind("move_down", Key.C);
		Bind("roll_left", Key.Q);
		Bind("roll_right", Key.E);
		Bind("toggle_dampeners", Key.Z);
		Bind("toggle_jetpack", Key.X);
		Bind("release_mouse", Key.Escape);
	}

	private static void Bind(string action, Key key)
	{
		if (!InputMap.HasAction(action))
			InputMap.AddAction(action);
		InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
	}
}
