using Godot;

namespace Rebirth.World;

/// <summary>
/// A small, walkable planet you can circle on foot in about a minute: a ball with rolling hills,
/// a soft crust over stone, ore pockets, its own gravity, and a thin haze.
/// </summary>
public partial class MiniPlanet : VoxelBody
{
	/// <summary>Hill height as a fraction of the radius.</summary>
	private const float Hilliness = 0.07f;
	/// <summary>Depth (m) of the crust before stone begins.</summary>
	private const float CrustDepth = 2.5f;

	/// <summary>Surface material: the planet's look while it sleeps.</summary>
	public byte Crust { get; set; } = VoxelMaterials.Regolith;
	public Color HazeTint { get; set; } = new(1f, 0.78f, 0.6f);
	public float SurfaceGravity { get; set; } = 9.81f;

	private FastNoiseLite _hills = null!;
	private OreVeins _ores = null!;

	protected override float MaxSurfaceRadius => Radius * (1f + Hilliness);

	public override void _Ready()
	{
		base._Ready();
		AddChild(BuildGravity());
		AddChild(BuildHaze());
	}

	protected override void PrepareShape()
	{
		// Broad, gentle swells so walking feels like strolling over dunes, not climbing rocks.
		_hills = new FastNoiseLite { Seed = Seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1.6f / Radius, FractalOctaves = 2 };
		_ores = new OreVeins(Seed + 2);
	}

	protected override float ShapeDensity(Vector3 p)
	{
		float r = p.Length();
		if (r < 1e-3f)
			return Radius;
		return Radius * (1f + Hilliness * _hills.GetNoise3Dv(p / r * Radius)) - r;
	}

	protected override byte ShapeMaterial(Vector3 p, float density) =>
		density < CrustDepth ? Crust : _ores.At(p, VoxelMaterials.Stone);

	/// <summary>Full gravity at the surface, falling off with the square of the distance, out to three radii.</summary>
	private Area3D BuildGravity()
	{
		var area = new Area3D
		{
			Name = "Gravity",
			GravitySpaceOverride = Area3D.SpaceOverride.Combine,
			GravityPoint = true,
			GravityPointCenter = Vector3.Zero,
			GravityPointUnitDistance = Radius,
			Gravity = SurfaceGravity,
		};
		area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius * 3f } });
		return area;
	}

	private MeshInstance3D BuildHaze()
	{
		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/atmosphere.gdshader") };
		material.SetShaderParameter("tint", HazeTint);
		float r = Radius * 1.18f;
		material.SetShaderParameter("haze_radius", r);
		return new MeshInstance3D
		{
			Name = "Haze",
			Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 64, Rings = 32, Material = material },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}
}
