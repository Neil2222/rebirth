using System.Collections.Generic;

namespace Driftworks.Items;

/// <summary>Item amounts by id. Ores and ingots are counted in kilograms.</summary>
public sealed class Inventory
{
	private readonly Dictionary<string, float> _items = new();

	public IReadOnlyDictionary<string, float> Items => _items;

	public float Get(string id) => _items.GetValueOrDefault(id);

	public void Add(string id, float amount)
	{
		if (amount > 0f)
			_items[id] = Get(id) + amount;
	}

	public bool TryRemove(string id, float amount)
	{
		float have = Get(id);
		if (have < amount)
			return false;
		if (have - amount <= 1e-4f)
			_items.Remove(id);
		else
			_items[id] = have - amount;
		return true;
	}
}
