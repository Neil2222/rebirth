using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Rebirth.Building;

namespace Rebirth.Persistence;

/// <summary>What a design is for: printed as a ship, printed as a station, or worn as a robot body.</summary>
public enum DesignKind { Ship, Station, Body }

/// <summary>
/// A grid design: which block sits in which cell, how it is turned, and its paint. Saves reuse it
/// with the per-block runtime state filled in.
/// </summary>
public sealed class Blueprint
{
	public const int CurrentVersion = 1;

	public int Version { get; set; } = CurrentVersion;
	public string Name { get; set; } = "Untitled";
	public DesignKind Kind { get; set; } = DesignKind.Ship;
	public List<BlueprintBlock> Blocks { get; set; } = new();

	public static readonly JsonSerializerOptions Json = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters = { new JsonStringEnumConverter() },
	};

	public void Add(Vector3I cell, BlockDefinition block, Basis orientation, Color? paint = null) =>
		Blocks.Add(BlueprintBlock.From(cell, new PlacedBlock(block, orientation, paint ?? block.Paint), state: null));

	/// <param name="includeState">Also record integrity and battery charge (for saves, not designs).</param>
	public static Blueprint FromGrid(BlockGrid grid, string name, bool includeState = false, DesignKind kind = DesignKind.Ship)
	{
		var blueprint = new Blueprint { Name = name, Kind = kind };
		foreach (var (cell, block) in grid.Blocks)
			blueprint.Blocks.Add(BlueprintBlock.From(cell, block, includeState ? grid.StateOf(cell) : null));
		return blueprint;
	}

	/// <summary>Adds every block to <paramref name="grid"/>; blocks without saved state start intact.</summary>
	/// <param name="charge">Battery charge (fraction) for blocks without saved state.</param>
	public void BuildInto(BlockGrid grid, float charge = 1f)
	{
		var blocks = new List<(Vector3I, PlacedBlock, BlockState)>(Blocks.Count);
		foreach (var entry in Blocks)
		{
			var definition = BlockCatalog.Get(entry.Id);
			var block = new PlacedBlock(definition, entry.Orientation(), entry.PaintColor(definition));
			var state = BlockState.Fresh(definition, charge);
			state.Integrity = entry.Integrity ?? definition.MaxIntegrity;
			state.StoredEnergy = entry.Energy ?? state.StoredEnergy;
			blocks.Add((entry.CellVector(), block, state));
		}
		grid.AddMany(blocks);
	}

	public string ToJson() => JsonSerializer.Serialize(this, Json);

	public static Blueprint FromJson(string json) =>
		JsonSerializer.Deserialize<Blueprint>(json, Json) ?? throw new JsonException("Empty blueprint");

	public float TotalMass() => Blocks.Sum(b => BlockCatalog.Get(b.Id).Mass);

	/// <summary>Space the design occupies, in metres, relative to cell (0,0,0)'s center.</summary>
	public Aabb Bounds()
	{
		if (Blocks.Count == 0)
			return new Aabb();
		Vector3I min = Blocks[0].CellVector(), max = min;
		foreach (var block in Blocks)
		{
			min = min.Min(block.CellVector());
			max = max.Max(block.CellVector());
		}
		Vector3 half = Vector3.One * (BlockGrid.CellSize * 0.5f);
		return new Aabb(BlockGrid.CellCenter(min) - half, (Vector3)(max - min + Vector3I.One) * BlockGrid.CellSize);
	}

	/// <summary>Total ingots needed to print this design.</summary>
	public Dictionary<string, float> TotalCost()
	{
		var cost = new Dictionary<string, float>();
		foreach (var entry in Blocks)
			foreach (var (item, amount) in BlockCatalog.Get(entry.Id).Cost)
				cost[item] = cost.GetValueOrDefault(item) + amount;
		return cost;
	}
}

public sealed class BlueprintBlock
{
	public string Id { get; set; } = "";
	/// <summary>Cell as [x, y, z].</summary>
	public int[] Cell { get; set; } = [0, 0, 0];
	/// <summary>Orientation basis columns X, Y, Z flattened; entries are -1, 0 or 1.</summary>
	public int[] Rotation { get; set; } = [1, 0, 0, 0, 1, 0, 0, 0, 1];
	/// <summary>Paint as RRGGBB hex; omitted when the block type's default is used.</summary>
	public string? Paint { get; set; }
	public float? Integrity { get; set; }
	public float? Energy { get; set; }

	public static BlueprintBlock From(Vector3I cell, PlacedBlock block, BlockState? state)
	{
		Basis b = block.Orientation;
		return new BlueprintBlock
		{
			Id = block.Definition.Id,
			Cell = [cell.X, cell.Y, cell.Z],
			Rotation =
			[
				Mathf.RoundToInt(b.X.X), Mathf.RoundToInt(b.X.Y), Mathf.RoundToInt(b.X.Z),
				Mathf.RoundToInt(b.Y.X), Mathf.RoundToInt(b.Y.Y), Mathf.RoundToInt(b.Y.Z),
				Mathf.RoundToInt(b.Z.X), Mathf.RoundToInt(b.Z.Y), Mathf.RoundToInt(b.Z.Z),
			],
			Paint = block.Paint == block.Definition.Paint ? null : block.Paint.ToHtml(includeAlpha: false),
			Integrity = state?.Integrity,
			Energy = state is not null && block.Definition.BatteryCapacity > 0f ? state.StoredEnergy : null,
		};
	}

	public Vector3I CellVector() => new(Cell[0], Cell[1], Cell[2]);

	public Basis Orientation() => new(
		new Vector3(Rotation[0], Rotation[1], Rotation[2]),
		new Vector3(Rotation[3], Rotation[4], Rotation[5]),
		new Vector3(Rotation[6], Rotation[7], Rotation[8]));

	public Color PaintColor(BlockDefinition definition) =>
		Paint is null ? definition.Paint : Color.FromHtml(Paint);
}
