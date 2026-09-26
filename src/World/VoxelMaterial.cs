using System.Collections.Generic;
using Godot;

namespace Rebirth.World;

/// <param name="OreItemId">Inventory item produced when this material is mined.</param>
/// <param name="YieldPerCubicMetre">Kilograms of ore per fully solid cubic metre mined.</param>
/// <param name="Glow">Faint self-illumination (0..1) so ore patches stay visible in shadow.</param>
public sealed record VoxelMaterial(string Name, Color Color, string OreItemId, float YieldPerCubicMetre, float Glow);

public static class VoxelMaterials
{
	public const byte Stone = 0;
	public const byte Iron = 1;
	public const byte Nickel = 2;
	public const byte Silicon = 3;
	public const byte Regolith = 4;
	public const byte Dune = 5;
	public const byte Frost = 6;
	public const byte Moss = 7;

	/// <summary>Indexed by the material byte stored per voxel.</summary>
	public static readonly IReadOnlyList<VoxelMaterial> All =
	[
		new("Stone", new Color(0.66f, 0.63f, 0.66f), "stone", 80f, 0f),
		new("Iron", new Color(0.88f, 0.46f, 0.30f), "iron_ore", 150f, 0.12f),
		new("Nickel", new Color(0.50f, 0.80f, 0.64f), "nickel_ore", 150f, 0.12f),
		new("Silicon", new Color(0.86f, 0.84f, 0.96f), "silicon_ore", 150f, 0.12f),
		new("Regolith", new Color(0.80f, 0.69f, 0.62f), "stone", 60f, 0f),
		// Sleepy planet crusts, each still waiting to come alive.
		new("Dune", new Color(0.88f, 0.74f, 0.60f), "stone", 60f, 0f),
		new("Frost", new Color(0.80f, 0.87f, 0.94f), "stone", 60f, 0f),
		new("Moss", new Color(0.64f, 0.67f, 0.52f), "stone", 60f, 0f),
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
