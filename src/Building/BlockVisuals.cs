using Godot;

namespace Rebirth.Building;

/// <summary>
/// Extra geometry on top of a block's cube so functional blocks are recognisable and their facing
/// is visible. Built in block-local space (cell center at origin, -Z = forward). Accents glow in the
/// block's paint colour; bodies are dark metal.
/// </summary>
public static class BlockVisuals
{
	public const string FlameName = "Flame";
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
		BlockKind.Fabricator => Fabricator(paint),
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

	public static readonly Color GhostValid = new(0.3f, 1f, 0.6f, 0.3f);
	public static readonly Color GhostInvalid = new(1f, 0.25f, 0.2f, 0.3f);

	/// <summary>One-shot burst of tumbling glowing fragments where a block was destroyed.</summary>
	public static void SpawnDebris(Node parent, Vector3 position, Color color)
	{
		var particles = new CpuParticles3D
		{
			Mesh = new BoxMesh { Size = Vector3.One * 0.3f, Material = Glow(color, 2f) },
			Amount = 18,
			Lifetime = 2.0,
			OneShot = true,
			Explosiveness = 1f,
			Direction = Vector3.Up,
			Spread = 180f,
			Gravity = Vector3.Zero,
			InitialVelocityMin = 2f,
			InitialVelocityMax = 7f,
			AngularVelocityMin = -360f,
			AngularVelocityMax = 360f,
			ScaleAmountMin = 0.5f,
			ScaleAmountMax = 1.4f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
			EmissionBoxExtents = Vector3.One * H,
			Position = position,
		};
		parent.AddChild(particles);
		particles.Emitting = true;
		particles.Finished += particles.QueueFree;
	}

	private static StandardMaterial3D Glow(Color color, float energy = 2.5f) => new()
	{
		AlbedoColor = color * 0.2f,
		EmissionEnabled = true,
		Emission = color,
		EmissionEnergyMultiplier = energy,
	};

	private static StandardMaterial3D DarkMetal() => new()
	{
		AlbedoColor = new Color(0.05f, 0.055f, 0.065f),
		Metallic = 0.8f,
		Roughness = 0.3f,
	};

	private static Node3D Cockpit(Color paint)
	{
		var root = new Node3D();
		var glass = new StandardMaterial3D
		{
			AlbedoColor = new Color(paint, 0.25f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			EmissionEnabled = true,
			Emission = paint,
			EmissionEnergyMultiplier = 0.6f,
			Metallic = 0.9f,
			Roughness = 0.05f,
		};
		// One-sided quad facing outwards (-Z), so the pilot looks straight through it from inside.
		root.AddChild(new MeshInstance3D
		{
			Mesh = new QuadMesh { Size = new Vector2(2.0f, 1.1f), Material = glass },
			Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(0, 0.25f, -H - 0.01f)),
		});
		return root;
	}

	private static Node3D Thruster(Color paint)
	{
		var root = new Node3D();
		// Cylinders are Y-aligned; tip them onto +Z (the exhaust side).
		var toZ = new Basis(Vector3.Right, Mathf.Pi / 2f);
		root.AddChild(new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 0.95f, BottomRadius = 0.75f, Height = 0.35f, Material = DarkMetal() },
			Transform = new Transform3D(toZ, new Vector3(0, 0, H + 0.175f)),
		});
		root.AddChild(new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = 0.78f, OuterRadius = 0.9f, Material = Glow(paint) },
			Transform = new Transform3D(toZ, new Vector3(0, 0, H + 0.36f)),
		});

		var flameMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			AlbedoColor = new Color(paint, 0.85f),
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		// Flame is a cone of length 1 starting at the nozzle; its Z scale is the throttle.
		var flame = new Node3D { Name = FlameName, Position = new Vector3(0, 0, H + 0.35f), Scale = new Vector3(1, 1, 0.001f) };
		flame.AddChild(new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.05f, Height = 1f, Material = flameMaterial },
			Transform = new Transform3D(toZ, new Vector3(0, 0, 0.5f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		root.AddChild(flame);
		return root;
	}

	private static Node3D Gyroscope(Color paint)
	{
		var root = new Node3D();
		var ring = new TorusMesh { InnerRadius = 1.22f, OuterRadius = 1.32f, Material = Glow(paint) };
		root.AddChild(new MeshInstance3D { Mesh = ring });
		root.AddChild(new MeshInstance3D { Mesh = ring, Basis = new Basis(Vector3.Right, Mathf.Pi / 2f) });
		return root;
	}

	private static Node3D Battery(Color paint)
	{
		// Charge strips on the four side faces.
		var root = new Node3D();
		var strip = new BoxMesh { Size = new Vector3(0.22f, 1.8f, 0.04f), Material = Glow(paint) };
		for (int i = 0; i < 4; i++)
		{
			var basis = new Basis(Vector3.Up, i * Mathf.Pi / 2f);
			root.AddChild(new MeshInstance3D { Mesh = strip, Transform = new Transform3D(basis, basis * new Vector3(0, 0, H + 0.02f)) });
		}
		return root;
	}

	private static Node3D SolarPanel(Color paint)
	{
		// Dark cells with glowing seams on the +Y face; that face must point at the sun.
		var root = new Node3D();
		var cells = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.03f, 0.08f), Metallic = 0.7f, Roughness = 0.15f };
		var cell = new BoxMesh { Size = new Vector3(1.1f, 0.05f, 1.1f), Material = cells };
		foreach (var (x, z) in new[] { (-0.6f, -0.6f), (0.6f, -0.6f), (-0.6f, 0.6f), (0.6f, 0.6f) })
			root.AddChild(new MeshInstance3D { Mesh = cell, Position = new Vector3(x, H + 0.025f, z) });
		var seam = Glow(paint, 1.5f);
		root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.3f, 0.03f, 0.06f), Material = seam }, Position = new Vector3(0, H + 0.02f, 0) });
		root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.03f, 2.3f), Material = seam }, Position = new Vector3(0, H + 0.02f, 0) });
		return root;
	}

	private static Node3D CargoContainer(Color paint)
	{
		// Glowing hatch outline on the front face.
		var root = new Node3D();
		var glow = Glow(paint);
		var horizontal = new BoxMesh { Size = new Vector3(1.6f, 0.08f, 0.04f), Material = glow };
		var vertical = new BoxMesh { Size = new Vector3(0.08f, 1.6f, 0.04f), Material = glow };
		float z = -H - 0.02f;
		root.AddChild(new MeshInstance3D { Mesh = horizontal, Position = new Vector3(0, 0.8f, z) });
		root.AddChild(new MeshInstance3D { Mesh = horizontal, Position = new Vector3(0, -0.8f, z) });
		root.AddChild(new MeshInstance3D { Mesh = vertical, Position = new Vector3(0.8f, 0, z) });
		root.AddChild(new MeshInstance3D { Mesh = vertical, Position = new Vector3(-0.8f, 0, z) });
		return root;
	}

	private static Node3D Refinery(Color paint)
	{
		// Furnace chimney on top with a glowing mouth.
		var root = new Node3D();
		root.AddChild(new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.6f, Height = 0.9f, Material = DarkMetal() },
			Position = new Vector3(0, H + 0.45f, 0),
		});
		root.AddChild(new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 0.38f, BottomRadius = 0.38f, Height = 0.05f, Material = Glow(paint, 4f) },
			Position = new Vector3(0, H + 0.91f, 0),
		});
		return root;
	}

	private static Node3D Fabricator(Color paint)
	{
		// Output aperture on the front (-Z) face: a glowing frame with two print-head rails.
		var root = new Node3D();
		var glow = Glow(paint, 3f);
		var horizontal = new BoxMesh { Size = new Vector3(2.1f, 0.12f, 0.06f), Material = glow };
		var vertical = new BoxMesh { Size = new Vector3(0.12f, 2.1f, 0.06f), Material = glow };
		float z = -H - 0.03f;
		root.AddChild(new MeshInstance3D { Mesh = horizontal, Position = new Vector3(0, 1.0f, z) });
		root.AddChild(new MeshInstance3D { Mesh = horizontal, Position = new Vector3(0, -1.0f, z) });
		root.AddChild(new MeshInstance3D { Mesh = vertical, Position = new Vector3(1.0f, 0, z) });
		root.AddChild(new MeshInstance3D { Mesh = vertical, Position = new Vector3(-1.0f, 0, z) });
		var rail = new BoxMesh { Size = new Vector3(1.7f, 0.05f, 0.05f), Material = Glow(paint, 1.5f) };
		root.AddChild(new MeshInstance3D { Mesh = rail, Position = new Vector3(0, 0.35f, z) });
		root.AddChild(new MeshInstance3D { Mesh = rail, Position = new Vector3(0, -0.35f, z) });
		return root;
	}
}
