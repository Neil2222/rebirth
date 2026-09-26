using Godot;
using Driftworks.Characters;
using Driftworks.UI;

namespace Driftworks;

/// <summary>Builds the test sandbox: sky, sun, a few asteroids, loose crates, and the player.</summary>
public partial class Main : Node3D
{
	public Player Player { get; private set; } = null!;

	public override void _Ready()
	{
		BuildEnvironment();
		BuildAsteroid(new Vector3(0, -20, -120), 40f, 1);
		BuildAsteroid(new Vector3(150, 40, -300), 70f, 2);
		BuildAsteroid(new Vector3(-90, 30, -60), 12f, 3);
		BuildCrates(new Vector3(0, 0, -15));

		Player = new Player { Name = "Player" };
		AddChild(Player);
		AddChild(new Hud { Player = Player });
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
	}

	private void BuildAsteroid(Vector3 position, float radius, int seed)
	{
		var mesh = AsteroidMesh(radius, seed);
		var body = new StaticBody3D { Position = position };
		body.AddChild(new MeshInstance3D { Mesh = mesh });
		body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
		AddChild(body);
	}

	/// <summary>Noise-displaced sphere with flat shading for a low-poly rock look.</summary>
	private static ArrayMesh AsteroidMesh(float radius, int seed)
	{
		var sphere = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 48, Rings = 24 };
		var arrays = sphere.GetMeshArrays();
		var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();

		var noise = new FastNoiseLite
		{
			Seed = seed,
			NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
			Frequency = 1.2f / radius,
			FractalOctaves = 4,
		};
		for (int i = 0; i < vertices.Length; i++)
			vertices[i] = vertices[i].Normalized() * radius * (1f + 0.35f * noise.GetNoise3Dv(vertices[i]));

		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		foreach (int index in indices)
			st.AddVertex(vertices[index]);
		st.GenerateNormals();
		st.SetMaterial(new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.38f, 0.34f), Roughness = 0.95f });
		return st.Commit();
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
