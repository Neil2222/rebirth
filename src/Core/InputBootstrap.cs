using Godot;

namespace Rebirth.Core;

/// <summary>Registers all input actions (defaults plus the player's own keys) before anything reads them.</summary>
public partial class InputBootstrap : Node
{
	public override void _EnterTree() => Keybinds.Setup();
}
