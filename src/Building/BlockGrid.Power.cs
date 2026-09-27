using System.Collections.Generic;
using System.Linq;
using Rebirth.Items;
using Rebirth.World;
using Godot;

namespace Rebirth.Building;

// Electrical network and shared storage. Power reaches every block on a grid without wiring, and
// stations with Power Pylons within reach of each other share one network (see PowerNet). Items only
// move between machines and storage through the logistics network (BlockGrid.Logistics.cs).
public partial class BlockGrid
{
	/// <summary>Metres between two pylons (on different stations) for a cable to join them.</summary>
	public const float PylonReach = 90f;
	/// <summary>Kilograms of stone a Stone Burner eats per second at full load.</summary>
	public const float BurnerStonePerSecond = 2f;

	/// <summary>Shared storage of all cargo containers on this grid.</summary>
	public Inventory Inventory { get; } = new() { Capacity = 0f };

	/// <summary>Fraction (0..1) of requested power that was delivered last tick; consumers scale by it.</summary>
	public float PowerSatisfaction { get; private set; } = 1f;
	public float PowerDemand { get; private set; }
	public float PowerDelivered { get; private set; }
	public float SolarProduction { get; private set; }
	/// <summary>Megawatts from burners, turbines and taps on this grid.</summary>
	public float GeneratorProduction { get; private set; }
	public float StoredEnergy { get; private set; }
	public float EnergyCapacity { get; private set; }

	/// <summary>The whole network this grid is cabled into (just itself when it has no linked pylon).</summary>
	public PowerNet.Summary Network { get; private set; }

	/// <summary>Set by the colony: how much air the planet under this station has (0..1); turbines need wind.</summary>
	public float WindLevel { get; set; }
	/// <summary>Set by the colony: geothermal taps make this much of their output (0 off a planet).</summary>
	public float HeatLevel { get; set; }

	private readonly List<Vector3I> _batteries = new();
	private readonly List<Vector3I> _solarPanels = new();
	private readonly List<Vector3I> _generators = new();
	private readonly List<Vector3I> _pylons = new();
	private float _refineryDraw;
	private float _gyroDraw;

	// This tick's own figures, balanced alone or together with the rest of the network.
	private float _solarNow, _generationNow, _demandNow, _batteryOutputNow;
	/// <summary>Share (0..1) of the generators' output that was used last tick: burners burn fuel by it.</summary>
	private float _generatorLoad;

	/// <summary>Pylon cells on this grid (a station without any stands alone).</summary>
	public IReadOnlyList<Vector3I> Pylons => _pylons;

	/// <summary>Where cables hang from a pylon: the top of its mast.</summary>
	public Vector3 PylonTop(Vector3I cell) =>
		GlobalTransform * (CellCenter(cell) + _blocks[cell].Orientation * (Vector3.Up * (CellSize * 0.5f + 3.1f)));

	/// <summary>Where a cable plugs into a station without a pylon: on top of its highest block.</summary>
	public Vector3 RoofPoint()
	{
		var top = _blocks.Keys.OrderByDescending(c => c.Y).First();
		return GlobalTransform * (CellCenter(top) + Vector3.Up * CellSize * 0.5f);
	}

	public Vector3 PylonUp(Vector3I cell) => GlobalBasis * (_blocks[cell].Orientation * Vector3.Up);

	private void RebuildPowerAndCargo()
	{
		_batteries.Clear();
		_solarPanels.Clear();
		_generators.Clear();
		_pylons.Clear();
		_refineryDraw = 0f;
		_gyroDraw = 0f;
		float cargo = 0f;
		foreach (var (cell, block) in _blocks)
		{
			var def = block.Definition;
			if (def.BatteryCapacity > 0f)
				_batteries.Add(cell);
			if (def.SolarOutput > 0f)
				_solarPanels.Add(cell);
			if (def.GeneratorOutput > 0f)
				_generators.Add(cell);
			if (def.Kind == BlockKind.PowerPylon)
				_pylons.Add(cell);
			if (def.Kind == BlockKind.Refinery)
				_refineryDraw += def.PowerDraw;
			// Always-on consumers: gyroscopes and firewalls.
			if (def.Kind is BlockKind.Gyroscope or BlockKind.Firewall)
				_gyroDraw += def.PowerDraw;
			cargo += def.CargoCapacity;
		}
		Inventory.Capacity = cargo;
		PowerNet.Register(this, _pylons.Count > 0 && !DesignMode);
	}

	private void UpdatePower(float dt)
	{
		MeasurePower(dt);
		// Cabled stations are balanced together, once per tick, by whichever of them gets here first.
		if (PowerNet.Tick(this, dt))
			return;
		Balance([this], dt);
	}

	/// <summary>What this grid makes, wants and could draw from its batteries this tick.</summary>
	private void MeasurePower(float dt)
	{
		float hours = dt / 3600f;
		float solar = 0f;
		foreach (var cell in _solarPanels)
		{
			var block = _blocks[cell];
			Vector3 up = GlobalBasis * (block.Orientation * Vector3.Up);
			solar += block.Definition.SolarOutput * Sun.Strength * Mathf.Max(0f, up.Dot(Sun.Direction));
		}

		float generated = 0f;
		foreach (var cell in _generators)
		{
			var def = _blocks[cell].Definition;
			var (output, status) = def.Kind switch
			{
				BlockKind.StoneBurner => BurnStone(cell, def, dt),
				BlockKind.WindTurbine => (def.GeneratorOutput * WindLevel,
					WindLevel <= 0.01f ? "No wind: needs a planet with air" : $"Spinning: {WindLevel:P0} wind"),
				BlockKind.GeothermalTap => (def.GeneratorOutput * HeatLevel,
					HeatLevel <= 0f ? "Needs to stand on a planet" : "Drawing heat"),
				_ => (0f, ""),
			};
			generated += output;
			_machineStatus[cell] = status;
		}

		float demand = _activeRefineryDraw + _fabricatorDraw + _drillDraw + _terraformDraw;
		if (!Freeze)
		{
			demand += _gyroDraw;
			foreach (var (cell, dir) in _thrusters)
				demand += _blocks[cell].Definition.PowerDraw * _throttle[dir];
		}

		float batteryOutput = 0f;
		foreach (var cell in _batteries)
			batteryOutput += Mathf.Min(_blocks[cell].Definition.BatteryMaxPower, _state[cell].StoredEnergy / hours);

		_solarNow = solar;
		_generationNow = solar + generated;
		GeneratorProduction = generated;
		_demandNow = demand;
		_batteryOutputNow = batteryOutput;
	}

	/// <summary>A burner with stone makes full power, and eats stone as much as its power is used.</summary>
	private (float Output, string Status) BurnStone(Vector3I cell, BlockDefinition def, float dt)
	{
		var input = _state[cell].Input!;
		float have = input.Get("stone");
		if (have <= 0.01f)
			return (0f, "Waiting for stone");
		float burn = Mathf.Min(have, BurnerStonePerSecond * _generatorLoad * dt);
		if (burn > 0f)
		{
			input.TryRemove("stone", burn);
			Items.ProductionStats.Consumed("stone", burn);
		}
		return (def.GeneratorOutput, _generatorLoad > 0.01f ? $"Burning stone ({_generatorLoad:P0} load)" : "Ready: nothing needs power");
	}

	/// <summary>
	/// Shares power across <paramref name="grids"/> (one station, or a cabled network): everyone gets the
	/// same fraction of what they asked for; generators go first, batteries cover the rest and soak up
	/// what is left over.
	/// </summary>
	internal static void Balance(IReadOnlyList<BlockGrid> grids, float dt)
	{
		float hours = dt / 3600f;
		float generation = 0f, demand = 0f, batteryOutput = 0f;
		bool consumers = false;
		foreach (var grid in grids)
		{
			generation += grid._generationNow;
			demand += grid._demandNow;
			batteryOutput += grid._batteryOutputNow;
			consumers |= grid.HasConsumers();
		}
		float supply = generation + batteryOutput;
		float delivered = Mathf.Min(demand, supply);
		float satisfaction = demand > 1e-6f ? delivered / demand : (supply > 0f || !consumers ? 1f : 0f);

		float fromBatteries = Mathf.Max(0f, delivered - generation);
		float surplus = Mathf.Max(0f, generation - delivered);
		float charged = 0f, stored = 0f, capacity = 0f;
		foreach (var grid in grids)
		{
			grid.StoredEnergy = 0f;
			grid.EnergyCapacity = 0f;
			foreach (var cell in grid._batteries)
			{
				var def = grid._blocks[cell].Definition;
				var state = grid._state[cell];
				if (fromBatteries > 0f && batteryOutput > 0f)
				{
					float share = Mathf.Min(def.BatteryMaxPower, state.StoredEnergy / hours) / batteryOutput;
					state.StoredEnergy = Mathf.Max(0f, state.StoredEnergy - fromBatteries * share * hours);
				}
				else if (surplus > 0f)
				{
					float charge = Mathf.Min(Mathf.Min(surplus, def.BatteryMaxPower) * hours, def.BatteryCapacity - state.StoredEnergy);
					state.StoredEnergy += charge;
					surplus -= charge / hours;
					charged += charge / hours;
				}
				grid.StoredEnergy += state.StoredEnergy;
				grid.EnergyCapacity += def.BatteryCapacity;
			}
			stored += grid.StoredEnergy;
			capacity += grid.EnergyCapacity;
		}

		float load = generation > 1e-6f ? Mathf.Clamp((Mathf.Min(generation, delivered) + charged) / generation, 0f, 1f) : 0f;
		var summary = new PowerNet.Summary(grids.Count, generation, demand, stored, capacity);
		foreach (var grid in grids)
		{
			grid.PowerSatisfaction = satisfaction;
			grid.SolarProduction = grid._solarNow;
			grid.PowerDemand = grid._demandNow;
			grid.PowerDelivered = grid._demandNow * satisfaction;
			grid._generatorLoad = load;
			grid.Network = summary;
		}
	}

	private bool HasConsumers() => _thrusters.Count > 0 || _gyroDraw > 0f || _refineryDraw > 0f || _drillDraw > 0f || _terraformDraw > 0f || _fabricatorQueues.Values.Any(q => q.Count > 0);
}
