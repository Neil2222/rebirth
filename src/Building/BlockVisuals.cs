using Godot;

namespace Driftworks.Building;

/// <summary>
/// Extra geometry drawn on top of a block's cube so functional blocks are recognisable and
/// their facing is visible. Built in block-local space (cell center at origin, -Z = forward).
/// </summary>
public static class BlockVisuals
{
	public const string FlameName = "Flame";
	private const float H = BlockGrid.CellSize * 0.5f;

	public static Node3D? CreateDecoration(BlockDefinition block) => block.Kind switch
	{
		BlockKind.Cockpit => Cockpit(),
		BlockKind.Thruster => Thruster(),
		BlockKind.Gyroscope => Gyroscope(),
		_ => null,
	};

	/// <summary>One-shot burst of tumbling fragments where a block was destroyed.</summary>
	public static void SpawnDebris(Node parent, Vector3 position, Color color)
	{
		var particles = new CpuParticles3D
		{
			Mesh = new BoxMesh { Size = Vector3.One * 0.3f, Material = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.8f } },
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

	private static Node3D Cockpit()
	{
		var root = new Node3D();
		var glass = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.35f, 0.7f, 1f),
			Emission = new Color(0.2f, 0.5f, 0.9f),
			EmissionEnabled = true,
			EmissionEnergyMultiplier = 0.6f,
			Metallic = 0.8f,
			Roughness = 0.1f,
		};
		// One-sided quad facing outwards (-Z), so the pilot looks straight through it from inside.
		root.AddChild(new MeshInstance3D
		{
			Mesh = new QuadMesh { Size = new Vector2(2.0f, 1.1f), Material = glass },
			Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(0, 0.25f, -H - 0.01f)),
		});
		return root;
	}

	private static Node3D Thruster()
	{
		var root = new Node3D();
		var metal = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.16f, 0.18f), Metallic = 0.8f, Roughness = 0.4f };
		// Cylinders are Y-aligned; tip them onto +Z (the exhaust side).
		var toZ = new Basis(Vector3.Right, Mathf.Pi / 2f);
		root.AddChild(new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 0.95f, BottomRadius = 0.75f, Height = 0.35f, Material = metal },
			Transform = new Transform3D(toZ, new Vector3(0, 0, H + 0.175f)),
		});

		var flameMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			AlbedoColor = new Color(0.45f, 0.75f, 1f, 0.8f),
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

	private static Node3D Gyroscope()
	{
		var root = new Node3D();
		var brass = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.75f, 0.3f), Metallic = 0.9f, Roughness = 0.3f };
		root.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 1.2f, OuterRadius = 1.36f, Material = brass } });
		root.AddChild(new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = 1.2f, OuterRadius = 1.36f, Material = brass },
			Basis = new Basis(Vector3.Right, Mathf.Pi / 2f),
		});
		return root;
	}
}
