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
	public const byte Regolith = 4;

	/// <summary>Indexed by the material byte stored per voxel.</summary>
	public static readonly IReadOnlyList<VoxelMaterial> All =
	[
		new("Stone", new Color(0.46f, 0.42f, 0.38f), "stone", 80f),
		new("Iron", new Color(0.55f, 0.28f, 0.18f), "iron_ore", 150f),
		new("Nickel", new Color(0.40f, 0.50f, 0.38f), "nickel_ore", 150f),
		new("Silicon", new Color(0.78f, 0.76f, 0.70f), "silicon_ore", 150f),
		new("Regolith", new Color(0.66f, 0.40f, 0.26f), "stone", 60f),
	];
}

/// <summary>Noise-driven ore pockets shared by asteroids and planets. Safe to use from worker threads.</summary>
public sealed class OreVeins
{
	private readonly FastNoiseLite _veins;
	private readonly FastNoiseLite _kind;

	public OreVeins(int seed)
	{
		_veins = new FastNoiseLite { Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.07f, FractalOctaves = 2 };
		_kind = new FastNoiseLite { Seed = seed + 1, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.02f };
	}

	/// <summary>Ore material at <paramref name="p"/>, or <paramref name="host"/> outside veins.</summary>
	public byte At(Vector3 p, byte host)
	{
		if (_veins.GetNoise3Dv(p) <= 0.45f)
			return host;
		float kind = _kind.GetNoise3Dv(p);
		return kind < -0.2f ? VoxelMaterials.Iron : kind < 0.25f ? VoxelMaterials.Nickel : VoxelMaterials.Silicon;
	}
}
