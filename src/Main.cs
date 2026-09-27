using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Characters;
using Rebirth.Core;
using Rebirth.Forge;
using Rebirth.Nexus;
using Rebirth.Persistence;
using Rebirth.UI;
using Rebirth.World;

namespace Rebirth;

/// <summary>
/// Builds the world (sky, sun, asteroids, small planets, props), then either restores a save or starts
/// a new game (tutorial or skipped intro). Owns the overlays (Forge, fabricator, Nexus, menus, tutorial),
/// printing designs into the world, and saving/loading.
/// </summary>
public partial class Main : Node3D
{
	private const double AutosaveSeconds = 120.0;
	private const double ConfirmSeconds = 3.0;

	/// <summary>First scene load of this process: continue from the latest save if there is one.</summary>
	private static bool _booted;

	public Player Player { get; private set; } = null!;
	public ForgeScreen Forge { get; private set; } = null!;
	public FabricatorPanel Fabricator { get; private set; } = null!;
	public Colony Colony { get; private set; } = null!;
	public NexusScreen Nexus { get; private set; } = null!;
	public GameMenu Menu { get; private set; } = null!;
	public Tutorial Tutorial { get; private set; } = null!;

	private Hud _hud = null!;
	private ProgressSave _progress = new();
	private double _nextAutosave;
	private double _newWorldConfirmUntil;

	public override void _Ready()
	{
		BuildEnvironment();
		AddAsteroid(new Vector3(0, -20, -120), 35f, 1);
		AddAsteroid(new Vector3(150, 40, -300), 55f, 2);
		AddAsteroid(new Vector3(-90, 30, -60), 14f, 3);
		AddAsteroid(new Vector3(-22, -4, -26), 8f, 4);
		// The rock Home sits on: where you learn to drill.
		var homeRock = new VoxelAsteroid { Name = "Home Rock", Position = new Vector3(-12, -30, 0), Radius = 16f, Seed = 11 };
		AddChild(homeRock);
		// Small walkable planets, each with its own sleepy look. Their gravity reaches three radii,
		// which stays clear of the spawn.
		AddPlanet("Dune", new Vector3(60, -140, -220), 55f, 7, VoxelMaterials.Dune, new Color(1f, 0.8f, 0.62f));
		AddPlanet("Frost", new Vector3(-260, 30, -120), 40f, 8, VoxelMaterials.Frost, new Color(0.72f, 0.86f, 1f));
		AddPlanet("Moss", new Vector3(220, 60, 120), 50f, 9, VoxelMaterials.Moss, new Color(0.78f, 0.95f, 0.72f));
		BuildCrates(new Vector3(0, 0, -15));

		Player = new Player { Name = "Player" };
		AddChild(Player);
		Player.BuildTool.GridParent = this;
		_hud = new Hud { Player = Player };
		AddChild(_hud);

		Forge = new ForgeScreen { Name = "Forge", Printer = Print, BodySetter = Player.SetBody };
		AddChild(Forge);
		Forge.Closed += OnOverlayClosed;
		Fabricator = new FabricatorPanel { Name = "FabricatorPanel" };
		AddChild(Fabricator);
		Fabricator.Closed += OnOverlayClosed;
		Player.FabricatorRequested += (grid, cell) =>
		{
			GameState.WorldInputBlocked = true;
			_hud.Visible = false;
			Fabricator.Open(grid, cell, Player);
		};

		Colony = new Colony { Name = "Colony", World = this, PilotedGrid = () => Player.PilotedGrid };
		AddChild(Colony);
		Colony.News += Player.ShowMessage;
		Nexus = new NexusScreen { Name = "Nexus", Colony = Colony };
		AddChild(Nexus);
		Nexus.Closed += OnOverlayClosed;
		Tutorial = new Tutorial { Name = "Tutorial", Player = Player, Colony = Colony };
		AddChild(Tutorial);
		Tutorial.NexusUnlocked += () => _progress.NexusUnlocked = true;
		// Last, so Esc reaches the menu before anything else in the world.
		Menu = new GameMenu { Name = "Menu" };
		AddChild(Menu);
		Menu.Closed += OnOverlayClosed;

		var save = SaveSystem.PendingLoad;
		SaveSystem.PendingLoad = null;
		bool firstBoot = !_booted;
		if (firstBoot && SaveSystem.LatestSlot() is { } latest)
			save = SaveSystem.Read(latest);
		_booted = true;

		if (save is not null)
		{
			SaveSystem.Apply(save, this, Player);
			Colony.Restore(save.Colony);
			_progress = save.Progress;
			if (save.ForgeDesign is not null)
				Forge.LoadDesign(save.ForgeDesign);
			Player.ShowMessage($"Welcome back — world from {save.SavedAt:g}");
		}
		else
			StartNewGame(homeRock, GameState.NextStart);
		Tutorial.Begin(_progress.TutorialStep);
		_nextAutosave = Now + AutosaveSeconds;

		if (firstBoot)
			OpenTitle(save is not null);
	}

	/// <summary>
	/// A fresh world: Home on its rock and the starter ship. The tutorial then has you add the drill and
	/// print the first bots; skipping the intro starts with exactly that done.
	/// </summary>
	private void StartNewGame(VoxelAsteroid homeRock, StartMode mode)
	{
		SpawnBlueprint(Presets.StarterHauler(), new Transform3D(Basis.Identity, new Vector3(14, 0, -8)), isStatic: false, charge: 1f);
		var outpost = Presets.Outpost();
		var home = SpawnBlueprint(outpost, Colony.PlaceOnSurface(homeRock, Vector3.Up, outpost, clearance: 2f), isStatic: true, charge: 1f);
		home.Label = Colony.HomeLabel;

		// Start in front of Home, looking at it.
		Vector3 eye = home.GlobalTransform * new Vector3(9f, 7f, 22f);
		Player.GlobalTransform = new Transform3D(Basis.LookingAt(home.GlobalPosition - eye, Vector3.Up), eye);
		Player.ResetPhysicsInterpolation();

		if (mode == StartMode.Tutorial)
		{
			_progress = new ProgressSave { TutorialStep = 0, NexusUnlocked = false };
			return;
		}
		// The tutorial's outcome: tube and drill in place, two bots, and the leftover ingots in storage.
		home.TryAdd(Tutorial.TubeCell, BlockCatalog.Tube, Basis.Identity);
		home.TryAdd(Tutorial.DrillCell, BlockCatalog.AutoDrill, Tutorial.DrillDown);
		foreach (var (item, amount) in Player.Inventory.Items.ToArray())
			Player.Inventory.TransferTo(home.Inventory, item, amount);
		var bot = Presets.WorkerBot();
		for (int i = 0; i < Tutorial.BotsToPrint; i++)
			SpawnBlueprint(bot, new Transform3D(Basis.Identity, home.GlobalTransform * new Vector3(-6f + i * 4f, 8f, -6f)), isStatic: false, charge: 1f);
		_progress = new ProgressSave { TutorialStep = -1, NexusUnlocked = true };
		Player.ShowMessage("Home, a drill and two bots are ready. Press N for the Nexus");
	}

	// ---------------------------------------------------------------- menus

	private void OpenTitle(bool hasSave)
	{
		_hud.Visible = false;
		Menu.Open("REBIRTH", "The Curator boxed the stars. You carry what is left of us.",
		[
			new GameMenu.Entry(hasSave ? "Continue" : "Start", () => { }),
			new GameMenu.Entry("New game - with tutorial", () => NewGame(StartMode.Tutorial), Confirm: hasSave),
			new GameMenu.Entry("New game - skip the intro", () => NewGame(StartMode.SkipIntro), Confirm: hasSave),
			new GameMenu.Entry("Quit", () => GetTree().Quit()),
		]);
	}

	private void OpenPauseMenu()
	{
		_hud.Visible = false;
		Menu.Open("PAUSED", "The world keeps turning while you are here.",
		[
			new GameMenu.Entry("Resume", () => { }),
			new GameMenu.Entry("Nexus overview   [N]", () => CallDeferred(MethodName.OpenNexus), Enabled: () => _progress.NexusUnlocked),
			new GameMenu.Entry("Forge   [B]", () => CallDeferred(MethodName.OpenForge)),
			new GameMenu.Entry("Quicksave   [F5]", () => SaveTo(SaveSystem.QuickSlot, "Quicksaved")),
			new GameMenu.Entry("Quickload   [F9]", () => LoadFrom(SaveSystem.QuickSlot), Enabled: () => SaveSystem.Exists(SaveSystem.QuickSlot)),
			new GameMenu.Entry("New game - with tutorial", () => NewGame(StartMode.Tutorial), Confirm: true),
			new GameMenu.Entry("New game - skip the intro", () => NewGame(StartMode.SkipIntro), Confirm: true),
			new GameMenu.Entry("Save and quit", () =>
			{
				SaveTo(SaveSystem.AutoSlot, null);
				GetTree().Quit();
			}),
		]);
	}

	private void NewGame(StartMode mode)
	{
		GameState.NextStart = mode;
		SaveSystem.PendingLoad = null;
		ReloadWorld();
	}

	private void OpenNexus()
	{
		if (GameState.WorldInputBlocked || Player.PilotedGrid is not null)
			return;
		if (!_progress.NexusUnlocked)
		{
			Player.ShowMessage("The Nexus comes online once your first bots are printed");
			return;
		}
		GameState.WorldInputBlocked = true;
		_hud.Visible = false;
		Tutorial.NexusOpened = true;
		Nexus.Open();
	}

	private static double Now => Time.GetTicksMsec() / 1000.0;

	private void AddPlanet(string name, Vector3 position, float radius, int seed, byte crust, Color haze) =>
		AddChild(new MiniPlanet { Name = name, Position = position, Radius = radius, Seed = seed, Crust = crust, HazeTint = haze });

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
		else if (GameState.WorldInputBlocked)
			return;
		else if (e.IsActionPressed("release_mouse"))
			OpenPauseMenu();
		else if (e.IsActionPressed("open_nexus"))
			OpenNexus();
		else if (e.IsActionPressed("open_forge") && Player.PilotedGrid is null)
			OpenForge();
		else
			return;
		GetViewport().SetInputAsHandled();
	}

	private void OpenForge()
	{
		if (GameState.WorldInputBlocked || Player.PilotedGrid is not null)
			return;
		GameState.WorldInputBlocked = true;
		_hud.Visible = false;
		Forge.Open();
	}

	private void OnOverlayClosed()
	{
		GameState.WorldInputBlocked = false;
		_hud.Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	/// <summary>
	/// The Forge's instant print, for creative mode: materialises a design in front of the player,
	/// free and fully charged. In survival, designs are printed at a fabricator.
	/// </summary>
	private string Print(Blueprint blueprint)
	{
		if (!Player.Creative)
			return "In survival, print designs at a Fabricator (toolbar page 2). F2 = creative";
		if (blueprint.Kind == DesignKind.Body)
			return "Bodies are worn, not printed: use \"Use as my body\"";
		if (FindPrintSpot(blueprint) is not { } spot)
			return "No free space in front of you to print into";
		var grid = SpawnBlueprint(blueprint, spot, isStatic: blueprint.Kind == DesignKind.Station, charge: 1f);
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
		_progress.TutorialStep = Tutorial.StepIndex;
		SaveSystem.Write(SaveSystem.Capture(this, Player, Forge.CurrentBlueprint(), Colony.ToSave(), _progress), slot);
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
		var skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/retro_sky.gdshader") };
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky { SkyMaterial = skyMaterial },
			// The colourful sky doubles as a soft fill light, so shadows are tinted rather than black.
			AmbientLightSource = Godot.Environment.AmbientSource.Sky,
			AmbientLightEnergy = 1.5f,
			ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
			TonemapExposure = 1.05f,
			// Soft contact shadows where things touch: the cosy, tactile look.
			SsaoEnabled = true,
			SsaoRadius = 1.6f,
			SsaoIntensity = 1.6f,
			// Only real lights (lamps, flames) bloom, and gently.
			GlowEnabled = true,
			GlowIntensity = 0.45f,
			GlowHdrThreshold = 1.2f,
			GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
			AdjustmentEnabled = true,
			AdjustmentSaturation = 1.08f,
		};
		AddChild(new WorldEnvironment { Environment = env });

		var sun = new DirectionalLight3D
		{
			LightEnergy = 1.5f,
			LightColor = new Color(1f, 0.9f, 0.76f),
			ShadowEnabled = true,
			ShadowBlur = 2.5f,
			DirectionalShadowMaxDistance = 400f,
		};
		AddChild(sun);
		sun.LookAt(new Vector3(-1, -0.4f, -0.6f), Vector3.Up);
		// The light shines along its -Z, so +Z points back at the sun.
		Sun.Direction = sun.GlobalBasis.Z;

		// A soft, cool fill from the other side so backlit sides stay friendly instead of going dark.
		var fill = new DirectionalLight3D { LightEnergy = 0.45f, LightColor = new Color(0.72f, 0.82f, 1f), LightSpecular = 0.1f };
		AddChild(fill);
		fill.LookAt(new Vector3(1, 0.3f, 0.6f), Vector3.Up);
	}

	private void BuildCrates(Vector3 origin)
	{
		var mesh = new BoxMesh
		{
			Size = Vector3.One,
			Material = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.6f, 0.4f), Roughness = 0.9f },   // cardboard
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
