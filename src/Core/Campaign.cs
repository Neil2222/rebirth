using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Rebirth.Persistence;

namespace Rebirth.Core;

/// <summary>The character of a Box: its sky, its sun and what its planets are like.</summary>
public enum BoxKind { First, Tide, Dim, Frost, Ember }

/// <summary>A star cluster you have reached.</summary>
public sealed class BoxInfo
{
	public int Index { get; set; }
	public BoxKind Kind { get; set; }
	public int Seed { get; set; }
	public string Name { get; set; } = "";
	/// <summary>You broke out of it: it now shines on its own and feeds Starlight.</summary>
	public bool Freed { get; set; }
	/// <summary>Resonance per minute it had when you last left: what it gives off as Starlight.</summary>
	public float Glow { get; set; }
}

/// <summary>A permanent upgrade bought with Starlight.</summary>
public sealed record Upgrade(string Id, string Name, string Effect, int MaxLevel, int BaseCost);

/// <summary>
/// Everything that spans Boxes: which Boxes you have reached, the Starlight pool the freed ones fill,
/// and the upgrades bought with it. Stored beside the per-Box worlds, in user://campaign.json.
/// </summary>
public sealed class Campaign
{
	public const string Path = "user://campaign.json";
	/// <summary>Starlight for each breach, on top of what freed Boxes give off.</summary>
	public const float BreachBonus = 150f;
	/// <summary>Share of a freed Box's Resonance rate that flows into the pool.</summary>
	public const float GlowShare = 0.5f;

	public static readonly IReadOnlyList<Upgrade> Upgrades =
	[
		new("bot_bay", "Bot bay", "+1 bot you can take along when you travel (2 at the start)", 4, 60),
		new("cargo_hold", "Cargo hold", "+3 t of ingots you can take along (2 t at the start)", 5, 40),
		new("bot_speed", "Bot thrusters", "Bots fly 15% faster", 5, 50),
		new("bot_discount", "Bot craftsmanship", "Building by bot costs 0.25× less (2.5× at the start)", 4, 80),
		new("refinery", "Refinery tuning", "Refineries work 20% faster", 5, 50),
		new("welcome_bots", "Welcome party", "+1 free Worker Bot waiting in every new Box", 3, 100),
	];

	public List<BoxInfo> Boxes { get; set; } = new();
	public int Current { get; set; } = 1;
	public float Starlight { get; set; }
	public Dictionary<string, int> Levels { get; set; } = new();

	public static Campaign Active { get; private set; } = Load();

	public BoxInfo CurrentBox => Boxes.First(b => b.Index == Current);

	public int Level(string id) => Levels.GetValueOrDefault(id);

	public int CostOf(Upgrade upgrade) => upgrade.BaseCost * (Level(upgrade.Id) + 1);

	public bool Buy(Upgrade upgrade)
	{
		int cost = CostOf(upgrade);
		if (Level(upgrade.Id) >= upgrade.MaxLevel || Starlight < cost)
			return false;
		Starlight -= cost;
		Levels[upgrade.Id] = Level(upgrade.Id) + 1;
		Save();
		return true;
	}

	// ------------------------------------------------------------ effects

	public int BotsCarried => 2 + Level("bot_bay");
	public float CargoCarried => 2000f + 3000f * Level("cargo_hold");
	public float BotSpeedFactor => 1f + 0.15f * Level("bot_speed");
	public float AutoBuildFactor => 2.5f - 0.25f * Level("bot_discount");
	public float RefineryFactor => 1f + 0.2f * Level("refinery");
	public int WelcomeBots => Level("welcome_bots");

	public float StarlightPerMinute => Boxes.Where(b => b.Freed).Sum(b => b.Glow * GlowShare);

	/// <summary>Freed Boxes keep shining while you play anywhere.</summary>
	public void Tick(float dt) => Starlight += StarlightPerMinute * dt / 60f;

	// ------------------------------------------------------------ boxes

	/// <summary>The Box after <paramref name="from"/>: the Tide Box second, generated ones after that.</summary>
	public BoxInfo Reach(int index)
	{
		if (Boxes.FirstOrDefault(b => b.Index == index) is { } known)
			return known;
		int seed = (int)(GD.Randi() % 100000);
		var kind = index switch
		{
			1 => BoxKind.First,
			2 => BoxKind.Tide,
			_ => new[] { BoxKind.Dim, BoxKind.Frost, BoxKind.Ember }[seed % 3],
		};
		var box = new BoxInfo { Index = index, Kind = kind, Seed = seed, Name = NameFor(kind, seed) };
		Boxes.Add(box);
		return box;
	}

	private static string NameFor(BoxKind kind, int seed)
	{
		string[] words = kind switch
		{
			BoxKind.First => ["First"],
			BoxKind.Tide => ["Tide"],
			BoxKind.Dim => ["Dim", "Hush", "Lantern-less", "Velvet"],
			BoxKind.Frost => ["Frost", "Rime", "Snowglobe", "Hailstone"],
			_ => ["Ember", "Kiln", "Cinder", "Saffron"],
		};
		return $"The {words[seed % words.Length]} Box";
	}

	/// <summary>Breaking out of the current Box: it is freed and starts to shine.</summary>
	public void Free(int index, float glow)
	{
		var box = Boxes.First(b => b.Index == index);
		if (!box.Freed)
			Starlight += BreachBonus;
		box.Freed = true;
		box.Glow = glow;
		Save();
	}

	/// <summary>Leaving a freed Box updates how brightly it shines from now on.</summary>
	public void Leave(int index, float glow)
	{
		var box = Boxes.First(b => b.Index == index);
		if (box.Freed)
			box.Glow = Mathf.Max(box.Glow, glow);
		Save();
	}

	public static string SlotFor(int index) => $"box{index}";

	// ------------------------------------------------------------ storage

	/// <summary>A new game: only the first Box, no Starlight, no upgrades.</summary>
	public static void Reset()
	{
		Active = new Campaign();
		Active.Reach(1);
		Active.Save();
	}

	public void Save()
	{
		using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
		file?.StoreString(JsonSerializer.Serialize(this, Blueprint.Json));
	}

	private static Campaign Load()
	{
		string json = FileAccess.FileExists(Path) ? FileAccess.GetFileAsString(Path) : "";
		Campaign? campaign = null;
		try
		{
			if (json.Length > 0)
				campaign = JsonSerializer.Deserialize<Campaign>(json, Blueprint.Json);
		}
		catch (JsonException e)
		{
			GD.PushWarning($"Starting a fresh campaign: {e.Message}");
		}
		campaign ??= new Campaign();
		if (campaign.Boxes.Count == 0)
			campaign.Reach(1);
		return campaign;
	}
}
