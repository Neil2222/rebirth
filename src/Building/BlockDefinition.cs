using System.Collections.Generic;
using Godot;

namespace Driftworks.Building;

public sealed record BlockDefinition(string Id, string DisplayName, Color Color, float Mass);

public static class BlockCatalog
{
	// Masses follow Space Engineers' large-grid armor blocks.
	public static readonly BlockDefinition LightArmor = new("light_armor", "Light Armor Block", new Color(0.62f, 0.64f, 0.66f), 418f);
	public static readonly BlockDefinition HeavyArmor = new("heavy_armor", "Heavy Armor Block", new Color(0.30f, 0.32f, 0.35f), 3300f);

	/// <summary>Blocks bound to toolbar slots 1..N.</summary>
	public static readonly IReadOnlyList<BlockDefinition> Toolbar = [LightArmor, HeavyArmor];
}
