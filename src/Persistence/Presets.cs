using System.Collections.Generic;
using Godot;
using Rebirth.Building;

namespace Rebirth.Persistence;

/// <summary>Built-in designs: a starting point in the Forge and the starter grids in a new world.</summary>
public static class Presets
{
	// A thruster pushes its grid along its local -Z; these orientations aim that push at each axis.
	private static readonly Basis PushForward = Basis.Identity;
	private static readonly Basis PushBack = new(Vector3.Up, Mathf.Pi);
	private static readonly Basis PushRight = new(Vector3.Up, -Mathf.Pi / 2f);
	private static readonly Basis PushLeft = new(Vector3.Up, Mathf.Pi / 2f);
	private static readonly Basis PushDown = new(Vector3.Right, -Mathf.Pi / 2f);
	private static readonly Basis PushUp = new(Vector3.Right, Mathf.Pi / 2f);

	public static IReadOnlyList<Blueprint> All => [Custodian(), StarterHauler(), ScoutDrone(), Outpost(), MiningRig(), DrillSite(), AirMaker(), WaterWorks(), Garden(), SettlementSeed(), BreachLanceSite(), UplinkPost(), WorkerBot()];

	/// <summary>
	/// Default robot body: legs, torso with a battery heart and gyro shoulders, a visor head,
	/// and two jetpack thrusters on the back firing downwards. Shrunk to player size when worn.
	/// </summary>
	public static Blueprint Custodian()
	{
		var bp = new Blueprint { Name = "Custodian", Kind = DesignKind.Body };
		foreach (int x in new[] { -1, 1 })
		{
			bp.Add(new Vector3I(x, 0, 0), BlockCatalog.HeavyArmor, Basis.Identity);
			bp.Add(new Vector3I(x, 1, 0), BlockCatalog.LightArmor, Basis.Identity);
			bp.Add(new Vector3I(x, 3, 0), BlockCatalog.Gyroscope, Basis.Identity, Palette.Orange);
			bp.Add(new Vector3I(x, 2, 1), BlockCatalog.Thruster, PushUp);
		}
		for (int x = -1; x <= 1; x++)
			bp.Add(new Vector3I(x, 2, 0), BlockCatalog.LightArmor, Basis.Identity);
		bp.Add(new Vector3I(0, 3, 0), BlockCatalog.Battery, Basis.Identity);
		bp.Add(new Vector3I(0, 4, 0), BlockCatalog.Cockpit, Basis.Identity);
		return bp;
	}

	/// <summary>Small ship with thrust on all six axes, a gyroscope, a battery, and a core facing -Z.</summary>
	public static Blueprint StarterHauler()
	{
		var bp = new Blueprint { Name = "Starter Hauler" };
		bp.Add(new Vector3I(0, 0, 0), BlockCatalog.Cockpit, Basis.Identity);
		bp.Add(new Vector3I(0, 0, 1), BlockCatalog.Gyroscope, Basis.Identity);
		bp.Add(new Vector3I(0, 0, 2), BlockCatalog.LightArmor, Basis.Identity);
		bp.Add(new Vector3I(-1, 0, 1), BlockCatalog.LightArmor, Basis.Identity);
		bp.Add(new Vector3I(1, 0, 1), BlockCatalog.LightArmor, Basis.Identity);
		bp.Add(new Vector3I(0, 0, 3), BlockCatalog.Thruster, PushForward);
		bp.Add(new Vector3I(-1, 0, 0), BlockCatalog.Thruster, PushBack);
		bp.Add(new Vector3I(1, 0, 0), BlockCatalog.Thruster, PushBack);
		bp.Add(new Vector3I(-2, 0, 1), BlockCatalog.Thruster, PushRight);
		bp.Add(new Vector3I(2, 0, 1), BlockCatalog.Thruster, PushLeft);
		bp.Add(new Vector3I(0, 1, 1), BlockCatalog.Thruster, PushDown);
		bp.Add(new Vector3I(0, -1, 1), BlockCatalog.Thruster, PushUp);
		bp.Add(new Vector3I(0, 1, 2), BlockCatalog.Battery, Basis.Identity);
		return bp;
	}

	/// <summary>Minimal flyer: core, battery, gyro, solar roof, and one thruster per direction.</summary>
	public static Blueprint ScoutDrone()
	{
		var bp = new Blueprint { Name = "Scout Drone" };
		bp.Add(new Vector3I(0, 0, 0), BlockCatalog.Cockpit, Basis.Identity);
		bp.Add(new Vector3I(0, 0, 1), BlockCatalog.Battery, Basis.Identity);
		bp.Add(new Vector3I(0, 0, 2), BlockCatalog.Gyroscope, Basis.Identity);
		bp.Add(new Vector3I(0, 1, 1), BlockCatalog.SolarPanel, Basis.Identity);
		bp.Add(new Vector3I(0, 0, 3), BlockCatalog.Thruster, PushForward);
		bp.Add(new Vector3I(0, -1, 0), BlockCatalog.Thruster, PushBack);
		bp.Add(new Vector3I(-1, 0, 1), BlockCatalog.Thruster, PushRight);
		bp.Add(new Vector3I(1, 0, 1), BlockCatalog.Thruster, PushLeft);
		bp.Add(new Vector3I(0, 1, 2), BlockCatalog.Thruster, PushDown);
		bp.Add(new Vector3I(0, -1, 2), BlockCatalog.Thruster, PushUp);
		return bp;
	}

	/// <summary>Static base: refinery, fabricator, cargo, battery, and solar panels facing +X.</summary>
	public static Blueprint Outpost()
	{
		var bp = new Blueprint { Name = "Outpost", Kind = DesignKind.Station };
		for (int x = -1; x <= 1; x++)
			for (int z = -1; z <= 1; z++)
				bp.Add(new Vector3I(x, 0, z), BlockCatalog.LightArmor, Basis.Identity);
		bp.Add(new Vector3I(-1, 1, 0), BlockCatalog.Battery, Basis.Identity);
		bp.Add(new Vector3I(0, 1, 0), BlockCatalog.Refinery, Basis.Identity);
		bp.Add(new Vector3I(1, 1, 0), BlockCatalog.CargoContainer, Basis.Identity);
		// Prints out of its -Z face, away from the rest of the base.
		bp.Add(new Vector3I(0, 1, -1), BlockCatalog.Fabricator, Basis.Identity);
		bp.Add(new Vector3I(-1, 1, -1), BlockCatalog.Uplink, Basis.Identity);
		// Panels collect on their local +Y; turn that towards +X.
		var sunward = new Basis(Vector3.Back, -Mathf.Pi / 2f);
		for (int z = -1; z <= 1; z++)
			bp.Add(new Vector3I(2, 0, z), BlockCatalog.SolarPanel, sunward);
		return bp;
	}

	/// <summary>
	/// A small automated mine to set down on rock: drill pointing down, ore by tube into a refinery,
	/// ingots on to cargo, and a fabricator next to the cargo. Solar roof and a battery for power.
	/// </summary>
	public static Blueprint MiningRig()
	{
		var bp = new Blueprint { Name = "Mining Rig", Kind = DesignKind.Station };
		bp.Add(new Vector3I(0, 0, 0), BlockCatalog.AutoDrill, PushDown);   // drill face points along -Y
		bp.Add(new Vector3I(0, 1, 0), BlockCatalog.Tube, Basis.Identity);
		bp.Add(new Vector3I(1, 1, 0), BlockCatalog.Tube, Basis.Identity);
		bp.Add(new Vector3I(2, 1, 0), BlockCatalog.Refinery, Basis.Identity);
		bp.Add(new Vector3I(3, 1, 0), BlockCatalog.Tube, Basis.Identity);
		bp.Add(new Vector3I(4, 1, 0), BlockCatalog.CargoContainer, Basis.Identity);
		bp.Add(new Vector3I(4, 1, -1), BlockCatalog.Fabricator, Basis.Identity);
		bp.Add(new Vector3I(1, 2, 0), BlockCatalog.Battery, Basis.Identity);
		foreach (int x in new[] { 0, 2, 3, 4 })
			bp.Add(new Vector3I(x, 2, 0), BlockCatalog.SolarPanel, Basis.Identity);
		return bp;
	}

	/// <summary>
	/// The smallest self-running mine, meant to be set down by bots: a drill pointing down, a refinery
	/// on top of it and storage beside it (touching blocks need no tubes), under a solar roof.
	/// </summary>
	public static Blueprint DrillSite()
	{
		var bp = new Blueprint { Name = "Drill Site", Kind = DesignKind.Station };
		bp.Add(new Vector3I(0, 0, 0), BlockCatalog.AutoDrill, PushDown);
		bp.Add(new Vector3I(0, 1, 0), BlockCatalog.Refinery, Basis.Identity);
		bp.Add(new Vector3I(1, 1, 0), BlockCatalog.CargoContainer, Basis.Identity);
		foreach (var cell in new[] { new Vector3I(0, 2, 0), new Vector3I(1, 2, 0), new Vector3I(0, 2, 1), new Vector3I(1, 2, 1) })
			bp.Add(cell, BlockCatalog.SolarPanel, Basis.Identity);
		return bp;
	}

	/// <summary>A flying crate with a face: Bot Core on top of a cargo container, so it hauls plenty.</summary>
	public static Blueprint WorkerBot()
	{
		var bp = new Blueprint { Name = "Worker Bot", Kind = DesignKind.Bot };
		bp.Add(new Vector3I(0, 1, 0), BlockCatalog.BotCore, Basis.Identity);
		bp.Add(new Vector3I(0, 0, 0), BlockCatalog.CargoContainer, Basis.Identity, Palette.Orange);
		return bp;
	}

	/// <summary>Drill pointing down feeding an Air Processor on top: stone in, air out. Solar ring around.</summary>
	public static Blueprint AirMaker() => DrillFed("Air Maker", BlockCatalog.AirProcessor);

	/// <summary>Drill pointing down feeding a Seed Garden: soil for a planet whose air and seas are back.</summary>
	public static Blueprint Garden() => DrillFed("Garden", BlockCatalog.SeedGarden);

	private static Blueprint DrillFed(string name, BlockDefinition machine)
	{
		var bp = new Blueprint { Name = name, Kind = DesignKind.Station };
		bp.Add(new Vector3I(0, 0, 0), BlockCatalog.AutoDrill, PushDown);
		bp.Add(new Vector3I(0, 1, 0), machine, Basis.Identity);
		foreach (var cell in new[] { new Vector3I(1, 1, 0), new Vector3I(-1, 1, 0), new Vector3I(0, 1, 1), new Vector3I(0, 1, -1), new Vector3I(1, 1, 1), new Vector3I(-1, 1, -1) })
			bp.Add(cell, BlockCatalog.SolarPanel, Basis.Identity);
		return bp;
	}

	/// <summary>A Hydrator with storage beside it: ice arrives by bot route (from Frost) and melts into the seas.</summary>
	public static Blueprint WaterWorks()
	{
		var bp = new Blueprint { Name = "Water Works", Kind = DesignKind.Station };
		bp.Add(new Vector3I(0, 0, 0), BlockCatalog.Hydrator, Basis.Identity);
		bp.Add(new Vector3I(1, 0, 0), BlockCatalog.CargoContainer, Basis.Identity);
		foreach (var cell in new[] { new Vector3I(-1, 0, 0), new Vector3I(0, 0, 1), new Vector3I(0, 0, -1), new Vector3I(1, 0, 1), new Vector3I(1, 0, -1), new Vector3I(2, 0, 0) })
			bp.Add(cell, BlockCatalog.SolarPanel, Basis.Identity);
		return bp;
	}

	/// <summary>An Incubator with a depot for what the villagers ask for, under a solar roof.</summary>
	public static Blueprint SettlementSeed()
	{
		var bp = new Blueprint { Name = "Settlement Seed", Kind = DesignKind.Station };
		bp.Add(new Vector3I(0, 0, 0), BlockCatalog.Incubator, Basis.Identity);
		bp.Add(new Vector3I(1, 0, 0), BlockCatalog.CargoContainer, Basis.Identity, Palette.Mustard);
		foreach (var cell in new[] { new Vector3I(1, 1, 0), new Vector3I(-1, 0, 0), new Vector3I(0, 0, 1), new Vector3I(1, 0, 1) })
			bp.Add(cell, BlockCatalog.SolarPanel, Basis.Identity);
		return bp;
	}

	/// <summary>The Breach Lance on a sturdy base with batteries: build it where it can see the sky.</summary>
	public static Blueprint BreachLanceSite()
	{
		var bp = new Blueprint { Name = "Breach Lance", Kind = DesignKind.Station };
		for (int x = -1; x <= 1; x++)
			for (int z = -1; z <= 1; z++)
				bp.Add(new Vector3I(x, 0, z), BlockCatalog.HeavyArmor, Basis.Identity);
		bp.Add(new Vector3I(0, 1, 0), BlockCatalog.BreachLance, Basis.Identity);
		bp.Add(new Vector3I(-1, 1, -1), BlockCatalog.Battery, Basis.Identity);
		bp.Add(new Vector3I(1, 1, 1), BlockCatalog.Battery, Basis.Identity);
		return bp;
	}

	/// <summary>An Uplink on a small base: what bots set down to light up a dark planet for the Nexus.</summary>
	public static Blueprint UplinkPost()
	{
		var bp = new Blueprint { Name = "Uplink Post", Kind = DesignKind.Station };
		bp.Add(new Vector3I(0, 0, 0), BlockCatalog.LightArmor, Basis.Identity);
		bp.Add(new Vector3I(0, 1, 0), BlockCatalog.Uplink, Basis.Identity);
		return bp;
	}
}
