using System.Collections.Generic;

namespace Driftworks.Items;

public static class ItemCatalog
{
	private static readonly Dictionary<string, string> Names = new()
	{
		["stone"] = "Stone",
		["iron_ore"] = "Iron Ore",
		["nickel_ore"] = "Nickel Ore",
		["silicon_ore"] = "Silicon Ore",
	};

	public static string DisplayName(string id) => Names.GetValueOrDefault(id, id);
}
