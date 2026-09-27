using Godot;

namespace Rebirth.World;

public static class Sun
{
	/// <summary>Unit vector pointing from the scene towards the sun. Set by whoever places the sun light.</summary>
	public static Vector3 Direction { get; set; } = Vector3.Up;

	/// <summary>How strong sunlight is in this Box for solar panels (a dim Box's star gives little).</summary>
	public static float Strength { get; set; } = 1f;
}
