using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Characters;
using Rebirth.Core;
using Rebirth.Forge;
using Rebirth.Items;
using Rebirth.Persistence;
using Rebirth.UI;
using Rebirth.World;

namespace Rebirth;

/// <summary>
/// Builds the world (sky, sun, asteroids, planet, props), then either restores a save or spawns
/// the starter grids. Also owns the Forge, printing designs into the world, and saving/loading.
/// </summary>
public partial class Main : Node3D
{
	private const double AutosaveSeconds = 120.0;
	private const double ConfirmSeconds = 3.0;

	/// <summary>First scene load of this process: continue from the latest save if there is one.</summary>
	private static bool _booted;

	public Player Player { get; private set; } = null!;
	public ForgeScreen Forge { get; private set; } = null!;

	private Hud _hud = null!;
	private double _nextAutosave;
	private double _newWorldConfirmUntil;

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

		Player = new Player { Name = "Player" };
		AddChild(Player);
		Player.BuildTool.GridParent = this;
		_hud = new Hud { Player = Player };
		AddChild(_hud);

		Forge = new ForgeScreen { Name = "Forge", Printer = Print, BodySetter = Player.SetBody };
		AddChild(Forge);
		Forge.Closed += OnForgeClosed;

		var save = SaveSystem.PendingLoad;
		SaveSystem.PendingLoad = null;
		if (!_booted && SaveSystem.LatestSlot() is { } latest)
			save = SaveSystem.Read(latest);
		_booted = true;

		if (save is not null)
		{
			SaveSystem.Apply(save, this, Player);
			if (save.ForgeDesign is not null)
				Forge.LoadDesign(save.ForgeDesign);
			Player.ShowMessage($"Welcome back — world from {save.SavedAt:g}");
		}
		else
		{
			SpawnBlueprint(Presets.StarterHauler(), new Transform3D(Basis.Identity, new Vector3(14, 0, -8)), isStatic: false, charge: 1f);
			SpawnBlueprint(Presets.Outpost(), new Transform3D(Basis.Identity, new Vector3(-12, -7, -2)), isStatic: true, charge: 1f);
		}
		_nextAutosave = Now + AutosaveSeconds;
	}

	private static double Now => Time.GetTicksMsec() / 1000.0;

	private void AddAsteroid(Vector3 position, float radius, int seed) =>
		AddChild(new VoxelAsteroid { Name = $"Asteroid{seed}", Position = position, Radius = radius, Seed = seed });

	private BlockGrid SpawnBlueprint(Blueprint blueprint, Transform3D transform, bool isStatic, float charge)
	{
		var grid = BlockGrid.Create(this, transform, isStatic);
		blueprint.BuildInto(grid, charge);
		return grid;
	}

	// ---------------------------------------------------------------- input, Forge, printing

	public override void _UnhandledInput(InputEvent e)
	{
		if (e.IsActionPressed("quick_save"))
			SaveTo(SaveSystem.QuickSlot, "Quicksaved");
		else if (e.IsActionPressed("quick_load"))
			LoadFrom(SaveSystem.QuickSlot);
		else if (e.IsActionPressed("new_world"))
			RequestNewWorld();
		else if (e.IsActionPressed("open_forge") && !Forge.IsOpen && !GameState.WorldInputBlocked && Player.PilotedGrid is null)
			OpenForge();
	}

	private void OpenForge()
	{
		GameState.WorldInputBlocked = true;
		_hud.Visible = false;
		Forge.Open();
		GetViewport().SetInputAsHandled();
	}

	private void OnForgeClosed()
	{
		GameState.WorldInputBlocked = false;
		_hud.Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	/// <summary>
	/// Materialises a design in front of the player as a ship. Survival pays the full ingot cost
	/// up front; creative prints for free with charged batteries.
	/// </summary>
	private string Print(Blueprint blueprint)
	{
		var cost = blueprint.TotalCost();
		if (!Player.Creative)
		{
			var missing = cost.Where(kv => Player.Inventory.Get(kv.Key) < kv.Value)
				.Select(kv => $"{kv.Value - Player.Inventory.Get(kv.Key):0} {ItemCatalog.DisplayName(kv.Key)}")
				.ToList();
			if (missing.Count > 0)
				return "Not enough materials. Missing: " + string.Join(", ", missing) + "  (F2 = creative)";
		}

		if (FindPrintSpot(blueprint) is not { } spot)
			return "No free space in front of you to print into";
		if (!Player.Creative)
			foreach (var (item, amount) in cost)
				Player.Inventory.TryRemove(item, amount);
		var grid = SpawnBlueprint(blueprint, spot, isStatic: false, charge: Player.Creative ? 1f : 0.25f);
		grid.LinearVelocity = Player.LinearVelocity;
		Player.ShowMessage($"Printed \"{blueprint.Name}\"");
		return $"Printed \"{blueprint.Name}\" in front of you";
	}

	/// <summary>A clear spot ahead of the camera big enough for the design, trying further out if needed.</summary>
	private Transform3D? FindPrintSpot(Blueprint blueprint)
	{
		float radius = BlockGrid.CellSize;
		foreach (var block in blueprint.Blocks)
			radius = Mathf.Max(radius, BlockGrid.CellCenter(block.CellVector()).Length() + BlockGrid.CellSize);

		var camera = Player.Camera;
		Vector3 forward = -camera.GlobalBasis.Z;
		var probe = new PhysicsShapeQueryParameters3D { Shape = new SphereShape3D { Radius = radius } };
		var space = GetWorld3D().DirectSpaceState;
		for (int attempt = 0; attempt < 6; attempt++)
		{
			var spot = new Transform3D(camera.GlobalBasis.Orthonormalized(), camera.GlobalPosition + forward * (radius + 6f + attempt * 12f));
			probe.Transform = spot;
			if (space.IntersectShape(probe, 1).Count == 0)
				return spot;
		}
		return null;
	}

	// ---------------------------------------------------------------- saving

	public override void _Process(double delta)
	{
		if (Now >= _nextAutosave)
			SaveTo(SaveSystem.AutoSlot, "Autosaved");
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
			SaveTo(SaveSystem.AutoSlot, null);
	}

	private void SaveTo(string slot, string? message)
	{
		_nextAutosave = Now + AutosaveSeconds;
		SaveSystem.Write(SaveSystem.Capture(this, Player, Forge.Design), slot);
		if (message is not null)
			Player.ShowMessage(message);
	}

	private void LoadFrom(string slot)
	{
		if (SaveSystem.Read(slot) is not { } save)
		{
			Player.ShowMessage("No quicksave yet (F5 to save)");
			return;
		}
		SaveSystem.PendingLoad = save;
		ReloadWorld();
	}

	/// <summary>Needs a second press within a few seconds, so a stray key can't wipe the world.</summary>
	private void RequestNewWorld()
	{
		if (Now > _newWorldConfirmUntil)
		{
			_newWorldConfirmUntil = Now + ConfirmSeconds;
			Player.ShowMessage("Press F8 again to start a new world (saves stay on disk)");
			return;
		}
		SaveSystem.PendingLoad = null;
		ReloadWorld();
	}

	private void ReloadWorld()
	{
		GameState.WorldInputBlocked = false;
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}

	private void BuildEnvironment()
	{
		var skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/starfield.gdshader") };
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky { SkyMaterial = skyMaterial },
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.05f, 0.06f, 0.10f),
			TonemapMode = Godot.Environment.ToneMapper.Aces,
			// Neon lives on bloom: emissive edges bleed into a soft halo.
			GlowEnabled = true,
			GlowIntensity = 0.9f,
			GlowStrength = 1.0f,
			GlowBloom = 0f,
			GlowHdrThreshold = 1.0f,
			GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,
		};
		env.SetGlowLevel(0, 1f);
		env.SetGlowLevel(2, 1f);
		env.SetGlowLevel(4, 0.6f);
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
			Material = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.06f, 0.05f, 0.04f),
				Metallic = 0.6f,
				Roughness = 0.4f,
				EmissionEnabled = true,
				Emission = Neon.Orange,
				EmissionEnergyMultiplier = 0.35f,
			},
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
