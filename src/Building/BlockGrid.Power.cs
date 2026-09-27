using System.Collections.Generic;
using System.Linq;
using Rebirth.Items;
using Rebirth.World;
using Godot;

namespace Rebirth.Building;

// Electrical network and shared storage. Power reaches every block on a grid without wiring; items
// only move between machines and storage through the logistics network (BlockGrid.Logistics.cs).
public partial class BlockGrid
{
	/// <summary>Shared storage of all cargo containers on this grid.</summary>
	public Inventory Inventory { get; } = new() { Capacity = 0f };

	/// <summary>Fraction (0..1) of requested power that was delivered last tick; consumers scale by it.</summary>
	public float PowerSatisfaction { get; private set; } = 1f;
	public float PowerDemand { get; private set; }
	public float PowerDelivered { get; private set; }
	public float SolarProduction { get; private set; }
	public float StoredEnergy { get; private set; }
	public float EnergyCapacity { get; private set; }

	private readonly List<Vector3I> _batteries = new();
	private readonly List<Vector3I> _solarPanels = new();
	private float _refineryDraw;
	private float _gyroDraw;

	private void RebuildPowerAndCargo()
	{
		_batteries.Clear();
		_solarPanels.Clear();
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
			if (def.Kind == BlockKind.Refinery)
				_refineryDraw += def.PowerDraw;
			// Always-on consumers: gyroscopes and firewalls.
			if (def.Kind is BlockKind.Gyroscope or BlockKind.Firewall)
				_gyroDraw += def.PowerDraw;
			cargo += def.CargoCapacity;
		}
		Inventory.Capacity = cargo;
	}

	private void UpdatePower(float dt)
	{
		float hours = dt / 3600f;

		float solar = 0f;
		foreach (var cell in _solarPanels)
		{
			var block = _blocks[cell];
			Vector3 up = GlobalBasis * (block.Orientation * Vector3.Up);
			solar += block.Definition.SolarOutput * Sun.Strength * Mathf.Max(0f, up.Dot(Sun.Direction));
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

		float supply = solar + batteryOutput;
		float delivered = Mathf.Min(demand, supply);
		PowerSatisfaction = demand > 1e-6f ? delivered / demand : (supply > 0f || !HasConsumers() ? 1f : 0f);

		// Solar goes first; batteries cover the rest in proportion to what each can deliver.
		float fromBatteries = Mathf.Max(0f, delivered - solar);
		float surplus = Mathf.Max(0f, solar - delivered);
		StoredEnergy = 0f;
		EnergyCapacity = 0f;
		foreach (var cell in _batteries)
		{
			var def = _blocks[cell].Definition;
			var state = _state[cell];
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
			}
			StoredEnergy += state.StoredEnergy;
			EnergyCapacity += def.BatteryCapacity;
		}

		SolarProduction = solar;
		PowerDemand = demand;
		PowerDelivered = delivered;
	}

	private bool HasConsumers() => _thrusters.Count > 0 || _gyroDraw > 0f || _refineryDraw > 0f || _drillDraw > 0f || _terraformDraw > 0f || _fabricatorQueues.Values.Any(q => q.Count > 0);
}
