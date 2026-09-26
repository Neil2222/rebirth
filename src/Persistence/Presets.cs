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

	public static IReadOnlyList<Blueprint> All => [Custodian(), StarterHauler(), ScoutDrone(), Outpost()];

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
		// Panels collect on their local +Y; turn that towards +X.
		var sunward = new Basis(Vector3.Back, -Mathf.Pi / 2f);
		for (int z = -1; z <= 1; z++)
			bp.Add(new Vector3I(2, 0, z), BlockCatalog.SolarPanel, sunward);
		return bp;
	}
}
