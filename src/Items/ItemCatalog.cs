using System.Collections.Generic;

namespace Driftworks.Items;

public enum ItemCategory { Ore, Ingot }

public sealed record ItemDefinition(string Id, string DisplayName, ItemCategory Category);

public static class ItemCatalog
{
	private static readonly Dictionary<string, ItemDefinition> Items = new()
	{
		["stone"] = new("stone", "Stone", ItemCategory.Ore),
		["iron_ore"] = new("iron_ore", "Iron Ore", ItemCategory.Ore),
		["nickel_ore"] = new("nickel_ore", "Nickel Ore", ItemCategory.Ore),
		["silicon_ore"] = new("silicon_ore", "Silicon Ore", ItemCategory.Ore),
		["iron_ingot"] = new("iron_ingot", "Iron Ingot", ItemCategory.Ingot),
		["nickel_ingot"] = new("nickel_ingot", "Nickel Ingot", ItemCategory.Ingot),
		["silicon_wafer"] = new("silicon_wafer", "Silicon Wafer", ItemCategory.Ingot),
	};

	public static ItemDefinition Get(string id) => Items[id];

	public static string DisplayName(string id) => Items.TryGetValue(id, out var item) ? item.DisplayName : id;

	/// <summary>
	/// Refinery output per kg of ore. Stone is mostly slag but still yields a little of everything,
	/// like gravel does in Space Engineers.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>> Refining =
		new Dictionary<string, IReadOnlyDictionary<string, float>>
		{
			["iron_ore"] = new Dictionary<string, float> { ["iron_ingot"] = 0.7f },
			["nickel_ore"] = new Dictionary<string, float> { ["nickel_ingot"] = 0.4f },
			["silicon_ore"] = new Dictionary<string, float> { ["silicon_wafer"] = 0.7f },
			["stone"] = new Dictionary<string, float> { ["iron_ingot"] = 0.05f, ["nickel_ingot"] = 0.01f, ["silicon_wafer"] = 0.01f },
		};
}
