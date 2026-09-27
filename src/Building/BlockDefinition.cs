using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Items;

namespace Rebirth.Building;

public enum BlockKind { Armor, Cockpit, Thruster, Gyroscope, Battery, SolarPanel, CargoContainer, Refinery, Fabricator, AutoDrill, Tube, BotCore, Uplink, AirProcessor, Hydrator, SeedGarden, Incubator, BreachLance }

/// <param name="Paint">Default paint colour; each block type has its own so they are easy to tell apart.</param>
/// <param name="MaxIntegrity">Damage the block absorbs before it is destroyed.</param>
public sealed record BlockDefinition(string Id, string DisplayName, BlockKind Kind, Color Paint, float Mass, float MaxIntegrity)
{
	/// <summary>Newtons, pushing the grid along the block's local forward (-Z); exhaust leaves through +Z.</summary>
	public float Thrust { get; init; }
	/// <summary>Newton-metres of rotational authority contributed to the grid.</summary>
	public float Torque { get; init; }
	/// <summary>Megawatts drawn at full load (thrusters: at full throttle).</summary>
	public float PowerDraw { get; init; }
	/// <summary>Megawatts produced when the block's local +Y faces the sun.</summary>
	public float SolarOutput { get; init; }
	/// <summary>Megawatt-hours a battery stores.</summary>
	public float BatteryCapacity { get; init; }
	/// <summary>Megawatts a battery can deliver or absorb.</summary>
	public float BatteryMaxPower { get; init; }
	/// <summary>Kilograms of items the block adds to the grid's shared storage.</summary>
	public float CargoCapacity { get; init; }
	/// <summary>Kilograms a machine can hold waiting to be worked on (delivered by the logistics network).</summary>
	public float InputCapacity { get; init; }
	/// <summary>Kilograms of finished product a machine can hold until the network carries it away.</summary>
	public float OutputCapacity { get; init; }

	/// <summary>Carries parcels: part of a grid's logistics network when touching other such blocks.</summary>
	public bool Logistics => Kind is BlockKind.Tube or BlockKind.CargoContainer or BlockKind.Refinery or BlockKind.Fabricator or BlockKind.AutoDrill
		or BlockKind.AirProcessor or BlockKind.Hydrator or BlockKind.SeedGarden;

	/// <summary>Heals the planet it stands on (air, water, soil) from what the network brings it.</summary>
	public bool Terraformer => Kind is BlockKind.AirProcessor or BlockKind.Hydrator or BlockKind.SeedGarden;

	/// <summary>Drawn as a solid cube in the grid mesh; tubes are see-through pipes instead.</summary>
	public bool FullCube => Kind != BlockKind.Tube;
	/// <summary>Ingots (item id → kg) consumed to build the block in survival; refunded on removal.</summary>
	public IReadOnlyDictionary<string, float> Cost { get; init; } = new Dictionary<string, float>();
}

/// <summary>A block as placed in a grid. Orientation is one of the 24 axis-aligned rotations, in grid-local space.</summary>
public readonly record struct PlacedBlock(BlockDefinition Definition, Basis Orientation, Color Paint);

/// <summary>Mutable per-block data; travels with the block when a grid splits.</summary>
public sealed class BlockState
{
	public float Integrity;
	/// <summary>Megawatt-hours held by a battery.</summary>
	public float StoredEnergy;
	/// <summary>Machine input buffer (refinery ore, fabricator ingots); null for other blocks.</summary>
	public Inventory? Input;
	/// <summary>Machine output buffer (drill ore, refinery ingots); null for other blocks.</summary>
	public Inventory? Output;

	/// <summary>A newly built block: intact, with empty buffers and batteries at <paramref name="charge"/>.</summary>
	public static BlockState Fresh(BlockDefinition definition, float charge) => new()
	{
		Integrity = definition.MaxIntegrity,
		StoredEnergy = definition.BatteryCapacity * charge,
		Input = definition.InputCapacity > 0f ? new Inventory { Capacity = definition.InputCapacity } : null,
		Output = definition.OutputCapacity > 0f ? new Inventory { Capacity = definition.OutputCapacity } : null,
	};
}

public static class BlockCatalog
{
	// Masses, outputs and power figures are in the ballpark of Space Engineers' large-grid blocks.
	public static readonly BlockDefinition LightArmor = new("light_armor", "Light Armor Block", BlockKind.Armor, Palette.Cream, 418f, 100f)
	{
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 60f },
	};

	public static readonly BlockDefinition HeavyArmor = new("heavy_armor", "Heavy Armor Block", BlockKind.Armor, Palette.Slate, 3300f, 600f)
	{
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 400f },
	};

	public static readonly BlockDefinition Cockpit = new("cockpit", "Control Core", BlockKind.Cockpit, Palette.Sky, 1200f, 80f)
	{
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 150f, ["silicon_wafer"] = 50f },
	};

	public static readonly BlockDefinition Thruster = new("ion_thruster", "Ion Thruster", BlockKind.Thruster, Palette.Orange, 700f, 80f)
	{
		Thrust = 250_000f,
		PowerDraw = 3.4f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 200f, ["nickel_ingot"] = 80f },
	};

	public static readonly BlockDefinition Gyroscope = new("gyroscope", "Gyroscope", BlockKind.Gyroscope, Palette.Mustard, 1400f, 80f)
	{
		Torque = 30_000_000f,
		PowerDraw = 0.03f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 250f, ["nickel_ingot"] = 30f },
	};

	public static readonly BlockDefinition Battery = new("battery", "Battery", BlockKind.Battery, Palette.Mint, 1500f, 80f)
	{
		BatteryCapacity = 3f,
		BatteryMaxPower = 12f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 100f, ["nickel_ingot"] = 40f, ["silicon_wafer"] = 20f },
	};

	public static readonly BlockDefinition SolarPanel = new("solar_panel", "Solar Panel", BlockKind.SolarPanel, Palette.Cream, 400f, 60f)
	{
		SolarOutput = 0.16f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 60f, ["silicon_wafer"] = 60f },
	};

	public static readonly BlockDefinition CargoContainer = new("cargo_container", "Cargo Container", BlockKind.CargoContainer, Palette.Plum, 900f, 100f)
	{
		CargoCapacity = 15_000f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 150f },
	};

	public static readonly BlockDefinition Refinery = new("refinery", "Refinery", BlockKind.Refinery, Palette.Coral, 3000f, 150f)
	{
		PowerDraw = 0.56f,
		InputCapacity = 400f,
		OutputCapacity = 400f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 600f, ["nickel_ingot"] = 60f, ["silicon_wafer"] = 60f },
	};

	/// <summary>Prints blueprints from the grid's ingots; the result appears in front of its -Z face.</summary>
	public static readonly BlockDefinition Fabricator = new("fabricator", "Fabricator", BlockKind.Fabricator, Palette.Teal, 2500f, 150f)
	{
		PowerDraw = 1.5f,
		// Holds the ingots for the job it is working on; big enough for any sensible design.
		InputCapacity = 100_000f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 500f, ["nickel_ingot"] = 100f, ["silicon_wafer"] = 80f },
	};

	/// <summary>Bores into whatever rock is in front of its -Z face and sends the ore out as parcels.</summary>
	public static readonly BlockDefinition AutoDrill = new("auto_drill", "Auto Drill", BlockKind.AutoDrill, Palette.Lemon, 1800f, 120f)
	{
		PowerDraw = 0.8f,
		OutputCapacity = 400f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 250f, ["nickel_ingot"] = 40f },
	};

	/// <summary>A glass pipe that links machines and storage so parcels can travel between them.</summary>
	public static readonly BlockDefinition Tube = new("tube", "Tube", BlockKind.Tube, Palette.Cream, 150f, 50f)
	{
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 20f },
	};

	/// <summary>
	/// The brain of a worker bot: a grid with one flies itself for the Nexus (building sites, hauling).
	/// Has its own small hover drive and a 200 kg hold; thrusters make the bot faster, cargo lets it carry more.
	/// </summary>
	public static readonly BlockDefinition BotCore = new("bot_core", "Bot Core", BlockKind.BotCore, Palette.Peach, 300f, 60f)
	{
		CargoCapacity = 200f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 120f, ["silicon_wafer"] = 20f },
	};

	/// <summary>
	/// Links the Nexus to everything within <c>Colony.UplinkRange</c>: bots can only build where an
	/// uplink reaches. Out-of-reach planets stay dark until you fly there and set one down yourself.
	/// </summary>
	public static readonly BlockDefinition Uplink = new("uplink", "Uplink", BlockKind.Uplink, Palette.Sky, 800f, 80f)
	{
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 200f, ["nickel_ingot"] = 30f, ["silicon_wafer"] = 80f },
	};

	/// <summary>Bakes stone into breathable air for the planet it stands on.</summary>
	public static readonly BlockDefinition AirProcessor = new("air_processor", "Air Processor", BlockKind.AirProcessor, Palette.Sky, 2200f, 120f)
	{
		PowerDraw = 0.8f,
		InputCapacity = 400f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 300f, ["nickel_ingot"] = 40f, ["silicon_wafer"] = 60f },
	};

	/// <summary>Melts ice into the planet's seas.</summary>
	public static readonly BlockDefinition Hydrator = new("hydrator", "Hydrator", BlockKind.Hydrator, Palette.Teal, 2000f, 120f)
	{
		PowerDraw = 0.6f,
		InputCapacity = 400f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 250f, ["nickel_ingot"] = 60f, ["silicon_wafer"] = 40f },
	};

	/// <summary>Grinds stone into soil and sows it; only takes once the air and seas are coming back.</summary>
	public static readonly BlockDefinition SeedGarden = new("seed_garden", "Seed Garden", BlockKind.SeedGarden, Palette.Mint, 1500f, 100f)
	{
		PowerDraw = 0.4f,
		InputCapacity = 200f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 150f, ["silicon_wafer"] = 80f },
	};

	/// <summary>
	/// Wakes the human DNA you carry into families, once the planet can hold them. A village grows around it.
	/// </summary>
	public static readonly BlockDefinition Incubator = new("incubator", "Incubator", BlockKind.Incubator, Palette.Coral, 1800f, 120f)
	{
		PowerDraw = 0.5f,
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 300f, ["nickel_ingot"] = 80f, ["silicon_wafer"] = 150f },
	};

	/// <summary>
	/// The way out: a spire that drinks Resonance until it is full, then fires one beam at the Box.
	/// </summary>
	public static readonly BlockDefinition BreachLance = new("breach_lance", "Breach Lance", BlockKind.BreachLance, Palette.Mustard, 8000f, 400f)
	{
		Cost = new Dictionary<string, float> { ["iron_ingot"] = 2500f, ["nickel_ingot"] = 600f, ["silicon_wafer"] = 900f },
	};

	public static readonly IReadOnlyList<BlockDefinition> All =
		[LightArmor, HeavyArmor, Cockpit, Thruster, Gyroscope, Battery, SolarPanel, CargoContainer, Refinery, Fabricator, AutoDrill, Tube, BotCore, Uplink,
		AirProcessor, Hydrator, SeedGarden, Incubator, BreachLance];

	private static readonly Dictionary<string, BlockDefinition> ById = All.ToDictionary(b => b.Id);

	public static BlockDefinition Get(string id) => ById[id];
}

/// <summary>Warm retro-futuristic paint colours for blocks, accents and the paint tool.</summary>
public static class Palette
{
	public static readonly Color Cream = new(0.95f, 0.90f, 0.80f);
	public static readonly Color Orange = new(0.96f, 0.50f, 0.18f);
	public static readonly Color Coral = new(0.93f, 0.40f, 0.38f);
	public static readonly Color Mustard = new(0.93f, 0.72f, 0.22f);
	public static readonly Color Mint = new(0.56f, 0.83f, 0.64f);
	public static readonly Color Teal = new(0.16f, 0.58f, 0.60f);
	public static readonly Color Sky = new(0.46f, 0.72f, 0.90f);
	public static readonly Color Plum = new(0.52f, 0.34f, 0.56f);
	public static readonly Color Slate = new(0.40f, 0.44f, 0.50f);
	/// <summary>Construction-machine yellow.</summary>
	public static readonly Color Lemon = new(0.98f, 0.84f, 0.32f);
	/// <summary>Friendly bot-shell colour.</summary>
	public static readonly Color Peach = new(1f, 0.74f, 0.58f);

	public static readonly IReadOnlyList<Color> Swatches = [Cream, Orange, Coral, Mustard, Mint, Teal, Sky, Plum, Slate];
}
