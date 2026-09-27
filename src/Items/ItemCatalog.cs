using System.Collections.Generic;
using Godot;

namespace Rebirth.Items;

public enum ItemCategory { Ore, Ingot }

/// <param name="Color">Colour of the parcels that carry this item through tubes.</param>
public sealed record ItemDefinition(string Id, string DisplayName, ItemCategory Category, Color Color);

public static class ItemCatalog
{
	private static readonly Dictionary<string, ItemDefinition> Items = new()
	{
		["stone"] = new("stone", "Stone", ItemCategory.Ore, new Color(0.66f, 0.63f, 0.66f)),
		["iron_ore"] = new("iron_ore", "Iron Ore", ItemCategory.Ore, new Color(0.88f, 0.46f, 0.30f)),
		["nickel_ore"] = new("nickel_ore", "Nickel Ore", ItemCategory.Ore, new Color(0.50f, 0.80f, 0.64f)),
		["silicon_ore"] = new("silicon_ore", "Silicon Ore", ItemCategory.Ore, new Color(0.86f, 0.84f, 0.96f)),
		["iron_ingot"] = new("iron_ingot", "Iron Ingot", ItemCategory.Ingot, new Color(0.82f, 0.82f, 0.86f)),
		["nickel_ingot"] = new("nickel_ingot", "Nickel Ingot", ItemCategory.Ingot, new Color(0.66f, 0.86f, 0.74f)),
		["silicon_wafer"] = new("silicon_wafer", "Silicon Wafer", ItemCategory.Ingot, new Color(0.45f, 0.52f, 0.9f)),
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
			["stone"] = new Dictionary<string, float> { ["iron_ingot"] = 0.1f, ["nickel_ingot"] = 0.03f, ["silicon_wafer"] = 0.04f },
		};
}
