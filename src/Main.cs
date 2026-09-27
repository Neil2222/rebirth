using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Characters;
using Rebirth.Core;
using Rebirth.Forge;
using Rebirth.Life;
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

	/// <summary>First scene load of this process: continue from the latest save if there is one.</summary>
	private static bool _booted;

	public Player Player { get; private set; } = null!;
	public ForgeScreen Forge { get; private set; } = null!;
	public FabricatorPanel Fabricator { get; private set; } = null!;
	public Colony Colony { get; private set; } = null!;
	public NexusScreen Nexus { get; private set; } = null!;
	public GameMenu Menu { get; private set; } = null!;
	public Tutorial Tutorial { get; private set; } = null!;
	public SlotPanel Slots { get; private set; } = null!;
	public InventoryPanel Inventory { get; private set; } = null!;
	public People People { get; private set; } = null!;
	public TalkPanel Talk { get; private set; } = null!;

	private Hud _hud = null!;
	private ProgressSave _progress = new();
	private int _box = 1;
	private BoxWall _wall = null!;
	public SpiritBomb SpiritBomb { get; private set; } = null!;

	private double _nextAutosave;

	public override void _Ready()
	{
		var save = SaveSystem.PendingLoad;
		SaveSystem.PendingLoad = null;
		bool firstBoot = !_booted;
		if (firstBoot)
			save = SaveSystem.ReadWorld(Campaign.Active.Current);
		_booted = true;
		_box = save?.Box ?? GameState.NextBox;
		Campaign.Active.Current = _box;

		var homeRock = BuildBox(Campaign.Active.Reach(_box));

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
		People = new People { Name = "People", Colony = Colony };
		AddChild(People);
		Colony.PeopleResonance = () => People.ResonancePerMinute;
		Colony.SettlementsToSave = () => People.Settlements;
		Talk = new TalkPanel { Name = "Talk", People = People };
		var boxMap = new BoxMapPanel { Name = "BoxMap" };
		Nexus = new NexusScreen { Name = "Nexus", Colony = Colony, People = People, Talk = Talk, BoxMap = boxMap };
		AddChild(Nexus);
		AddChild(boxMap);
		boxMap.TravelRequested += target =>
		{
			Nexus.Close();
			Travel(target);
		};
		// After the Nexus, so a conversation opened from it gets Esc first.
		AddChild(Talk);
		Talk.Closed += () =>
		{
			if (!Nexus.IsOpen)
				OnOverlayClosed();
		};
		Player.VillageRequested += grid =>
		{
			if (People.At(grid) is not { } village)
			{
				Player.ShowMessage("No one has woken here yet: the planet needs 50% air and water and 30% soil");
				return;
			}
			GameState.WorldInputBlocked = true;
			_hud.Visible = false;
			Talk.Open(village);
		};
		Nexus.Closed += OnOverlayClosed;
		Tutorial = new Tutorial { Name = "Tutorial", Player = Player, Colony = Colony };
		AddChild(Tutorial);
		Tutorial.NexusUnlocked += () => _progress.NexusUnlocked = true;
		// Last, so Esc reaches the menu before anything else in the world.
		Menu = new GameMenu { Name = "Menu" };
		AddChild(Menu);
		Menu.Closed += OnOverlayClosed;
		Inventory = new InventoryPanel { Name = "Inventory" };
		AddChild(Inventory);
		Inventory.Closed += OnOverlayClosed;
		Slots = new SlotPanel { Name = "Slots" };
		AddChild(Slots);
		Slots.Closed += OnOverlayClosed;

		SpiritBomb = new SpiritBomb { Name = "SpiritBomb", Colony = Colony, People = People, Wall = _wall };
		AddChild(SpiritBomb);
		SpiritBomb.Breached += EnterNextBox;
		Nexus.FireRequested += FireLance;

		if (save is not null)
		{
			SaveSystem.Apply(save, this, Player);
			Colony.Restore(save.Colony);
			People.Restore(save.Colony.Settlements);
			_progress = save.Progress;
			if (save.ForgeDesign is not null)
				Forge.LoadDesign(save.ForgeDesign);
			Player.ShowMessage($"Welcome back — world from {save.SavedAt:g}");
		}
		else
			StartNewGame(homeRock, GameState.NextStart);
		if (GameState.PendingTransfer is { } transfer)
		{
			GameState.PendingTransfer = null;
			// A world you come back to may have its player standing anywhere: put them at Home.
			if (save is not null && Colony.Home is { } home)
			{
				Vector3 eye = home.GlobalTransform * new Vector3(9f, 7f, 22f);
				Player.GlobalTransform = new Transform3D(Basis.LookingAt(home.GlobalPosition - eye, Vector3.Up), eye);
				Player.LinearVelocity = Vector3.Zero;
				Player.ResetPhysicsInterpolation();
			}
			Unpack(transfer);
			// So Continue picks up here, not in the Box you left.
			SaveGame(null);
		}
		else if (save is null && !firstBoot)
			SaveGame(null);   // a new game takes up its slot straight away
		Tutorial.Begin(_progress.TutorialStep);
		_nextAutosave = Now + AutosaveSeconds;

		if (firstBoot)
			OpenTitle();
	}

	/// <summary>Colours, sun and wall of a Box: the cluster's mood.</summary>
	private sealed record BoxLook(Color Plum, Color Peach, Color Teal, Color Rose, Color SunColor, float SunEnergy, float SolarStrength, Color Wall);

	private static BoxLook LookOf(BoxKind kind) => kind switch
	{
		BoxKind.Tide => new(new(0.10f, 0.17f, 0.30f), new(0.75f, 0.95f, 0.9f), new(0.07f, 0.27f, 0.36f), new(0.45f, 0.75f, 0.85f), new(0.88f, 0.96f, 1f), 1.5f, 1f, new(0.7f, 1f, 0.9f)),
		// A faint, far star: dusky indigo, and solar panels give little.
		BoxKind.Dim => new(new(0.08f, 0.07f, 0.18f), new(0.55f, 0.45f, 0.7f), new(0.06f, 0.1f, 0.2f), new(0.4f, 0.3f, 0.55f), new(0.75f, 0.72f, 1f), 0.9f, 0.4f, new(0.7f, 0.65f, 1f)),
		BoxKind.Frost => new(new(0.16f, 0.2f, 0.34f), new(0.92f, 0.95f, 1f), new(0.2f, 0.36f, 0.5f), new(0.7f, 0.78f, 0.95f), new(0.92f, 0.96f, 1f), 1.4f, 0.9f, new(0.85f, 0.95f, 1f)),
		BoxKind.Ember => new(new(0.26f, 0.12f, 0.16f), new(1f, 0.62f, 0.38f), new(0.3f, 0.2f, 0.25f), new(0.95f, 0.5f, 0.4f), new(1f, 0.82f, 0.6f), 1.7f, 1.15f, new(1f, 0.75f, 0.6f)),
		_ => new(new(0.20f, 0.15f, 0.34f), new(1f, 0.70f, 0.48f), new(0.12f, 0.34f, 0.42f), new(0.85f, 0.45f, 0.60f), new(1f, 0.9f, 0.76f), 1.5f, 1f, new(0.86f, 0.78f, 1f)),
	};

	/// <summary>
	/// Sky, sun, the Box wall and the bodies of a Box. The first two are hand-made; later ones are
	/// generated from their seed in the character of their kind.
	/// </summary>
	private VoxelAsteroid BuildBox(BoxInfo box)
	{
		var look = LookOf(box.Kind);
		BuildEnvironment(look);
		_wall = new BoxWall { Name = "BoxWall", LineColor = look.Wall };
		AddChild(_wall);
		// The rock Home sits on: where you learn to drill, and where you land in a new Box.
		var homeRock = new VoxelAsteroid { Name = "Home Rock", Position = new Vector3(-12, -30, 0), Radius = 16f, Seed = 11 + box.Index };
		AddChild(homeRock);
		switch (box.Kind)
		{
			case BoxKind.First:
				AddAsteroid(new Vector3(0, -20, -120), 35f, 1);
				AddAsteroid(new Vector3(150, 40, -300), 55f, 2);
				AddAsteroid(new Vector3(-90, 30, -60), 14f, 3);
				AddAsteroid(new Vector3(-22, -4, -26), 8f, 4);
				// Small walkable planets, each with its own sleepy look. Their gravity reaches three radii,
				// which stays clear of the spawn.
				AddPlanet("Dune", new Vector3(60, -140, -220), 55f, 7, VoxelMaterials.Dune, new Color(1f, 0.8f, 0.62f));
				AddPlanet("Frost", new Vector3(-260, 30, -120), 40f, 8, VoxelMaterials.Frost, new Color(0.72f, 0.86f, 1f));
				AddPlanet("Moss", new Vector3(220, 60, 120), 50f, 9, VoxelMaterials.Moss, new Color(0.78f, 0.95f, 0.72f));
				BuildCrates(new Vector3(0, 0, -15));
				break;
			case BoxKind.Tide:
				AddAsteroid(new Vector3(40, 10, -110), 28f, 21);
				AddAsteroid(new Vector3(-120, -40, -80), 20f, 22);
				AddAsteroid(new Vector3(180, -60, 60), 42f, 23);
				// A water world that is half-way there already, a coral-pink reef planet and a little lantern moon.
				AddPlanet("Tide", new Vector3(-40, -170, -270), 70f, 31, VoxelMaterials.Frost, new Color(0.6f, 0.95f, 0.95f));
				AddPlanet("Coral", new Vector3(260, 40, -80), 45f, 32, VoxelMaterials.Moss, new Color(1f, 0.72f, 0.72f));
				AddPlanet("Lantern", new Vector3(-240, 90, 150), 38f, 33, VoxelMaterials.Dune, new Color(1f, 0.85f, 0.55f));
				break;
			default:
				GenerateBodies(box);
				break;
		}
		return homeRock;
	}

	private static readonly string[] Syllables = ["ve", "lo", "ma", "ki", "ru", "so", "ne", "ta", "mi", "or", "el", "pa", "zu", "ri", "an"];
	private static readonly string[] Endings = ["ra", "lin", "ssa", "do", "mir", "ven", "ta", "ro", "nel", "sh"];

	/// <summary>
	/// Planets and asteroids of a generated Box, spread around Home with room between them. Frost Boxes
	/// are icy all over; Ember Boxes are dry, with a single small ice moon; Dim Boxes have a mix.
	/// </summary>
	private void GenerateBodies(BoxInfo box)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)box.Seed };
		var taken = new List<(Vector3 Position, float Radius)> { (new Vector3(-12, -30, 0), 40f) };
		Vector3 Place(float radius, float min, float max)
		{
			for (int attempt = 0; attempt < 200; attempt++)
			{
				var dir = new Vector3(rng.Randfn(), rng.Randfn() * 0.5f, rng.Randfn()).Normalized();
				var position = dir * rng.RandfRange(min, max);
				if (taken.All(t => t.Position.DistanceTo(position) > t.Radius + radius * 3.2f + 30f))
				{
					taken.Add((position, radius * 3f));
					return position;
				}
			}
			return new Vector3(rng.RandfRange(-300, 300), rng.RandfRange(-100, 100), rng.RandfRange(-300, 300));
		}
		string NewName()
		{
			string first = Syllables[rng.RandiRange(0, Syllables.Length - 1)];
			return char.ToUpper(first[0]) + first[1..] + Syllables[rng.RandiRange(0, Syllables.Length - 1)] + Endings[rng.RandiRange(0, Endings.Length - 1)];
		}

		int planets = rng.RandiRange(3, 4);
		for (int i = 0; i < planets; i++)
		{
			float radius = rng.RandfRange(38f, 62f);
			byte crust = box.Kind switch
			{
				BoxKind.Frost => VoxelMaterials.Frost,
				BoxKind.Ember => VoxelMaterials.Dune,
				_ => i % 2 == 0 ? VoxelMaterials.Moss : VoxelMaterials.Dune,
			};
			Color haze = box.Kind switch
			{
				BoxKind.Frost => new Color(0.78f, 0.9f, 1f),
				BoxKind.Ember => new Color(1f, 0.66f, 0.5f),
				_ => new Color(0.75f, 0.7f, 1f),
			};
			string name = NewName();
			AddPlanet(name, Place(radius, 190f, 330f), radius, box.Seed + i * 7, crust, haze);
		}
		// Every Box needs ice somewhere: dry ones get a single small ice moon.
		if (box.Kind != BoxKind.Frost)
			AddPlanet(NewName(), Place(30f, 200f, 320f), 30f, box.Seed + 99, VoxelMaterials.Frost, new Color(0.75f, 0.9f, 1f));
		int asteroids = rng.RandiRange(3, 4);
		for (int i = 0; i < asteroids; i++)
		{
			float radius = rng.RandfRange(12f, 38f);
			AddAsteroid(Place(radius, 70f, 300f), radius, box.Seed + 50 + i, $"{NewName()} Rock");
		}
	}

	/// <summary>
	/// A fresh world. In the first Box: Home on its rock and the starter ship, then either the tutorial
	/// or its outcome (drill, tube, two bots). Arriving in another Box: Home with its drill, plus whatever
	/// you brought along (added after this).
	/// </summary>
	private void StartNewGame(VoxelAsteroid homeRock, StartMode mode)
	{
		if (Campaign.Active.CurrentBox.Kind == BoxKind.Tide && GetNodeOrNull<MiniPlanet>("Tide") is { } tide)
			tide.Water = 0.55f;
		if (Campaign.Active.CurrentBox.Kind == BoxKind.Frost)
			foreach (var planet in GetChildren().OfType<MiniPlanet>())
				planet.Water = 0.15f;
		if (mode != StartMode.Arrival)
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
		// The tutorial's outcome: tube and drill in place.
		home.TryAdd(Tutorial.TubeCell, BlockCatalog.Tube, Basis.Identity);
		home.TryAdd(Tutorial.DrillCell, BlockCatalog.AutoDrill, Tutorial.DrillDown);
		_progress = new ProgressSave { TutorialStep = -1, NexusUnlocked = true };
		if (mode == StartMode.Arrival)
		{
			// Your pockets start empty in a new Box: what you brought is in the hold at Home.
			Player.Inventory.Clear();
			for (int i = 0; i < Campaign.Active.WelcomeBots; i++)
				SpawnNearHome(Presets.WorkerBot(), home, 10 + i);
			return;
		}
		foreach (var (item, amount) in Player.Inventory.Items.ToArray())
			Player.Inventory.TransferTo(home.Inventory, item, amount);
		for (int i = 0; i < Tutorial.BotsToPrint; i++)
			SpawnNearHome(Presets.WorkerBot(), home, i);
		Player.ShowMessage("Home, a drill and two bots are ready. Press N for the Nexus");
	}

	private void SpawnNearHome(Blueprint bot, BlockGrid home, int slot) =>
		SpawnBlueprint(bot, new Transform3D(Basis.Identity, home.GlobalTransform * new Vector3(-8f + slot % 5 * 4f, 8f + slot / 5 * 4f, -6f)), isStatic: false, charge: 1f);

	/// <summary>Unpacks what came along on a journey: ingots into Home's storage, bots beside it.</summary>
	private void Unpack(Transfer transfer)
	{
		if (Colony.Home is not { } home)
			return;
		foreach (var (item, amount) in transfer.Ingots)
			home.Inventory.Add(item, amount);
		for (int i = 0; i < transfer.Bots.Count; i++)
			SpawnNearHome(transfer.Bots[i], home, 20 + i);
		float kg = transfer.Ingots.Values.Sum();
		Player.ShowMessage($"Welcome to {Campaign.Active.CurrentBox.Name}: {kg:0} kg of ingots and {transfer.Bots.Count} bots came with you");
	}

	// ---------------------------------------------------------------- menus

	private static bool AnySave() => Enumerable.Range(1, SaveSystem.SlotCount).Any(SaveSystem.Used);

	private void OpenTitle()
	{
		_hud.Visible = false;
		bool playing = SaveSystem.Used(SaveSystem.ActiveSlot);
		Menu.Open("REBIRTH", "The Curator boxed the stars. You carry what is left of us.",
		[
			new GameMenu.Entry(playing ? $"Continue   ({Campaign.Active.SlotName})" : "Start", () => { }),
			new GameMenu.Entry("Load game", () => OpenSlots(SlotMode.Load, OpenTitle), Enabled: AnySave),
			new GameMenu.Entry("New game - with tutorial", () => OpenSlots(SlotMode.NewGame, OpenTitle, StartMode.Tutorial)),
			new GameMenu.Entry("New game - skip the intro", () => OpenSlots(SlotMode.NewGame, OpenTitle, StartMode.SkipIntro)),
			new GameMenu.Entry("Quit", () => GetTree().Quit()),
		]);
	}

	private void OpenPauseMenu()
	{
		_hud.Visible = false;
		Menu.Open("PAUSED", $"{Campaign.Active.SlotName} · {Campaign.Active.CurrentBox.Name}. The world keeps turning while you are here.",
		[
			new GameMenu.Entry("Resume", () => { }),
			new GameMenu.Entry("Nexus overview   [N]", () => CallDeferred(MethodName.OpenNexus), Enabled: () => _progress.NexusUnlocked),
			new GameMenu.Entry("Forge   [B]", () => CallDeferred(MethodName.OpenForge)),
			new GameMenu.Entry("Save   [F5]", () => SaveGame("Saved")),
			new GameMenu.Entry("Save to slot...", () => OpenSlots(SlotMode.Save, OpenPauseMenu)),
			new GameMenu.Entry("Load game...", () => OpenSlots(SlotMode.Load, OpenPauseMenu), Enabled: AnySave),
			new GameMenu.Entry("New game - with tutorial...", () => OpenSlots(SlotMode.NewGame, OpenPauseMenu, StartMode.Tutorial)),
			new GameMenu.Entry("New game - skip the intro...", () => OpenSlots(SlotMode.NewGame, OpenPauseMenu, StartMode.SkipIntro)),
			new GameMenu.Entry("Save and quit", () =>
			{
				SaveGame(null);
				GetTree().Quit();
			}),
		]);
	}

	/// <param name="back">Reopens the menu the slot list came from.</param>
	private void OpenSlots(SlotMode mode, Action back, StartMode start = StartMode.SkipIntro)
	{
		_hud.Visible = false;
		Slots.Open(mode, (slot, name) =>
		{
			switch (mode)
			{
				case SlotMode.Save:
					SaveInto(slot, name);
					break;
				case SlotMode.Load:
					LoadSlot(slot);
					break;
				default:
					NewGame(slot, name, start);
					break;
			}
		}, back);
	}

	/// <summary>Saves the running game into <paramref name="slot"/>, which becomes the one being played.</summary>
	private void SaveInto(int slot, string name)
	{
		if (slot != SaveSystem.ActiveSlot)
		{
			// The other Boxes' worlds come along; the one you are in is written fresh below.
			SaveSystem.CopySlot(SaveSystem.ActiveSlot, slot);
			SaveSystem.SetActive(slot);
		}
		if (name.Length > 0)
			Campaign.Active.SlotName = name;
		SaveGame($"Saved as \"{Campaign.Active.SlotName}\" (slot {slot})");
	}

	private void LoadSlot(int slot)
	{
		SaveSystem.SetActive(slot);
		Campaign.Activate(slot);
		GameState.PendingTransfer = null;
		SaveSystem.PendingLoad = SaveSystem.ReadWorld(Campaign.Active.Current);
		GameState.NextBox = Campaign.Active.Current;
		GameState.NextStart = StartMode.Arrival;
		ReloadWorld();
	}

	private void NewGame(int slot, string name, StartMode mode)
	{
		SaveSystem.DeleteSlot(slot);
		SaveSystem.SetActive(slot);
		Campaign.Reset(name.Length > 0 ? name : $"Game {slot}");
		GameState.NextBox = 1;
		GameState.PendingTransfer = null;
		GameState.NextStart = mode;
		SaveSystem.PendingLoad = null;
		ReloadWorld();
	}

	/// <summary>The Nexus's "Fire" button: close it and let the Spirit Bomb take over the screen.</summary>
	private void FireLance()
	{
		Nexus.Close();
		if (!SpiritBomb.Fire())
			return;
		GameState.WorldInputBlocked = true;
		_hud.Visible = false;
		Tutorial.Visible = false;
	}

	/// <summary>After the breach: this Box is freed (it shines Starlight from now on) and you travel on to a new one.</summary>
	private void EnterNextBox()
	{
		var campaign = Campaign.Active;
		campaign.Free(_box, Colony.ResonancePerMinute);
		Travel(campaign.Boxes.Max(b => b.Index) + 1);
	}

	/// <summary>
	/// Goes to another Box: packs what the hold and bot bay allow, stores this world under its Box, and
	/// loads the target (or builds it fresh the first time).
	/// </summary>
	public void Travel(int target)
	{
		var campaign = Campaign.Active;
		var transfer = new Transfer();
		if (Colony.Home is { } home)
		{
			// Ingots, shared out fairly by what Home holds, up to the hold's capacity.
			var ingots = home.Inventory.Items.Where(kv => Items.ItemCatalog.Get(kv.Key).Category == Items.ItemCategory.Ingot).ToList();
			float total = ingots.Sum(kv => kv.Value);
			float share = total > 0f ? Mathf.Min(1f, campaign.CargoCarried / total) : 0f;
			foreach (var (item, amount) in ingots)
			{
				float take = amount * share;
				if (take >= 1f && home.Inventory.TryRemove(item, take))
					transfer.Ingots[item] = take;
			}
		}
		foreach (var bot in Colony.Bots.Take(campaign.BotsCarried).ToList())
		{
			transfer.Bots.Add(Blueprint.FromGrid(bot.Grid, bot.Grid.Label ?? "Bot", kind: DesignKind.Bot));
			bot.Grid.QueueFree();
		}
		Colony.Bots.RemoveAll(b => b.Grid.IsQueuedForDeletion());

		campaign.Leave(_box, Colony.ResonancePerMinute);
		SaveGame(null);
		var box = campaign.Reach(target);
		campaign.Current = box.Index;
		campaign.Save();

		GameState.PendingTransfer = transfer;
		GameState.NextBox = box.Index;
		GameState.NextStart = StartMode.Arrival;
		SaveSystem.PendingLoad = SaveSystem.ReadWorld(box.Index);
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

	private void AddAsteroid(Vector3 position, float radius, int seed, string? name = null) =>
		AddChild(new VoxelAsteroid { Name = name ?? $"Asteroid{seed}", Position = position, Radius = radius, Seed = seed });

	private BlockGrid SpawnBlueprint(Blueprint blueprint, Transform3D transform, bool isStatic, float charge)
	{
		var grid = BlockGrid.Create(this, transform, isStatic);
		blueprint.BuildInto(grid, charge);
		return grid;
	}

	// ---------------------------------------------------------------- input, Forge, printing

	public override void _UnhandledInput(InputEvent e)
	{
		if (e.IsActionPressed("quick_save") && !SpiritBomb.Firing)
			SaveGame("Saved");
		else if (e.IsActionPressed("quick_load"))
		{
			if (SaveSystem.ReadWorld(Campaign.Active.Current) is null)
				Player.ShowMessage("Nothing saved in this slot yet (F5 to save)");
			else
				LoadSlot(SaveSystem.ActiveSlot);
		}
		else if (GameState.WorldInputBlocked)
			return;
		else if (e.IsActionPressed("release_mouse"))
			OpenPauseMenu();
		else if (e.IsActionPressed("open_nexus"))
			OpenNexus();
		else if (e.IsActionPressed("open_inventory") && Player.PilotedGrid is null)
		{
			GameState.WorldInputBlocked = true;
			_hud.Visible = false;
			Inventory.Open(Player, Player.BuildTool.AimedGrid);
		}
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
		Campaign.Active.Tick((float)delta);
		if (Now >= _nextAutosave)
			SaveGame("Autosaved");
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
			SaveGame(null);
	}

	/// <summary>Writes this Box's world and the campaign into the slot being played.</summary>
	private void SaveGame(string? message)
	{
		_nextAutosave = Now + AutosaveSeconds;
		_progress.TutorialStep = Tutorial.StepIndex;
		Campaign.Active.SavedAt = System.DateTime.Now;
		Campaign.Active.Save();
		var save = SaveSystem.Capture(this, Player, Forge.CurrentBlueprint(), Colony.ToSave(), _progress);
		save.Box = _box;
		SaveSystem.WriteWorld(save);
		if (message is not null)
			Player.ShowMessage(message);
	}

	private void ReloadWorld()
	{
		GameState.WorldInputBlocked = false;
		GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
	}

	private void BuildEnvironment(BoxLook look)
	{
		var skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/retro_sky.gdshader") };
		skyMaterial.SetShaderParameter("plum", look.Plum);
		skyMaterial.SetShaderParameter("peach", look.Peach);
		skyMaterial.SetShaderParameter("teal", look.Teal);
		skyMaterial.SetShaderParameter("rose", look.Rose);
		Sun.Strength = look.SolarStrength;
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
			LightEnergy = look.SunEnergy,
			LightColor = look.SunColor,
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
