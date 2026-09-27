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
	private const float H = BlockGrid.CellSize * 0.5f;

	public static Node3D? CreateDecoration(BlockDefinition block, Color paint) => block.Kind switch
	{
		BlockKind.Cockpit => Cockpit(paint),
		BlockKind.Thruster => Thruster(paint),
		BlockKind.Gyroscope => Gyroscope(),
		BlockKind.Battery => Battery(),
		BlockKind.SolarPanel => SolarPanel(),
		BlockKind.CargoContainer => CargoContainer(paint),
		BlockKind.Refinery => Refinery(),
		BlockKind.Fabricator => Fabricator(),
		BlockKind.AutoDrill => AutoDrill(),
		BlockKind.BotCore => BotCore(),
		BlockKind.Uplink => Uplink(),
		_ => null,
	};

	/// <summary>
	/// Translucent preview of a block (cube plus its decoration) drawn entirely with
	/// <paramref name="material"/>, so its colour can signal whether placement is allowed.
	/// </summary>
	public static Node3D CreateGhost(BlockDefinition block, Color paint, Material material)
	{
		var root = new Node3D();
		root.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = Vector3.One * BlockGrid.CellSize * 1.002f },
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

	private static Node3D Thruster(Color paint)
	{
		var root = new Node3D();
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

	private static Node3D Gyroscope()
	{
		var root = new Node3D();
		var ring = new TorusMesh { InnerRadius = 1.22f, OuterRadius = 1.36f, Material = Brass() };
		root.AddChild(new MeshInstance3D { Mesh = ring });
		root.AddChild(Part(ring, Vector3.Zero, new Basis(Vector3.Right, Mathf.Pi / 2f)));
		return root;
	}

	private static Node3D Battery()
	{
		// Chrome bands and a row of charge lamps on the front.
		var root = new Node3D();
		var band = new BoxMesh { Size = new Vector3(2.56f, 0.18f, 2.56f), Material = Chrome() };
		root.AddChild(Part(band, new Vector3(0, 0.75f, 0)));
		root.AddChild(Part(band, new Vector3(0, -0.75f, 0)));
		Color[] lamps = [new(0.4f, 0.95f, 0.5f), new(0.4f, 0.95f, 0.5f), new(1f, 0.8f, 0.3f)];
		for (int i = 0; i < lamps.Length; i++)
			root.AddChild(Part(new SphereMesh { Radius = 0.11f, Height = 0.22f, Material = Lamp(lamps[i]) }, new Vector3(-0.35f + i * 0.35f, 0, -H - 0.02f)));
		return root;
	}

	private static Node3D SolarPanel()
	{
		// Deep-blue cells in a chrome frame on the +Y face; that face must point at the sun.
		var root = new Node3D();
		var cells = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.2f, 0.45f), Metallic = 0.4f, Roughness = 0.2f };
		var cell = new BoxMesh { Size = new Vector3(1.05f, 0.06f, 1.05f), Material = cells };
		foreach (var (x, z) in new[] { (-0.58f, -0.58f), (0.58f, -0.58f), (-0.58f, 0.58f), (0.58f, 0.58f) })
			root.AddChild(Part(cell, new Vector3(x, H + 0.03f, z)));
		var chrome = Chrome();
		root.AddChild(Part(new BoxMesh { Size = new Vector3(2.4f, 0.08f, 0.1f), Material = chrome }, new Vector3(0, H + 0.03f, 0)));
		root.AddChild(Part(new BoxMesh { Size = new Vector3(0.1f, 0.08f, 2.4f), Material = chrome }, new Vector3(0, H + 0.03f, 0)));
		return root;
	}

	private static Node3D CargoContainer(Color paint)
	{
		// A lighter inset door on the front with two chunky handles.
		var root = new Node3D();
		root.AddChild(Part(new BoxMesh { Size = new Vector3(1.8f, 1.8f, 0.08f), Material = Plastic(paint.Lightened(0.25f)) }, new Vector3(0, 0, -H - 0.04f)));
		var handle = new CapsuleMesh { Radius = 0.07f, Height = 0.6f, Material = Chrome() };
		root.AddChild(Part(handle, new Vector3(-0.45f, 0, -H - 0.14f)));
		root.AddChild(Part(handle, new Vector3(0.45f, 0, -H - 0.14f)));
		return root;
	}

	private static Node3D Refinery()
	{
		// A stubby round chimney with a warm furnace glow in its mouth.
		var root = new Node3D();
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.65f, Height = 0.9f, Material = Plastic(Palette.Slate) }, new Vector3(0, H + 0.45f, 0)));
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.46f, OuterRadius = 0.6f, Material = Chrome() }, new Vector3(0, H + 0.9f, 0)));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.44f, BottomRadius = 0.44f, Height = 0.05f, Material = Lamp(new Color(1f, 0.5f, 0.15f), 1.6f) }, new Vector3(0, H + 0.88f, 0)));
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

	private static Node3D AutoDrill()
	{
		// A chunky drill head on the front (-Z): chrome collar and a spinning cone bit.
		var root = new Node3D();
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.95f, BottomRadius = 1.05f, Height = 0.35f, Material = Plastic(Palette.Slate) }, new Vector3(0, 0, -H - 0.17f), ToZ));
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.78f, OuterRadius = 0.95f, Material = Chrome() }, new Vector3(0, 0, -H - 0.35f), ToZ));
		var bit = new Node3D { Name = DrillBitName, Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2f), new Vector3(0, 0, -H - 0.35f)) };
		// In the bit's frame +Y points out of the face, so spinning about Y turns it in place.
		bit.AddChild(Part(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.7f, Height = 1.2f, Material = Chrome() }, new Vector3(0, 0.6f, 0)));
		bit.AddChild(Part(new BoxMesh { Size = new Vector3(1.3f, 0.08f, 0.12f), Material = Plastic(Palette.Orange) }, new Vector3(0, 0.3f, 0)));
		root.AddChild(bit);
		root.AddChild(Part(new SphereMesh { Radius = 0.1f, Height = 0.2f, Material = Lamp(new Color(1f, 0.75f, 0.3f)) }, new Vector3(0.9f, 0.9f, -H - 0.03f)));
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

	/// <summary>A little radar dish on a chrome mast with a warm beacon, like a 70s tracking station.</summary>
	private static Node3D Uplink()
	{
		var root = new Node3D();
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.12f, Height = 1.2f, Material = Chrome() }, new Vector3(0, H + 0.6f, 0)));
		// Dish: a wide, shallow cone tipped back towards the sky.
		var tilt = new Basis(Vector3.Right, 0.6f);
		root.AddChild(Part(new CylinderMesh { TopRadius = 1.05f, BottomRadius = 0.25f, Height = 0.35f, Material = Plastic(Palette.Cream) }, new Vector3(0, H + 1.35f, 0), tilt));
		root.AddChild(Part(new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1.08f, Material = Plastic(Palette.Orange) }, new Vector3(0, H + 1.52f, 0) + tilt * new Vector3(0, 0.02f, 0), tilt));
		root.AddChild(Part(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.04f, Height = 0.7f, Material = Chrome() }, new Vector3(0, H + 1.6f, 0) + tilt * new Vector3(0, 0.3f, 0), tilt));
		root.AddChild(Part(new SphereMesh { Radius = 0.12f, Height = 0.24f, Material = Lamp(new Color(1f, 0.55f, 0.3f), 2f) }, new Vector3(0, H + 1.6f, 0) + tilt * new Vector3(0, 0.68f, 0)));
		return root;
	}
}
