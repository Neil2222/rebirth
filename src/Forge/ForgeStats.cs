using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Rebirth.Building;
using Rebirth.Items;
using Rebirth.Persistence;

namespace Rebirth.Forge;

/// <summary>What a design will do once printed: the live readout on the Forge's right panel.</summary>
public static class ForgeStats
{
	public static string Describe(BlockGrid design, string name)
	{
		if (design.BlockCount == 0)
			return "EMPTY DESIGN\n\nClick in the hangar to\nplace the first block.";

		var blocks = design.Blocks.ToList();
		var sb = new StringBuilder();

		Vector3I min = blocks[0].Key, max = blocks[0].Key;
		foreach (var (cell, _) in blocks)
		{
			min = min.Min(cell);
			max = max.Max(cell);
		}
		Vector3I size = max - min + Vector3I.One;
		float mass = design.Mass;
		sb.AppendLine("STRUCTURE");
		sb.AppendLine($"  Blocks      {blocks.Count}");
		sb.AppendLine($"  Mass        {mass / 1000f:0.0} t");
		sb.AppendLine($"  Size        {size.X}×{size.Y}×{size.Z}  ({size.X * BlockGrid.CellSize:0}×{size.Y * BlockGrid.CellSize:0}×{size.Z * BlockGrid.CellSize:0} m)");

		// Thrust is judged from the pilot's seat: the first control core, else the grid's own axes.
		PlacedBlock? core = blocks.Where(b => b.Value.Definition.Kind == BlockKind.Cockpit).Select(b => (PlacedBlock?)b.Value).FirstOrDefault();
		Basis frame = core?.Orientation ?? Basis.Identity;
		float Thrust(Vector3 dir) => design.ThrustCapacity(BlockGrid.DominantAxis(frame * dir));
		float forward = Thrust(Vector3.Forward);
		sb.AppendLine();
		sb.AppendLine("PROPULSION  (kN)");
		sb.AppendLine($"  Fwd {Thrust(Vector3.Forward) / 1000f,5:0}   Back {Thrust(Vector3.Back) / 1000f,5:0}");
		sb.AppendLine($"  Up  {Thrust(Vector3.Up) / 1000f,5:0}   Down {Thrust(Vector3.Down) / 1000f,5:0}");
		sb.AppendLine($"  Left{Thrust(Vector3.Left) / 1000f,5:0}   Right{Thrust(Vector3.Right) / 1000f,5:0}");
		sb.AppendLine($"  Acceleration {forward / mass:0.0} m/s² ({forward / mass / 9.81f:0.00} g)");
		sb.AppendLine($"  Gyro torque  {design.GyroTorque / 1e6f:0.0} MN·m");

		float solar = 0f, battery = 0f, draw = 0f, cargo = 0f;
		foreach (var (_, block) in blocks)
		{
			var def = block.Definition;
			solar += def.SolarOutput;
			battery += def.BatteryCapacity;
			draw += def.PowerDraw;
			cargo += def.CargoCapacity;
		}
		sb.AppendLine();
		sb.AppendLine("POWER");
		sb.AppendLine($"  Solar (max)  {solar:0.00} MW");
		sb.AppendLine($"  Battery      {battery:0.0} MWh");
		sb.AppendLine($"  Peak draw    {draw:0.00} MW");
		if (cargo > 0f)
			sb.AppendLine($"  Cargo        {cargo / 1000f:0} t");

		sb.AppendLine();
		sb.AppendLine("COST");
		foreach (var (item, amount) in Blueprint.FromGrid(design, name).TotalCost().OrderBy(kv => kv.Key))
			sb.AppendLine($"  {ItemCatalog.DisplayName(item),-14}{amount,6:0} kg");

		var warnings = Warnings(design, blocks, core is not null, forward, Thrust(Vector3.Back), solar, battery, draw);
		if (warnings.Count > 0)
		{
			sb.AppendLine();
			sb.AppendLine("WARNINGS");
			foreach (string warning in warnings)
				sb.AppendLine($"  ! {warning}");
		}
		// AppendLine writes \r\n on Windows; a Label shows the \r as an extra blank line.
		return sb.ToString().Replace("\r\n", "\n");
	}

	private static List<string> Warnings(BlockGrid design, List<KeyValuePair<Vector3I, PlacedBlock>> blocks, bool hasCore,
		float forward, float back, float solar, float battery, float draw)
	{
		var warnings = new List<string>();
		int pieces = design.PieceCount;
		if (pieces > 1)
			warnings.Add($"{pieces} loose parts: they fall apart when printed");
		bool flies = blocks.Any(b => b.Value.Definition.Thrust > 0f);
		if (flies && !hasCore)
			warnings.Add("No Control Core: can't be piloted");
		if (flies && design.GyroTorque <= 0f)
			warnings.Add("No gyroscope: can't turn");
		if (flies && forward > 0f && back <= 0f)
			warnings.Add("No reverse thrust: can't brake");
		if (draw > 0f && solar <= 0f && battery <= 0f)
			warnings.Add("Needs power but has no battery or solar");
		return warnings;
	}
}
