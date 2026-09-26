using System.Collections.Generic;
using System.Linq;

namespace Rebirth.Items;

/// <summary>Item amounts by id, in kilograms, limited by a total mass capacity.</summary>
public sealed class Inventory
{
	private readonly Dictionary<string, float> _items = new();

	public float Capacity { get; set; } = float.PositiveInfinity;

	public IReadOnlyDictionary<string, float> Items => _items;

	public float Total => _items.Values.Sum();

	public float FreeSpace => System.Math.Max(0f, Capacity - Total);

	public float Get(string id) => _items.GetValueOrDefault(id);

	/// <summary>Adds as much as fits; returns the amount actually added.</summary>
	public float Add(string id, float amount)
	{
		amount = System.Math.Min(amount, FreeSpace);
		if (amount <= 0f)
			return 0f;
		_items[id] = Get(id) + amount;
		return amount;
	}

	public void Clear() => _items.Clear();

	public bool Has(IReadOnlyDictionary<string, float> items) => items.All(kv => Get(kv.Key) >= kv.Value);

	public bool TryRemove(string id, float amount)
	{
		float have = Get(id);
		if (have < amount)
			return false;
		if (have - amount <= 1e-3f)
			_items.Remove(id);
		else
			_items[id] = have - amount;
		return true;
	}

	/// <summary>Moves up to <paramref name="amount"/> of an item into <paramref name="target"/>; returns what moved.</summary>
	public float TransferTo(Inventory target, string id, float amount)
	{
		float moved = target.Add(id, System.Math.Min(amount, Get(id)));
		if (moved > 0f)
			TryRemove(id, moved);
		return moved;
	}
}
