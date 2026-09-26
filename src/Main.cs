using Driftworks.Building;
using Driftworks.Characters;
using Driftworks.UI;
using Driftworks.World;
using Godot;

namespace Driftworks;

/// <summary>Builds the test sandbox: sky, sun, asteroids, loose crates, a starter ship, and the player.</summary>
public partial class Main : Node3D
{
	public Player Player { get; private set; } = null!;

	public override void _Ready()
	{
		BuildEnvironment();
		AddAsteroid(new Vector3(0, -20, -120), 35f, 1);
		AddAsteroid(new Vector3(150, 40, -300), 55f, 2);
		AddAsteroid(new Vector3(-90, 30, -60), 14f, 3);
		AddAsteroid(new Vector3(-22, -4, -26), 8f, 4);
		// Close enough to fly to (~1 km of surface), far enough that its gravity (2.5 radii) misses the spawn.
		AddChild(new Planet { Name = "Planet", Position = new Vector3(0, -1100, -1300), Radius = 600f, Seed = 7 });
		BuildCrates(new Vector3(0, 0, -15));
		BuildStarterShip(new Transform3D(Basis.Identity, new Vector3(14, 0, -8)));
		BuildStarterStation(new Transform3D(Basis.Identity, new Vector3(-12, -7, -2)));

		Player = new Player { Name = "Player" };
		AddChild(Player);
		Player.BuildTool.GridParent = this;
		AddChild(new Hud { Player = Player });
	}

	private void AddAsteroid(Vector3 position, float radius, int seed) =>
		AddChild(new VoxelAsteroid { Position = position, Radius = radius, Seed = seed });

	/// <summary>Small ship with thrust on all six axes, a gyroscope, and a cockpit facing -Z.</summary>
	private void BuildStarterShip(Transform3D transform)
	{
		var ship = BlockGrid.Create(this, transform, isStatic: false);
		Basis identity = Basis.Identity;
		ship.TryAdd(new Vector3I(0, 0, 0), BlockCatalog.Cockpit, identity);
		ship.TryAdd(new Vector3I(0, 0, 1), BlockCatalog.Gyroscope, identity);
		ship.TryAdd(new Vector3I(0, 0, 2), BlockCatalog.LightArmor, identity);
		ship.TryAdd(new Vector3I(-1, 0, 1), BlockCatalog.LightArmor, identity);
		ship.TryAdd(new Vector3I(1, 0, 1), BlockCatalog.LightArmor, identity);
		// A thruster pushes the grid along its local -Z; these orientations aim that at each axis.
		ship.TryAdd(new Vector3I(0, 0, 3), BlockCatalog.Thruster, identity);                                  // forward
		ship.TryAdd(new Vector3I(-1, 0, 0), BlockCatalog.Thruster, new Basis(Vector3.Up, Mathf.Pi));          // backward
		ship.TryAdd(new Vector3I(1, 0, 0), BlockCatalog.Thruster, new Basis(Vector3.Up, Mathf.Pi));           // backward
		ship.TryAdd(new Vector3I(-2, 0, 1), BlockCatalog.Thruster, new Basis(Vector3.Up, -Mathf.Pi / 2f));    // right
		ship.TryAdd(new Vector3I(2, 0, 1), BlockCatalog.Thruster, new Basis(Vector3.Up, Mathf.Pi / 2f));      // left
		ship.TryAdd(new Vector3I(0, 1, 1), BlockCatalog.Thruster, new Basis(Vector3.Right, -Mathf.Pi / 2f));  // down
		ship.TryAdd(new Vector3I(0, -1, 1), BlockCatalog.Thruster, new Basis(Vector3.Right, Mathf.Pi / 2f));  // up
		ship.TryAdd(new Vector3I(0, 1, 2), BlockCatalog.Battery, identity, charge: 1f);
	}

	/// <summary>Static base with a refinery, cargo, a battery, and solar panels on the sunward (+X) side.</summary>
	private void BuildStarterStation(Transform3D transform)
	{
		var station = BlockGrid.Create(this, transform, isStatic: true);
		Basis identity = Basis.Identity;
		for (int x = -1; x <= 1; x++)
			for (int z = -1; z <= 1; z++)
				station.TryAdd(new Vector3I(x, 0, z), BlockCatalog.LightArmor, identity);
		station.TryAdd(new Vector3I(-1, 1, 0), BlockCatalog.Battery, identity, charge: 1f);
		station.TryAdd(new Vector3I(0, 1, 0), BlockCatalog.Refinery, identity);
		station.TryAdd(new Vector3I(1, 1, 0), BlockCatalog.CargoContainer, identity);
		// Panels collect on their local +Y; turn that towards +X.
		var sunward = new Basis(Vector3.Back, -Mathf.Pi / 2f);
		for (int z = -1; z <= 1; z++)
			station.TryAdd(new Vector3I(2, 0, z), BlockCatalog.SolarPanel, sunward);
	}

	private void BuildEnvironment()
	{
		var skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/starfield.gdshader") };
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky { SkyMaterial = skyMaterial },
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.08f, 0.09f, 0.12f),
			TonemapMode = Godot.Environment.ToneMapper.Aces,
			GlowEnabled = true,
		};
		AddChild(new WorldEnvironment { Environment = env });

		var sun = new DirectionalLight3D
		{
			LightEnergy = 1.4f,
			LightColor = new Color(1f, 0.96f, 0.9f),
			ShadowEnabled = true,
			DirectionalShadowMaxDistance = 400f,
		};
		AddChild(sun);
		sun.LookAt(new Vector3(-1, -0.4f, -0.6f), Vector3.Up);
		// The light shines along its -Z, so +Z points back at the sun.
		Sun.Direction = sun.GlobalBasis.Z;
	}

	private void BuildCrates(Vector3 origin)
	{
		var mesh = new BoxMesh
		{
			Size = Vector3.One,
			Material = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.55f, 0.15f), Roughness = 0.6f, Metallic = 0.3f },
		};
		var shape = new BoxShape3D { Size = Vector3.One };
		var rng = new RandomNumberGenerator { Seed = 42 };

		for (int i = 0; i < 12; i++)
		{
			var crate = new RigidBody3D
			{
				Mass = 50f,
				Position = origin + new Vector3(rng.RandfRange(-6, 6), rng.RandfRange(-3, 3), rng.RandfRange(-4, 4)),
				Rotation = new Vector3(rng.Randf(), rng.Randf(), rng.Randf()) * Mathf.Tau,
				AngularVelocity = new Vector3(rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(-0.3f, 0.3f)),
			};
			crate.AddChild(new MeshInstance3D { Mesh = mesh });
			crate.AddChild(new CollisionShape3D { Shape = shape });
			AddChild(crate);
		}
	}
}
