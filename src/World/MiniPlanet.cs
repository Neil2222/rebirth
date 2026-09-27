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

	/// <summary>Haze once the air is back: clear sky blue.</summary>
	public static readonly Color LivingHaze = new(0.55f, 0.8f, 1f);
	/// <summary>Kilograms of stone or ice per 1% of a vital, per square metre of radius (bigger planets take longer).</summary>
	private const float KilogramsPerVitalPerR2 = 0.2f;

	/// <summary>How far each vital has come back, 0..1.</summary>
	public float Air { get; set; }
	public float Water { get; set; }
	public float Soil { get; set; }
	public float Vitality => (Air + Water + Soil) / 3f;

	/// <summary>Resonance the planet's life gives off per minute: plants and seas together.</summary>
	public float ResonancePerMinute => 30f * Soil * (0.5f + 0.5f * Water);

	private FastNoiseLite _hills = null!;
	private OreVeins _ores = null!;
	private ShaderMaterial _hazeMaterial = null!;
	private MeshInstance3D _sea = null!;
	// What is drawn, easing towards the real values so changes roll in gently.
	private float _shownAir = -1f, _shownWater = -1f, _shownSoil = -1f;

	protected override float MaxSurfaceRadius => Radius * (1f + Hilliness);

	public override void _Ready()
	{
		base._Ready();
		AddChild(BuildGravity());
		AddChild(BuildHaze());
		AddChild(BuildSea());
	}

	/// <summary>Adds <paramref name="kilograms"/> of processed stone or ice to a vital.</summary>
	public void Nourish(Rebirth.Building.Vital vital, float kilograms)
	{
		float amount = kilograms / (Radius * Radius * KilogramsPerVitalPerR2 * 100f);
		switch (vital)
		{
			case Rebirth.Building.Vital.Air: Air = Mathf.Min(1f, Air + amount); break;
			case Rebirth.Building.Vital.Water: Water = Mathf.Min(1f, Water + amount); break;
			default: Soil = Mathf.Min(1f, Soil + amount); break;
		}
	}

	/// <summary>Kilograms of stone (air, soil) or ice (water) to bring a vital from 0 to 100%.</summary>
	public float KilogramsForFullVital => Radius * Radius * KilogramsPerVitalPerR2 * 100f;

	public override void _Process(double delta)
	{
		float ease = _shownAir < 0f ? 1f : Mathf.Min(1f, (float)delta * 0.8f);
		_shownAir = Mathf.Lerp(Mathf.Max(_shownAir, 0f), Air, ease);
		_shownWater = Mathf.Lerp(Mathf.Max(_shownWater, 0f), Water, ease);
		_shownSoil = Mathf.Lerp(Mathf.Max(_shownSoil, 0f), Soil, ease);

		_hazeMaterial.SetShaderParameter("tint", HazeTint.Lerp(LivingHaze, _shownAir));
		_hazeMaterial.SetShaderParameter("strength", 0.7f + 0.35f * _shownAir);
		// The sea rises from the deepest valleys; at full water the lowlands are ocean.
		float level = SeaLevelFor(_shownWater);
		_sea.Visible = _shownWater > 0.01f;
		_sea.Scale = Vector3.One * level;
		SurfaceMaterial.SetShaderParameter("bloom", _shownSoil);
		SurfaceMaterial.SetShaderParameter("planet_center", GlobalPosition);
	}

	/// <summary>Distance from the center to the sea surface at the current water.</summary>
	public float SeaLevel => SeaLevelFor(Water);

	private float SeaLevelFor(float water) => Radius * (1f - Hilliness) + water * Radius * Hilliness * 1.1f;

	/// <summary>A unit sphere of sea, scaled to the water level.</summary>
	private MeshInstance3D BuildSea()
	{
		_sea = new MeshInstance3D
		{
			Name = "Sea",
			Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 96, Rings = 48 },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.38f, 0.64f, 0.86f, 0.8f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				Roughness = 0.08f,
				Metallic = 0.1f,
				RimEnabled = true,
				Rim = 0.4f,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};
		return _sea;
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
		_hazeMaterial = material;
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
