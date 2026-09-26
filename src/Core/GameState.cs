namespace Rebirth.Core;

public static class GameState
{
	/// <summary>
	/// True while a full-screen tool (the Forge) has the keyboard and mouse. The world keeps
	/// simulating, but the player character ignores input.
	/// </summary>
	public static bool WorldInputBlocked { get; set; }
}
