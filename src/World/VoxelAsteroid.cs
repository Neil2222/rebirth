using Godot;

namespace Rebirth.World;

/// <summary>A floating pebble of rock: a sphere pushed in and out by a couple of broad noise octaves.</summary>
public partial class VoxelAsteroid : VoxelBody
{
	/// <summary>How far the surface bulges in or out, as a fraction of the radius.</summary>
	private const float Lumpiness = 0.22f;

	private FastNoiseLite _shape = null!;
	private OreVeins _ores = null!;

	protected override float MaxSurfaceRadius => Radius * (1f + Lumpiness);

	protected override void PrepareShape()
	{
		// Few, broad octaves: soft pebble-like lumps rather than a crumbly rock.
		_shape = new FastNoiseLite { Seed = Seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.9f / Radius, FractalOctaves = 2 };
		_ores = new OreVeins(Seed + 2);
	}

	protected override float ShapeDensity(Vector3 p) => Radius * (1f + Lumpiness * _shape.GetNoise3Dv(p)) - p.Length();

	protected override byte ShapeMaterial(Vector3 p, float density) => _ores.At(p, VoxelMaterials.Stone);
}
