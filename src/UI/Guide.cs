using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Core;
using Rebirth.Life;
using Rebirth.Nexus;
using Rebirth.World;

namespace Rebirth.UI;

/// <summary>
/// The hand to hold after the tutorial: always one next goal on the way from a lone base to breaking
/// the Box, worked out from the world as it is (nothing to save). Each goal says what to do in plain
/// words and knows where in the Nexus to point: which place, and which design to pick.
/// </summary>
public sealed class Guide
{
	public sealed record Goal(string Title, string How, Func<string?>? Progress = null, Func<(Node3D? Target, string? Design)>? Show = null);

	public Colony Colony { get; init; } = null!;
	public People People { get; init; } = null!;

	private IEnumerable<MiniPlanet> Planets => Colony.Bodies.OfType<MiniPlanet>();

	/// <summary>How the building sites are doing, with advice when one waits for materials.</summary>
	private string? JobsProgress()
	{
		if (Colony.Jobs.Count == 0)
			return null;
		var lines = Colony.Jobs.Select(j => $"{j.Name}: {j.Built}/{j.Order.Count} blocks" + (j.Status.StartsWith("Waiting for") ? $" - {j.Status.ToLowerInvariant()}" : "")).ToList();
		if (Colony.Jobs.Any(j => j.Status.StartsWith("Waiting for")))
			lines.Add("Home makes ingots from the stone its drill brings up; just give it a few minutes. More drill sites make it faster.");
		return string.Join("\n", lines);
	}
	private IEnumerable<BlockGrid> Stations => Colony.Grids.Where(g => g.IsStatic && !g.IsBot);

	/// <summary>The first goal not reached yet, or null when there is nothing left to point at.</summary>
	public Goal? Current()
	{
		if (Colony.Home is null)
			return null;
		var home = Colony.Home.GlobalPosition;
		MiniPlanet? Nearest(Func<MiniPlanet, bool> where) =>
			Planets.Where(where).OrderBy(p => p.GlobalPosition.DistanceTo(home)).FirstOrDefault();
		// The planet most worth healing: the one furthest along already, else the nearest linked.
		MiniPlanet? Favourite() =>
			Planets.Where(Colony.IsLinked).OrderByDescending(p => p.Vitality).ThenBy(p => p.GlobalPosition.DistanceTo(home)).FirstOrDefault();
		bool IsIcy(VoxelBody b) => b is MiniPlanet { Crust: VoxelMaterials.Frost };

		if (!Planets.Any(Colony.IsLinked))
			return new Goal("Light up a planet",
				"Planets start dark: the Nexus can't reach them. Open the Nexus ({open_nexus}), pick a dark planet and press \"Send bots to set up an Uplink\". Your bots fly there and build it.",
				JobsProgress,
				() => (Nearest(p => !Colony.IsLinked(p)), null));

		if (!Colony.Sites.Any(s => s.HasBlock(BlockKind.AutoDrill)))
			return new Goal("Start a mine",
				"In the Nexus, pick a lit planet or asteroid, choose \"Drill Site\" and press \"Send bots to build\". It drills, refines, and bots bring the ingots home.",
				JobsProgress,
				() => ((Node3D?)Nearest(Colony.IsLinked) ?? Colony.Bodies.FirstOrDefault(b => b.Name != "Home Rock" && Colony.IsLinked(b)), "Drill Site"));

		if (Colony.Bots.Count < 4)
			return new Goal("More hands",
				"More bots build and haul more at once. In the Nexus, select Home and press \"Print bot\" (it uses ingots from Home).",
				() => $"Bots: {Colony.Bots.Count} / 4",
				() => (Colony.Home, null));

		if (!Planets.Any(p => p.Air > 0.005f))
			return new Goal("Give a planet air",
				"Pick a lit planet in the Nexus, choose \"Air Maker\" and send the bots. It drills stone and turns it into air.",
				JobsProgress,
				() => (Favourite(), "Air Maker"));

		if (!Stations.Any(s => s.Inventory.Get("ice") > 1f) && !Planets.Any(p => p.Water > 0.005f))
			return new Goal("Find ice",
				"Seas need ice. Build a \"Drill Site\" on an icy planet (like Frost). If it is still dark, set up an Uplink there first.",
				JobsProgress,
				() => (Nearest(IsIcy) ?? Favourite(), Colony.Bodies.Any(b => IsIcy(b) && Colony.IsLinked(b)) ? "Drill Site" : null));

		if (!Planets.Any(p => p.Water > 0.005f))
			return new Goal("Fill the seas",
				"On the planet you are healing, build a \"Water Works\". Bots haul the ice to it by themselves and it melts it into seas.",
				JobsProgress,
				() => (Favourite(), "Water Works"));

		var healing = Favourite();
		if (healing is not null && (healing.Air < Colony.GardenThreshold || healing.Water < Colony.GardenThreshold))
			return new Goal($"Heal {healing.Name}",
				$"Keep going until air and water are at {Colony.GardenThreshold:P0}: more Air Makers and Water Works make it faster. Then gardens can grow.",
				() => $"{healing.Name}: air {healing.Air:P0}, water {healing.Water:P0} (need {Colony.GardenThreshold:P0} each)",
				() => (healing, "Air Maker"));

		if (!Planets.Any(p => p.Soil > 0.005f))
			return new Goal("Grow soil",
				$"Air and seas are back on {healing?.Name ?? "your planet"}. Build a \"Garden\" there: it grows soil, and the ground turns green.",
				JobsProgress,
				() => (healing, "Garden"));

		if (People.Settlements.Count == 0)
		{
			var home2 = Planets.FirstOrDefault(People.IsHabitable);
			return new Goal("Wake the first people",
				home2 is null
					? $"People need {People.HabitableAir:P0} air, {People.HabitableWater:P0} water and {People.HabitableSoil:P0} soil. Keep healing with Air Makers, Water Works and Gardens."
					: $"{home2.Name} can hold people now! Build a \"Settlement Seed\" there: the incubator wakes families from the DNA you carry.",
				() => healing is null ? null : $"{healing.Name}: air {healing.Air:P0}, water {healing.Water:P0}, soil {healing.Soil:P0}",
				() => ((Node3D?)home2 ?? healing, home2 is null ? "Garden" : "Settlement Seed"));
		}

		if (!Colony.HasLance)
			return new Goal("Build the Breach Lance",
				"Living planets and villages make Resonance. Build a \"Breach Lance\" (Home Rock is fine): it gathers that Resonance into a ball of light.",
				() => $"Resonance: {Colony.Resonance:0}",
				() => (Colony.Bodies.FirstOrDefault(b => b.Name == "Home Rock"), "Breach Lance"));

		if (Campaign.Active.CurrentBox.Freed is false)
			return new Goal("Break the Box",
				"When the Lance is full, select it in the Nexus and press \"Fire the Breach Lance\". Everyone lends their light.",
				() => $"Lance: {Colony.LanceFraction:P0}   (+{Colony.ResonancePerMinute:0.0} Resonance/min)",
				() => (Stations.FirstOrDefault(s => s.HasBlock(BlockKind.BreachLance)), null));

		return null;
	}
}
