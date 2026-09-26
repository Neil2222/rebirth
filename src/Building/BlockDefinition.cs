using Godot;

namespace Driftworks.Building;

public enum BlockKind { Armor, Cockpit, Thruster, Gyroscope }

/// <param name="MaxIntegrity">Damage the block absorbs before it is destroyed.</param>
/// <param name="Thrust">Newtons, pushing the grid along the block's local forward (-Z); exhaust leaves through +Z.</param>
/// <param name="Torque">Newton-metres of rotational authority contributed to the grid.</param>
public sealed record BlockDefinition(
	string Id,
	string DisplayName,
	BlockKind Kind,
	Color Color,
	float Mass,
	float MaxIntegrity,
	float Thrust = 0f,
	float Torque = 0f);

/// <summary>A block as placed in a grid. Orientation is one of the 24 axis-aligned rotations, in grid-local space.</summary>
public readonly record struct PlacedBlock(BlockDefinition Definition, Basis Orientation);

public static class BlockCatalog
{
	// Masses and outputs are in the ballpark of Space Engineers' large-grid blocks.
	public static readonly BlockDefinition LightArmor = new("light_armor", "Light Armor Block", BlockKind.Armor, new Color(0.62f, 0.64f, 0.66f), 418f, 100f);
	public static readonly BlockDefinition HeavyArmor = new("heavy_armor", "Heavy Armor Block", BlockKind.Armor, new Color(0.30f, 0.32f, 0.35f), 3300f, 600f);
	public static readonly BlockDefinition Cockpit = new("cockpit", "Cockpit", BlockKind.Cockpit, new Color(0.25f, 0.33f, 0.45f), 1200f, 80f);
	public static readonly BlockDefinition Thruster = new("ion_thruster", "Ion Thruster", BlockKind.Thruster, new Color(0.45f, 0.47f, 0.50f), 700f, 80f, Thrust: 250_000f);
	public static readonly BlockDefinition Gyroscope = new("gyroscope", "Gyroscope", BlockKind.Gyroscope, new Color(0.75f, 0.62f, 0.25f), 1400f, 80f, Torque: 30_000_000f);
}
