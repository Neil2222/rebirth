using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Persistence;

namespace Rebirth.Forge;

/// <summary>
/// Helps you design: which parts belong to what kind of design, what each part does, and a checklist of
/// what a design of this kind still needs to work.
/// </summary>
public static class PartGuide
{
	public sealed record Category(string Name, DesignKind[] Kinds, BlockDefinition[] Blocks);

	private static readonly DesignKind[] All = [DesignKind.Ship, DesignKind.Station, DesignKind.Body, DesignKind.Bot];
	private static readonly DesignKind[] Movers = [DesignKind.Ship, DesignKind.Body, DesignKind.Bot];

	public static readonly IReadOnlyList<Category> Categories =
	[
		new("Frame", All, [BlockCatalog.LightArmor, BlockCatalog.HeavyArmor]),
		new("Brain", All, [BlockCatalog.Cockpit, BlockCatalog.BotCore]),
		new("Movement", Movers, [BlockCatalog.Thruster, BlockCatalog.Gyroscope]),
		new("Power", All, [BlockCatalog.Battery, BlockCatalog.SolarPanel]),
		new("Power plants", [DesignKind.Station], [BlockCatalog.PowerPylon, BlockCatalog.StoneBurner, BlockCatalog.WindTurbine, BlockCatalog.GeothermalTap]),
		new("Storage & tubes", [DesignKind.Ship, DesignKind.Station, DesignKind.Bot], [BlockCatalog.CargoContainer, BlockCatalog.Tube]),
		new("Production", [DesignKind.Station, DesignKind.Ship], [BlockCatalog.AutoDrill, BlockCatalog.Refinery, BlockCatalog.Fabricator]),
		new("Nexus", [DesignKind.Station], [BlockCatalog.Uplink, BlockCatalog.Firewall]),
		new("Life", [DesignKind.Station], [BlockCatalog.AirProcessor, BlockCatalog.Hydrator, BlockCatalog.SeedGarden, BlockCatalog.Incubator]),
		new("The way out", [DesignKind.Station], [BlockCatalog.BreachLance]),
	];

	/// <summary>Whether a part makes sense in this kind of design (the brain depends on the kind).</summary>
	public static bool Fits(BlockDefinition block, DesignKind kind) => BlockCatalog.Get(block.FamilyId) is var family && family.Kind switch
	{
		BlockKind.Cockpit => kind is DesignKind.Ship or DesignKind.Body,
		BlockKind.BotCore => kind == DesignKind.Bot,
		_ => Categories.Any(c => c.Blocks.Contains(family) && c.Kinds.Contains(kind)),
	};

	public static string Intro(DesignKind kind) => kind switch
	{
		DesignKind.Body => "A robot body for you to wear. It is shrunk to your size.",
		DesignKind.Bot => "A worker bot for the Nexus: it flies itself, carries and builds.",
		DesignKind.Station => "Something that stays put: a base, a mine, a farm.",
		_ => "A ship you fly yourself from its Control Core.",
	};

	public static string Describe(BlockDefinition block) => block.Kind switch
	{
		BlockKind.Armor => (block.FamilyId == BlockCatalog.HeavyArmor.Id ? "Tough and heavy. For hulls that take knocks." : "Cheap, light building block for shapes and frames.")
			+ ShapeNote(block.Shape),
		BlockKind.Cockpit => "The brain and seat. In a body it is the head; in a ship, where you sit. It faces its blue window forward.",
		BlockKind.BotCore => "A bot's brain with its own small hover drive and a 200 kg hold. Every bot needs one.",
		BlockKind.Thruster => "Pushes the opposite way from its flame. Point flames down to lift, backwards to fly forward.",
		BlockKind.Gyroscope => "Turns the design. Ships need one to steer; bodies use them as shoulders.",
		BlockKind.Battery => "Stores power for when the sun isn't shining. Thrusters drink a lot of it.",
		BlockKind.SolarPanel => "Makes power from the sun on its bright face. Point that face at the sky.",
		BlockKind.CargoContainer => "Shared storage for the whole design. On a bot: how much it can carry.",
		BlockKind.Tube => "Links machines and storage so ore and ingots travel between them as pods.",
		BlockKind.AutoDrill => "Drills the rock in front of its head and sends out ore. Point the head at the ground.",
		BlockKind.Refinery => "Turns ore into ingots. Must touch storage or tubes.",
		BlockKind.Fabricator => "Prints designs (bots, ships) from ingots, out of its front face.",
		BlockKind.Uplink => "Lets the Nexus reach 170 m around it: bots can build there.",
		BlockKind.Firewall => "Chases viruses out of machines within 170 m by itself. Needs power.",
		BlockKind.AirProcessor => "Turns stone into air for the planet it stands on.",
		BlockKind.Hydrator => "Melts ice into the planet's seas.",
		BlockKind.SeedGarden => "Grows soil once air and water are at 30%.",
		BlockKind.Incubator => "Wakes families from DNA once the planet is habitable.",
		BlockKind.BreachLance => "Gathers Resonance into a ball of light that breaks the Box open.",
		BlockKind.PowerPylon => $"Joins this station's power with every station that has a pylon within {BlockGrid.PylonReach:0} m. Build a power plant once and cable it to your sites.",
		BlockKind.StoneBurner => "Burns stone into power, day and night. The network brings it stone from storage.",
		BlockKind.WindTurbine => "Makes power from a planet's wind: nothing on a dead world, full power once the air is back.",
		BlockKind.GeothermalTap => "Draws a planet's inner heat: lots of power, but only standing on a planet. Stronger in an Ember Box.",
		_ => "",
	};

	private static string ShapeNote(BlockShape shape) => shape switch
	{
		BlockShape.Slope => " A ramp: the low edge faces front, the full sides are the bottom and back.",
		BlockShape.Corner => " A pyramid corner, for the tips of noses and roofs.",
		BlockShape.InnerCorner => " A cube with one corner cut off, to go between two slopes.",
		BlockShape.Half => " Half height, for thin floors and steps.",
		BlockShape.Rounded => " A rounded edge: a soft curve instead of a sharp corner.",
		BlockShape.Cylinder => " A round pillar.",
		_ => " Comes in more shapes.",
	};

	/// <summary>What this kind of design needs, ticked off as you build.</summary>
	public static List<(bool Done, string Text)> Checklist(BlockGrid design, DesignKind kind)
	{
		var kinds = design.Blocks.Select(b => b.Value.Definition.Kind).ToList();
		bool Has(BlockKind k) => kinds.Contains(k);
		bool power = Has(BlockKind.Battery) || Has(BlockKind.SolarPanel) || Has(BlockKind.PowerPylon)
			|| Has(BlockKind.StoneBurner) || Has(BlockKind.WindTurbine) || Has(BlockKind.GeothermalTap);
		var list = new List<(bool, string)>();
		switch (kind)
		{
			case DesignKind.Body:
				list.Add((Has(BlockKind.Cockpit), "A Control Core as the head (on top)"));
				list.Add((design.ThrustCapacity(Vector3I.Up) > 0f, "A thruster with its flame pointing down: your jetpack"));
				list.Add((Has(BlockKind.Battery), "A battery to power the jetpack"));
				list.Add((design.BlockCount >= 6, "Something to stand on: legs or a base"));
				break;
			case DesignKind.Bot:
				list.Add((Has(BlockKind.BotCore), "A Bot Core (required: it is the bot's brain and drive)"));
				list.Add((Has(BlockKind.CargoContainer), "A Cargo Container, so it can carry more than 200 kg"));
				list.Add((Has(BlockKind.Thruster), "Optional: thrusters make it fly faster"));
				break;
			case DesignKind.Station:
				list.Add((power, "Power: solar panels facing up, a power plant, or a pylon to cable it in"));
				if (Has(BlockKind.AutoDrill) || Has(BlockKind.Refinery))
				{
					list.Add((Has(BlockKind.CargoContainer), "Storage, touching the machines (or joined by tubes)"));
					list.Add((Has(BlockKind.AutoDrill) && Has(BlockKind.Refinery), "Drill + refinery: ore in, ingots out"));
				}
				else
					list.Add((design.BlockCount > 1, "Machines: pick them from Production or Life"));
				break;
			default:
				list.Add((Has(BlockKind.Cockpit), "A Control Core: where you sit and steer"));
				list.Add((design.ThrustCapacity(Vector3I.Forward) > 0f && design.ThrustCapacity(Vector3I.Back) > 0f, "Thrusters forward and back"));
				list.Add((design.ThrustCapacity(Vector3I.Up) > 0f && design.ThrustCapacity(Vector3I.Down) > 0f, "Thrusters up and down"));
				list.Add((Has(BlockKind.Gyroscope), "A gyroscope to turn"));
				list.Add((power, "A battery or solar panels"));
				break;
		}
		return list;
	}
}
