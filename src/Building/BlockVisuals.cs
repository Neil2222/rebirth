using System.Collections.Generic;
using Godot;

namespace Rebirth.Building;

/// <summary>
/// Extra geometry on top of a block's cube so functional blocks are recognisable and their facing
/// is visible. Built in block-local space (cell center at origin, -Z = forward). Retro-futuristic
/// toy style: chrome trims, bubble glass, chunky round parts, and light only where a lamp is.
/// </summary>
public static class BlockVisuals
{
	public const string FlameName = "Flame";
	public const string DrillBitName = "Bit";
	public const string RotorName = "Rotor";
	public const string LanceRingsName = "Rings";
	public const string LanceGlowName = "Glow";
	private const float H = BlockGrid.CellSize * 0.5f;

	public static Node3D? CreateDecoration(BlockDefinition block, Color paint) => block.Kind switch
	{
		BlockKind.Cockpit => Cockpit(paint),
		BlockKind.Thruster => Thruster(paint),
		BlockKind.Gyroscope => Gyroscope(paint),
		BlockKind.Battery => Battery(paint),
		BlockKind.SolarPanel => SolarPanel(paint),
		BlockKind.CargoContainer => CargoContainer(paint),
		BlockKind.Refinery => Refinery(paint),
		BlockKind.Fabricator => Fabricator(),
		BlockKind.AutoDrill => AutoDrill(paint),
		BlockKind.BotCore => BotCore(),
		BlockKind.Uplink => Uplink(paint),
		BlockKind.AirProcessor => AirProcessor(paint),
		BlockKind.Hydrator => Hydrator(paint),
		BlockKind.SeedGarden => SeedGarden(paint),
		BlockKind.Incubator => Incubator(paint),
		BlockKind.BreachLance => BreachLance(paint),
		BlockKind.Firewall => Firewall(paint),
		BlockKind.PowerPylon => PowerPylon(paint),
		BlockKind.StoneBurner => StoneBurner(paint),
		BlockKind.WindTurbine => WindTurbine(paint),
		BlockKind.GeothermalTap => GeothermalTap(paint),
		_ => null,
	};

	/// <summary>
	/// Translucent preview of a block (its shape plus its decoration) drawn entirely with
	/// <paramref name="material"/>, so its colour can signal whether placement is allowed.
	/// </summary>
	public static Node3D CreateGhost(BlockDefinition block, Color paint, Material material)
	{
		var root = new Node3D();
		Mesh? body = block.Shape switch
		{
			BlockShape.Custom => null,
			BlockShape.None => new BoxMesh { Size = Vector3.One * BlockGrid.CellSize * 0.6f },
			_ => BlockMesher.Build(new Dictionary<Vector3I, PlacedBlock> { [Vector3I.Zero] = new(block, Basis.Identity, paint) }, _ => 1f),
		};
		if (body is not null)
			root.AddChild(new MeshInstance3D
			{
				Mesh = body,
				Scale = Vector3.One * 1.002f,
				MaterialOverride = material,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		if (CreateDecoration(block, paint) is { } decoration)
		{
			foreach (var node in decoration.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false))
			{
				var mesh = (MeshInstance3D)node;
				mesh.MaterialOverride = material;
				mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			}
			// A short static flame makes a thruster's exhaust side obvious while placing.
			if (decoration.GetNodeOrNull<Node3D>(FlameName) is { } flame)
				flame.Scale = new Vector3(1, 1, 0.8f);
			root.AddChild(decoration);
		}
		return root;
	}

	/// <summary>Unshaded see-through material for ghosts.</summary>
	public static StandardMaterial3D CreateGhostMaterial() => new()
	{
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
	};

	public static readonly Color GhostValid = new(0.55f, 0.95f, 0.65f, 0.35f);
	public static readonly Color GhostInvalid = new(1f, 0.45f, 0.4f, 0.35f);

	/// <summary>One-shot burst of tumbling chunks where a block was destroyed.</summary>
	public static void SpawnDebris(Node parent, Vector3 position, Color color)
	{
		var particles = new CpuParticles3D
		{
			Mesh = new BoxMesh { Size = Vector3.One * 0.35f, Material = Plastic(color) },
			Amount = 16,
			Lifetime = 2.0,
			OneShot = true,
			Explosiveness = 1f,
			Direction = Vector3.Up,
			Spread = 180f,
			Gravity = Vector3.Zero,
			InitialVelocityMin = 2f,
			InitialVelocityMax = 6f,
			AngularVelocityMin = -300f,
			AngularVelocityMax = 300f,
			ScaleAmountMin = 0.5f,
			ScaleAmountMax = 1.3f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
			EmissionBoxExtents = Vector3.One * H,
			Position = position,
		};
		parent.AddChild(particles);
		particles.Emitting = true;
		particles.Finished += particles.QueueFree;
	}

	// ------------------------------------------------------------ materials

	private static StandardMaterial3D Plastic(Color color) => new() { AlbedoColor = color, Roughness = 0.5f };

	private static StandardMaterial3D Chrome() => new() { AlbedoColor = new Color(0.86f, 0.84f, 0.8f), Metallic = 0.85f, Roughness = 0.22f };

	private static StandardMaterial3D Brass() => new() { AlbedoColor = new Color(0.86f, 0.66f, 0.3f), Metallic = 0.8f, Roughness = 0.3f };

	/// <summary>A small light: gently emissive so it reads as "on" without flooding the scene.</summary>
	private static StandardMaterial3D Lamp(Color color, float energy = 1.2f) => new()
	{
		AlbedoColor = color,
		EmissionEnabled = true,
		Emission = color,
		EmissionEnergyMultiplier = energy,
		Roughness = 0.3f,
	};

	private static MeshInstance3D Part(Mesh mesh, Vector3 position, Basis? basis = null) =>
		new() { Mesh = mesh, Transform = new Transform3D(basis ?? Basis.Identity, position) };

	private static readonly Basis ToZ = new(Vector3.Right, Mathf.Pi / 2f);   // Y-aligned primitives onto Z

	/// <summary>
	/// The mounting plate machines with their own silhouette stand on: it fills the bottom of the cell so
	/// the block still reads as sitting on its neighbour. Along <paramref name="side"/> (default: the floor).
	/// </summary>
	private static MeshInstance3D Plate(Color paint, Vector3? side = null)
	{
		var down = side ?? Vector3.Down;
		var size = new Vector3(
			Mathf.Abs(down.X) > 0.5f ? 0.3f : 2.36f,
			Mathf.Abs(down.Y) > 0.5f ? 0.3f : 2.36f,
			Mathf.Abs(down.Z) > 0.5f ? 0.3f : 2.36f);
		return Part(new BoxMesh { Size = size, Material = Plastic(paint.Darkened(0.12f)) }, down * (H - 0.15f));
	}

	// ------------------------------------------------------------ blocks

	private static Node3D Cockpit(Color paint)
	{
		var root = new Node3D();
		var glass = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.7f, 0.9f, 1f, 0.35f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			Metallic = 0.3f,
			Roughness = 0.05f,
			// Back faces culled: from inside the pilot sees straight out through the bubble.
			CullMode = BaseMaterial3D.CullModeEnum.Back,
		};
		// A bubble canopy: a flattened sphere, half of it sunk into the block.
		root.AddChild(new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.95f, Height = 1.9f, Material = glass },
			Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(1f, 0.8f, 0.55f)), new Vector3(0, 0.15f, -H)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1.02f, Material = Chrome() }, new Vector3(0, 0.15f, -H), ToZ.Scaled(new Vector3(1f, 1f, 0.8f))));
		// Antenna with a friendly blinker on top.
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.05f, Height = 0.7f, Material = Chrome() }, new Vector3(0.7f, H + 0.35f, 0.5f)));
		root.AddChild(Part(new SphereMesh { Radius = 0.1f, Height = 0.2f, Material = Lamp(new Color(1f, 0.35f, 0.25f)) }, new Vector3(0.7f, H + 0.72f, 0.5f)));
		return root;
	}

	/// <summary>A round engine body on a mounting plate (the -Z side it pushes towards), with a bell nozzle out the back.</summary>
	private static Node3D Thruster(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint, Vector3.Forward));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0.95f, Height = 1.9f, Material = Plastic(paint) }, new Vector3(0, 0, 0.1f), ToZ));
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.88f, OuterRadius = 1.02f, Material = Chrome() }, new Vector3(0, 0, -0.7f), ToZ));
		// Bell nozzle on the exhaust side (+Z), with a painted rim.
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.95f, Height = 0.6f, Material = Plastic(Palette.Slate) }, new Vector3(0, 0, H + 0.3f), ToZ));
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.88f, OuterRadius = 1.02f, Material = Plastic(paint.Lightened(0.15f)) }, new Vector3(0, 0, H + 0.6f), ToZ));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.75f, BottomRadius = 0.75f, Height = 0.04f, Material = Lamp(new Color(1f, 0.6f, 0.25f), 0.8f) }, new Vector3(0, 0, H + 0.58f), ToZ));

		var flameMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			AlbedoColor = new Color(1f, 0.62f, 0.25f, 0.7f),
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		// Flame is a soft cone of length 1 starting at the nozzle; its Z scale is the throttle.
		var flame = new Node3D { Name = FlameName, Position = new Vector3(0, 0, H + 0.6f), Scale = new Vector3(1, 1, 0.001f) };
		flame.AddChild(new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.08f, Height = 1f, Material = flameMaterial },
			Transform = new Transform3D(ToZ, new Vector3(0, 0, 0.5f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		root.AddChild(flame);
		return root;
	}

	/// <summary>A painted ball spinning inside two brass rings, on a little pedestal.</summary>
	private static Node3D Gyroscope(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.3f, Height = 0.5f, Material = Chrome() }, new Vector3(0, -H + 0.5f, 0)));
		root.AddChild(Part(new SphereMesh { Radius = 0.72f, Height = 1.44f, Material = Plastic(paint) }, Vector3.Zero));
		var ring = new TorusMesh { InnerRadius = 1.02f, OuterRadius = 1.16f, Material = Brass() };
		root.AddChild(new MeshInstance3D { Mesh = ring });
		root.AddChild(Part(ring, Vector3.Zero, new Basis(Vector3.Right, Mathf.Pi / 2f)));
		root.AddChild(Part(ring, Vector3.Zero, new Basis(Vector3.Forward, Mathf.Pi / 2f)));
		return root;
	}

	/// <summary>Three chunky round cells with chrome caps and a charge lamp each.</summary>
	private static Node3D Battery(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		Color[] lamps = [new(0.4f, 0.95f, 0.5f), new(0.4f, 0.95f, 0.5f), new(1f, 0.8f, 0.3f)];
		for (int i = 0; i < 3; i++)
		{
			float x = -0.78f + i * 0.78f;
			root.AddChild(Part(new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 1.8f, Material = Plastic(paint) }, new Vector3(x, -0.05f, 0)));
			root.AddChild(Part(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.3f, Height = 0.18f, Material = Chrome() }, new Vector3(x, 0.94f, 0)));
			root.AddChild(Part(new TorusMesh { InnerRadius = 0.33f, OuterRadius = 0.41f, Material = Chrome() }, new Vector3(x, -0.3f, 0)));
			root.AddChild(Part(new SphereMesh { Radius = 0.1f, Height = 0.2f, Material = Lamp(lamps[i]) }, new Vector3(x, 0.35f, -0.36f)));
		}
		return root;
	}

	/// <summary>Deep-blue cells in a frame, held up on a post. The +Y face must point at the sun.</summary>
	private static Node3D SolarPanel(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.2f, Height = 1.9f, Material = Chrome() }, new Vector3(0, -0.1f, 0)));
		root.AddChild(Part(new BoxMesh { Size = new Vector3(2.44f, 0.16f, 2.44f), Material = Plastic(paint) }, new Vector3(0, H - 0.2f, 0)));
		var cells = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.2f, 0.45f), Metallic = 0.4f, Roughness = 0.2f };
		var cell = new BoxMesh { Size = new Vector3(1.05f, 0.06f, 1.05f), Material = cells };
		foreach (var (x, z) in new[] { (-0.58f, -0.58f), (0.58f, -0.58f), (-0.58f, 0.58f), (0.58f, 0.58f) })
			root.AddChild(Part(cell, new Vector3(x, H - 0.1f, z)));
		return root;
	}

	/// <summary>A ribbed crate: chunky bands around it, and a lighter door with two handles on the front.</summary>
	private static Node3D CargoContainer(Color paint)
	{
		var root = new Node3D();
		var rib = Plastic(paint.Darkened(0.15f));
		foreach (float x in new[] { -0.8f, 0f, 0.8f })
			root.AddChild(Part(new BoxMesh { Size = new Vector3(0.16f, 2.62f, 2.62f), Material = rib }, new Vector3(x, 0, 0)));
		root.AddChild(Part(new BoxMesh { Size = new Vector3(2.62f, 0.16f, 2.62f), Material = rib }, new Vector3(0, 1.1f, 0)));
		root.AddChild(Part(new BoxMesh { Size = new Vector3(1.3f, 1.6f, 0.08f), Material = Plastic(paint.Lightened(0.25f)) }, new Vector3(0, -0.05f, -H - 0.08f)));
		var handle = new CapsuleMesh { Radius = 0.07f, Height = 0.6f, Material = Chrome() };
		root.AddChild(Part(handle, new Vector3(-0.3f, -0.05f, -H - 0.18f)));
		root.AddChild(Part(handle, new Vector3(0.3f, -0.05f, -H - 0.18f)));
		return root;
	}

	/// <summary>A round furnace tank with chrome bands and a stubby glowing chimney.</summary>
	private static Node3D Refinery(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		root.AddChild(Part(new CylinderMesh { TopRadius = 1.0f, BottomRadius = 1.1f, Height = 1.8f, Material = Plastic(paint) }, new Vector3(0, -0.05f, 0)));
		foreach (float y in new[] { -0.6f, 0.5f })
			root.AddChild(Part(new TorusMesh { InnerRadius = 1.02f, OuterRadius = 1.12f, Material = Chrome() }, new Vector3(0, y, 0)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.6f, Height = 0.8f, Material = Plastic(Palette.Slate) }, new Vector3(0, 1.25f, 0)));
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.55f, Material = Chrome() }, new Vector3(0, 1.65f, 0)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.4f, Height = 0.05f, Material = Lamp(new Color(1f, 0.5f, 0.15f), 1.6f) }, new Vector3(0, 1.63f, 0)));
		root.AddChild(Part(new SphereMesh { Radius = 0.12f, Height = 0.24f, Material = Lamp(new Color(1f, 0.55f, 0.25f)) }, new Vector3(0, 0f, -1.08f)));
		return root;
	}

	private static Node3D Fabricator()
	{
		// Output hatch on the front (-Z): a chrome arch with a row of work lights.
		var root = new Node3D();
		var chrome = Chrome();
		float z = -H - 0.05f;
		root.AddChild(Part(new BoxMesh { Size = new Vector3(2.1f, 0.16f, 0.1f), Material = chrome }, new Vector3(0, 1.0f, z)));
		root.AddChild(Part(new BoxMesh { Size = new Vector3(0.16f, 2.0f, 0.1f), Material = chrome }, new Vector3(1.0f, 0, z)));
		root.AddChild(Part(new BoxMesh { Size = new Vector3(0.16f, 2.0f, 0.1f), Material = chrome }, new Vector3(-1.0f, 0, z)));
		root.AddChild(Part(new BoxMesh { Size = new Vector3(1.84f, 1.84f, 0.04f), Material = Plastic(new Color(0.2f, 0.2f, 0.25f)) }, new Vector3(0, -0.08f, z + 0.03f)));
		for (int i = 0; i < 4; i++)
			root.AddChild(Part(new SphereMesh { Radius = 0.08f, Height = 0.16f, Material = Lamp(new Color(1f, 0.85f, 0.5f)) }, new Vector3(-0.6f + i * 0.4f, 1.0f, z - 0.08f)));
		return root;
	}

	/// <summary>
	/// A little drill tower lying along Z: a mounting plate at the back, a round motor with a chrome lattice,
	/// and a chunky drill head on the front (-Z) with a spinning cone bit.
	/// </summary>
	private static Node3D AutoDrill(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint, Vector3.Back));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.85f, BottomRadius = 1.0f, Height = 1.5f, Material = Plastic(paint) }, new Vector3(0, 0, 0.2f), ToZ));
		var strut = new BoxMesh { Size = new Vector3(0.14f, 0.14f, 2.1f), Material = Chrome() };
		foreach (var (x, y) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) })
			root.AddChild(Part(strut, new Vector3(x * 0.95f, y * 0.95f, 0f)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.95f, BottomRadius = 1.05f, Height = 0.35f, Material = Plastic(Palette.Slate) }, new Vector3(0, 0, -H + 0.17f), ToZ));
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.78f, OuterRadius = 0.95f, Material = Chrome() }, new Vector3(0, 0, -H), ToZ));
		var bit = new Node3D { Name = DrillBitName, Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2f), new Vector3(0, 0, -H)) };
		// In the bit's frame +Y points out of the face, so spinning about Y turns it in place.
		bit.AddChild(Part(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.7f, Height = 1.2f, Material = Chrome() }, new Vector3(0, 0.6f, 0)));
		bit.AddChild(Part(new BoxMesh { Size = new Vector3(1.3f, 0.08f, 0.12f), Material = Plastic(Palette.Orange) }, new Vector3(0, 0.3f, 0)));
		root.AddChild(bit);
		root.AddChild(Part(new SphereMesh { Radius = 0.1f, Height = 0.2f, Material = Lamp(new Color(1f, 0.75f, 0.3f)) }, new Vector3(0, 0.88f, 0.4f)));
		return root;
	}

	/// <summary>
	/// A slim lattice mast with a cross-arm and brass insulators; cables hang from the top (see
	/// <see cref="BlockGrid.PylonTop"/>, 3.1 m above the cell).
	/// </summary>
	private static Node3D PowerPylon(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		var steel = Plastic(paint.Darkened(0.1f));
		float bottom = -H + 0.3f, top = H + 3.1f, height = top - bottom;
		foreach (var (x, z) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) })
		{
			// Four legs leaning in from the plate towards the top.
			var from = new Vector3(x * 0.8f, bottom, z * 0.8f);
			var to = new Vector3(x * 0.15f, top - 0.3f, z * 0.15f);
			var leg = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.09f, Height = from.DistanceTo(to), Material = steel };
			root.AddChild(Part(leg, (from + to) * 0.5f, BasisAlong((to - from).Normalized())));
		}
		for (int i = 1; i <= 3; i++)
		{
			float t = i / 4f;
			float y = bottom + height * t;
			float half = Mathf.Lerp(0.8f, 0.15f, t);
			foreach (var (size, offset) in new[] { (new Vector3(half * 2f, 0.07f, 0.07f), new Vector3(0, y, half)), (new Vector3(half * 2f, 0.07f, 0.07f), new Vector3(0, y, -half)),
				(new Vector3(0.07f, 0.07f, half * 2f), new Vector3(half, y, 0)), (new Vector3(0.07f, 0.07f, half * 2f), new Vector3(-half, y, 0)) })
				root.AddChild(Part(new BoxMesh { Size = size, Material = steel }, offset));
		}
		root.AddChild(Part(new BoxMesh { Size = new Vector3(1.8f, 0.14f, 0.14f), Material = steel }, new Vector3(0, top - 0.35f, 0)));
		var brass = Brass();
		foreach (float x in new[] { -0.8f, 0.8f })
			root.AddChild(Part(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.12f, Height = 0.3f, Material = brass }, new Vector3(x, top - 0.15f, 0)));
		root.AddChild(Part(new SphereMesh { Radius = 0.16f, Height = 0.32f, Material = brass }, new Vector3(0, top, 0)));
		return root;
	}

	/// <summary>Turns a Y-aligned primitive to point along <paramref name="dir"/>.</summary>
	private static Basis BasisAlong(Vector3 dir)
	{
		var axis = Vector3.Up.Cross(dir);
		return axis.LengthSquared() < 1e-6f ? Basis.Identity : new Basis(axis.Normalized(), Vector3.Up.AngleTo(dir));
	}

	/// <summary>A round stove with a glowing fire door and a stubby chimney: stone in, warmth and power out.</summary>
	private static Node3D StoneBurner(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.95f, BottomRadius = 1.1f, Height = 1.6f, Material = Plastic(paint) }, new Vector3(0, -0.15f, 0)));
		root.AddChild(Part(new SphereMesh { Radius = 0.95f, Height = 1.0f, IsHemisphere = true, Material = Plastic(paint) }, new Vector3(0, 0.65f, 0)));
		root.AddChild(Part(new TorusMesh { InnerRadius = 1.0f, OuterRadius = 1.14f, Material = Chrome() }, new Vector3(0, -0.6f, 0)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.28f, Height = 1.1f, Material = Plastic(Palette.Slate) }, new Vector3(0.35f, 1.4f, 0.3f)));
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.2f, OuterRadius = 0.3f, Material = Chrome() }, new Vector3(0.35f, 1.95f, 0.3f)));
		// Fire door on the front (-Z): a dark frame around a warm glow that breathes with the fire.
		root.AddChild(Part(new BoxMesh { Size = new Vector3(0.9f, 0.7f, 0.12f), Material = Plastic(Palette.Slate) }, new Vector3(0, -0.2f, -1.02f)));
		var fire = new Node3D { Name = FlameName, Position = new Vector3(0, -0.2f, -1.08f) };
		fire.AddChild(Part(new BoxMesh { Size = new Vector3(0.7f, 0.5f, 0.04f), Material = Lamp(new Color(1f, 0.55f, 0.2f), 2.2f) }, Vector3.Zero));
		fire.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 1.2f, OmniRange = 6f, Position = new Vector3(0, 0, -0.4f) });
		root.AddChild(fire);
		return root;
	}

	/// <summary>A tall slim mast with a nacelle and three soft blades that turn with the wind.</summary>
	private static Node3D WindTurbine(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		float hub = H + 4.2f;
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.3f, Height = hub + H - 0.3f, Material = Plastic(Palette.Cream) }, new Vector3(0, (hub - H + 0.3f) * 0.5f, 0)));
		root.AddChild(Part(new CapsuleMesh { Radius = 0.28f, Height = 1.3f, Material = Plastic(paint) }, new Vector3(0, hub, 0.2f), ToZ));
		var rotor = new Node3D { Name = RotorName, Position = new Vector3(0, hub, -0.5f) };
		rotor.AddChild(Part(new SphereMesh { Radius = 0.22f, Height = 0.44f, Material = Plastic(Palette.Coral) }, Vector3.Zero));
		for (int i = 0; i < 3; i++)
		{
			var blade = new Node3D { Rotation = new Vector3(0, 0, i * Mathf.Tau / 3f) };
			blade.AddChild(Part(new BoxMesh { Size = new Vector3(0.28f, 2.4f, 0.06f), Material = Plastic(Palette.Cream) }, new Vector3(0, 1.35f, 0), new Basis(Vector3.Up, 0.25f)));
			rotor.AddChild(blade);
		}
		root.AddChild(rotor);
		return root;
	}

	/// <summary>A squat dome over a warm vent, with chunky pipes curling down into the ground.</summary>
	private static Node3D GeothermalTap(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		float floor = -H + 0.3f;
		root.AddChild(Part(new SphereMesh { Radius = 1.05f, Height = 1.05f * 1.6f, IsHemisphere = true, Material = Plastic(paint) }, new Vector3(0, floor, 0)));
		root.AddChild(Part(new TorusMesh { InnerRadius = 1.0f, OuterRadius = 1.15f, Material = Chrome() }, new Vector3(0, floor + 0.05f, 0)));
		foreach (var (x, z) in new[] { (0.85f, 0.85f), (-0.85f, 0.85f), (0.85f, -0.85f), (-0.85f, -0.85f) })
			root.AddChild(Part(new TorusMesh { InnerRadius = 0.2f, OuterRadius = 0.36f, Material = Brass() }, new Vector3(x, floor + 0.05f, z), new Basis(new Vector3(-z, 0, x).Normalized(), Mathf.Pi / 2f)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.4f, Height = 0.5f, Material = Plastic(Palette.Slate) }, new Vector3(0, floor + 1.75f, 0)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.26f, Height = 0.05f, Material = Lamp(new Color(1f, 0.45f, 0.2f), 2f) }, new Vector3(0, floor + 2.0f, 0)));
		return root;
	}

	/// <summary>
	/// A tube junction: a glass hub with a glass pipe towards each linked neighbour, so parcels can be
	/// seen sliding through. Built by the grid, which knows the neighbours.
	/// </summary>
	public static Node3D CreateTube(Color paint, IEnumerable<Vector3I> links)
	{
		var root = new Node3D();
		var glass = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.85f, 0.95f, 1f, 0.16f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			Roughness = 0.05f,
			Metallic = 0.2f,
		};
		var collar = Plastic(paint);
		root.AddChild(Part(new SphereMesh { Radius = 0.62f, Height = 1.24f, Material = glass }, Vector3.Zero));
		foreach (var link in links)
		{
			Vector3 dir = link;
			// Cylinders run along Y: turn Y onto the link direction.
			var basis = Basis.LookingAt(dir, dir.Abs().IsEqualApprox(Vector3.Up) ? Vector3.Forward : Vector3.Up) * new Basis(Vector3.Right, Mathf.Pi / 2f);
			root.AddChild(Part(new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.45f, Height = H, Material = glass }, dir * (H * 0.5f), basis));
			root.AddChild(Part(new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.56f, Material = collar }, dir * (H - 0.05f), basis));
		}
		return root;
	}

	/// <summary>A friendly face on the front (-Z): a big round lamp eye, a visor band and an antenna.</summary>
	private static Node3D BotCore()
	{
		var root = new Node3D();
		root.AddChild(Part(new BoxMesh { Size = new Vector3(2.0f, 0.9f, 0.12f), Material = Plastic(Palette.Slate) }, new Vector3(0, 0.15f, -H - 0.05f)));
		root.AddChild(Part(new SphereMesh { Radius = 0.3f, Height = 0.6f, Material = Lamp(new Color(0.55f, 0.95f, 1f)) }, new Vector3(-0.45f, 0.15f, -H - 0.08f)));
		root.AddChild(Part(new SphereMesh { Radius = 0.3f, Height = 0.6f, Material = Lamp(new Color(0.55f, 0.95f, 1f)) }, new Vector3(0.45f, 0.15f, -H - 0.08f)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.06f, Height = 0.9f, Material = Chrome() }, new Vector3(0.6f, H + 0.45f, 0.3f)));
		root.AddChild(Part(new SphereMesh { Radius = 0.14f, Height = 0.28f, Material = Lamp(new Color(1f, 0.55f, 0.3f)) }, new Vector3(0.6f, H + 0.95f, 0.3f)));
		// Hover ring underneath: the bot's own little drive.
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.7f, OuterRadius = 0.95f, Material = Lamp(new Color(1f, 0.7f, 0.4f)) }, new Vector3(0, -H - 0.05f, 0)));
		return root;
	}

	/// <summary>A little tracking station: an equipment box, a chrome mast, a dish and a warm beacon.</summary>
	private static Node3D Uplink(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		root.AddChild(Part(new BoxMesh { Size = new Vector3(1.3f, 0.9f, 1.3f), Material = Plastic(paint) }, new Vector3(0, -H + 0.75f, 0)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.13f, Height = 2.6f, Material = Chrome() }, new Vector3(0, 0.35f, 0)));
		var tilt = new Basis(Vector3.Right, 0.6f);
		root.AddChild(Part(new CylinderMesh { TopRadius = 1.05f, BottomRadius = 0.25f, Height = 0.35f, Material = Plastic(Palette.Cream) }, new Vector3(0, H + 0.4f, 0), tilt));
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1.08f, Material = Plastic(Palette.Orange) }, new Vector3(0, H + 0.57f, 0) + tilt * new Vector3(0, 0.02f, 0), tilt));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.04f, Height = 0.7f, Material = Chrome() }, new Vector3(0, H + 0.65f, 0) + tilt * new Vector3(0, 0.3f, 0), tilt));
		root.AddChild(Part(new SphereMesh { Radius = 0.12f, Height = 0.24f, Material = Lamp(new Color(1f, 0.55f, 0.3f), 2f) }, new Vector3(0, H + 0.65f, 0) + tilt * new Vector3(0, 0.68f, 0)));
		return root;
	}

	/// <summary>A round body with two striped chimneys: the planet's lungs.</summary>
	private static Node3D AirProcessor(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		root.AddChild(Part(new CylinderMesh { TopRadius = 1.0f, BottomRadius = 1.08f, Height = 1.6f, Material = Plastic(paint) }, new Vector3(0, -0.15f, 0)));
		foreach (float x in new[] { -0.45f, 0.5f })
		{
			float h = x < 0f ? 1.4f : 1.05f;
			float bottom = 0.6f;
			root.AddChild(Part(new CylinderMesh { TopRadius = 0.28f, BottomRadius = 0.34f, Height = h, Material = Plastic(Palette.Cream) }, new Vector3(x, bottom + h * 0.5f, 0.1f)));
			root.AddChild(Part(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.14f, Material = Plastic(Palette.Coral) }, new Vector3(x, bottom + h * 0.72f, 0.1f)));
			root.AddChild(Part(new TorusMesh { InnerRadius = 0.2f, OuterRadius = 0.32f, Material = Chrome() }, new Vector3(x, bottom + h, 0.1f)));
		}
		root.AddChild(Part(new SphereMesh { Radius = 0.1f, Height = 0.2f, Material = Lamp(new Color(0.6f, 0.9f, 1f)) }, new Vector3(0, -0.1f, -1.06f)));
		return root;
	}

	/// <summary>A tall glass tank of sea-blue water in chrome rings.</summary>
	private static Node3D Hydrator(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		var glass = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.95f, 1f, 0.25f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f };
		var water = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.65f, 0.95f, 0.85f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.1f };
		root.AddChild(Part(new CylinderMesh { TopRadius = 1.0f, BottomRadius = 1.0f, Height = 2.1f, Material = glass }, new Vector3(0, 0.1f, 0)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.92f, BottomRadius = 0.92f, Height = 1.3f, Material = water }, new Vector3(0, -0.3f, 0)));
		foreach (float y in new[] { -0.9f, 1.15f })
			root.AddChild(Part(new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1.1f, Material = Chrome() }, new Vector3(0, y, 0)));
		return root;
	}

	/// <summary>A bubble greenhouse with little round shrubs inside.</summary>
	private static Node3D SeedGarden(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		var glass = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 1f, 0.92f, 0.22f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f };
		float floor = -H + 0.3f;
		root.AddChild(Part(new SphereMesh { Radius = 1.15f, Height = 1.15f * 1.6f, IsHemisphere = true, Material = glass }, new Vector3(0, floor, 0)));
		root.AddChild(Part(new TorusMesh { InnerRadius = 1.08f, OuterRadius = 1.2f, Material = Chrome() }, new Vector3(0, floor + 0.02f, 0)));
		var leaf = Plastic(new Color(0.42f, 0.72f, 0.36f));
		foreach (var (x, z, r) in new[] { (-0.4f, -0.2f, 0.36f), (0.35f, 0.25f, 0.3f), (0.1f, -0.45f, 0.26f), (-0.2f, 0.4f, 0.24f) })
			root.AddChild(Part(new SphereMesh { Radius = r, Height = r * 2f, Material = leaf }, new Vector3(x, floor + r * 0.8f, z)));
		return root;
	}

	/// <summary>A glass capsule with a warm glow inside, cradled in chrome rings: where people wake up.</summary>
	private static Node3D Incubator(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		var glass = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.95f, 0.88f, 0.3f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f };
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0.95f, Height = 0.4f, Material = Plastic(paint) }, new Vector3(0, -H + 0.5f, 0)));
		root.AddChild(Part(new CapsuleMesh { Radius = 0.65f, Height = 2.0f, Material = glass }, new Vector3(0, 0.25f, 0)));
		root.AddChild(Part(new SphereMesh { Radius = 0.3f, Height = 0.6f, Material = Lamp(new Color(1f, 0.72f, 0.5f), 1.4f) }, new Vector3(0, 0.25f, 0)));
		foreach (float y in new[] { -0.35f, 0.85f })
			root.AddChild(Part(new TorusMesh { InnerRadius = 0.62f, OuterRadius = 0.76f, Material = Chrome() }, new Vector3(0, y, 0)));
		return root;
	}

	/// <summary>
	/// A tall brass-and-cream spire on a round plinth, with three chrome rings that spin faster as it charges
	/// and a glowing tip where the beam will leave. It points along the block's +Y.
	/// </summary>
	private static Node3D BreachLance(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.95f, BottomRadius = 1.15f, Height = 0.7f, Material = Plastic(paint) }, new Vector3(0, -H + 0.65f, 0)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.9f, Height = 7.0f, Material = Plastic(Palette.Cream) }, new Vector3(0, 2.75f, 0)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 0.3f, Material = Plastic(Palette.Mustard) }, new Vector3(0, H + 2f, 0)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.4f, Height = 0.3f, Material = Plastic(Palette.Coral) }, new Vector3(0, H + 4.2f, 0)));
		var rings = new Node3D { Name = LanceRingsName };
		for (int i = 0; i < 3; i++)
		{
			var ring = Part(new TorusMesh { InnerRadius = 1.3f + i * 0.35f, OuterRadius = 1.45f + i * 0.35f, Material = Chrome() }, new Vector3(0, H + 1.5f + i * 1.6f, 0),
				new Basis(Vector3.Right, 0.35f * (i - 1)));
			rings.AddChild(ring);
		}
		root.AddChild(rings);
		var glow = Part(new SphereMesh { Radius = 0.45f, Height = 0.9f, Material = Lamp(new Color(1f, 0.85f, 0.55f), 2.5f) }, new Vector3(0, H + 6.2f, 0));
		glow.Name = LanceGlowName;
		root.AddChild(glow);
		return root;
	}

	/// <summary>A little shield dome with a steady green lamp: all clear.</summary>
	private static Node3D Firewall(Color paint)
	{
		var root = new Node3D();
		root.AddChild(Plate(paint));
		float floor = -H + 0.3f;
		root.AddChild(Part(new SphereMesh { Radius = 1.1f, Height = 1.1f * 1.7f, IsHemisphere = true, Material = Plastic(paint) }, new Vector3(0, floor, 0)));
		root.AddChild(Part(new TorusMesh { InnerRadius = 1.05f, OuterRadius = 1.18f, Material = Chrome() }, new Vector3(0, floor + 0.05f, 0)));
		root.AddChild(Part(new SphereMesh { Radius = 0.16f, Height = 0.32f, Material = Lamp(new Color(0.55f, 1f, 0.6f), 1.8f) }, new Vector3(0, floor + 1.9f, 0)));
		return root;
	}
}
