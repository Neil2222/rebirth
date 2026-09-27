namespace Rebirth.Core;

public static class GameState
{
	/// <summary>
	/// True while a full-screen tool (the Forge) has the keyboard and mouse. The world keeps
	/// simulating, but the player character ignores input.
	/// </summary>
	public static bool WorldInputBlocked { get; set; }

	/// <summary>How the next fresh world starts (set by the title and pause menus before reloading).</summary>
	public static StartMode NextStart { get; set; } = StartMode.Tutorial;

	/// <summary>Which Box a fresh world is built in (1 = the first cluster; 2 after the first breach).</summary>
	public static int NextBox { get; set; } = 1;
}

/// <summary>A new game either teaches you to build your first drill and bots, or starts with them done.</summary>
public enum StartMode { Tutorial, SkipIntro }
