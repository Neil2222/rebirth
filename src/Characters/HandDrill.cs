using Driftworks.Items;
using Driftworks.World;
using Godot;

namespace Driftworks.Characters;

/// <summary>Hold the primary action to bore into asteroids; mined ore goes into the inventory.</summary>
public partial class HandDrill : Node3D
{
	[Export] public float Reach = 4.5f;
	[Export] public float BiteRadius = 1.1f;
	[Export] public float BiteInterval = 0.12f;   // seconds between bites

	public Camera3D Camera { get; set; } = null!;
	public CollisionObject3D Body { get; set; } = null!;
	public Inventory Inventory { get; set; } = null!;

	/// <summary>Equipped in the player's hand.</summary>
	public bool Equipped { get; set; }
	public bool Drilling { get; private set; }
	/// <summary>True while drilling is blocked because the inventory has no room left.</summary>
	public bool InventoryFull { get; private set; }

	private float _cooldown;
	private Node3D _model = null!;
	private CpuParticles3D _dust = null!;

	public override void _Ready()
	{
		var steel = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.72f, 0.75f), Metallic = 0.9f, Roughness = 0.3f };
		_model = new Node3D { Position = Camera.Position + new Vector3(0.22f, -0.2f, -0.65f) };
		// Cone tip points along -Z (forward).
		_model.AddChild(new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 0.004f, BottomRadius = 0.035f, Height = 0.3f, Material = steel },
			Basis = new Basis(Vector3.Right, -Mathf.Pi / 2f),
		});
		AddChild(_model);

		_dust = new CpuParticles3D
		{
			TopLevel = true,
			Emitting = false,
			Amount = 40,
			Lifetime = 0.8,
			Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 6, Rings = 3 },
			Spread = 70f,
			Gravity = Vector3.Zero,
			InitialVelocityMin = 1f,
			InitialVelocityMax = 3f,
			Color = new Color(0.55f, 0.5f, 0.45f),
		};
		_dust.Mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f });
		AddChild(_dust);
	}

	public override void _PhysicsProcess(double delta)
	{
		_model.Visible = Equipped;
		InventoryFull = Inventory.FreeSpace <= 0f;
		Drilling = Equipped && !InventoryFull && Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed("primary_action");
		if (Drilling)
			_model.RotateObjectLocal(Vector3.Forward, 25f * (float)delta);

		_cooldown -= (float)delta;
		if (!Drilling || _cooldown > 0f)
		{
			if (!Drilling)
				_dust.Emitting = false;
			return;
		}
		_cooldown = BiteInterval;

		Vector3 from = Camera.GlobalPosition;
		Vector3 forward = -Camera.GlobalBasis.Z;
		var ray = PhysicsRayQueryParameters3D.Create(from, from + forward * Reach, exclude: [Body.GetRid()]);
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
		if (hit.Count == 0 || hit["collider"].AsGodotObject() is not IMinable terrain)
		{
			_dust.Emitting = false;
			return;
		}

		Vector3 point = hit["position"].AsVector3();
		float[] mined = terrain.Carve(point + forward * 0.4f, BiteRadius);
		for (int m = 0; m < mined.Length; m++)
		{
			var material = VoxelMaterials.All[m];
			Inventory.Add(material.OreItemId, mined[m] * material.YieldPerCubicMetre);
		}

		_dust.GlobalPosition = point;
		_dust.Direction = hit["normal"].AsVector3();
		_dust.Emitting = true;
	}
}
