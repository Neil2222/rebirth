using System.Collections.Generic;
using System.Linq;
using Driftworks.Building;

namespace Driftworks.Characters;

/// <summary>Something the player can hold: a block to build with, or a hand tool.</summary>
public sealed record ToolbarItem(string Name, BlockDefinition? Block = null, bool IsDrill = false);

public static class Toolbar
{
	/// <summary>Items bound to keys 1..N; key 0 is always the empty hand.</summary>
	public static readonly IReadOnlyList<ToolbarItem> Slots =
	[
		.. new[] { BlockCatalog.LightArmor, BlockCatalog.HeavyArmor, BlockCatalog.Cockpit, BlockCatalog.Thruster, BlockCatalog.Gyroscope }
			.Select(b => new ToolbarItem(b.DisplayName, Block: b)),
		new ToolbarItem("Hand Drill", IsDrill: true),
	];
}
