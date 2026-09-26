using System.Collections.Generic;
using Godot;

namespace Driftworks.World;

/// <param name="OreItemId">Inventory item produced when this material is mined.</param>
/// <param name="YieldPerCubicMetre">Kilograms of ore per fully solid cubic metre mined.</param>
public sealed record VoxelMaterial(string Name, Color Color, string OreItemId, float YieldPerCubicMetre);

public static class VoxelMaterials
{
	public const byte Stone = 0;
	public const byte Iron = 1;
	public const byte Nickel = 2;
	public const byte Silicon = 3;

	/// <summary>Indexed by the material byte stored per voxel.</summary>
	public static readonly IReadOnlyList<VoxelMaterial> All =
	[
		new("Stone", new Color(0.46f, 0.42f, 0.38f), "stone", 300f),
		new("Iron", new Color(0.55f, 0.28f, 0.18f), "iron_ore", 300f),
		new("Nickel", new Color(0.40f, 0.50f, 0.38f), "nickel_ore", 300f),
		new("Silicon", new Color(0.78f, 0.76f, 0.70f), "silicon_ore", 300f),
	];
}
