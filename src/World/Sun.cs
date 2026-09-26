using Godot;

namespace Rebirth.World;

public static class Sun
{
	/// <summary>Unit vector pointing from the scene towards the sun. Set by whoever places the sun light.</summary>
	public static Vector3 Direction { get; set; } = Vector3.Up;
}
