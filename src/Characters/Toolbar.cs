using System.Collections.Generic;
using System.Linq;
using Rebirth.Building;

namespace Rebirth.Characters;

/// <summary>Something the player can hold: a block to build with, or a hand tool.</summary>
public sealed record ToolbarItem(string Name, BlockDefinition? Block = null, bool IsDrill = false);

public static class Toolbar
{
	/// <summary>
	/// Pages of items bound to keys 1..9 and 0 (in that order); Tab flips pages. Pressing the key of
	/// the held item empties the hand.
	/// </summary>
	public static readonly IReadOnlyList<IReadOnlyList<ToolbarItem>> Pages =
	[
		[
			new ToolbarItem("Hand Drill", IsDrill: true),
			.. Blocks(BlockCatalog.LightArmor, BlockCatalog.HeavyArmor, BlockCatalog.Cockpit, BlockCatalog.Thruster, BlockCatalog.Gyroscope,
				BlockCatalog.Battery, BlockCatalog.SolarPanel, BlockCatalog.CargoContainer, BlockCatalog.Refinery),
		],
		[
			new ToolbarItem("Hand Drill", IsDrill: true),
			.. Blocks(BlockCatalog.Fabricator),
		],
	];

	private static IEnumerable<ToolbarItem> Blocks(params BlockDefinition[] blocks) =>
		blocks.Select(b => new ToolbarItem(b.DisplayName, Block: b));

	/// <summary>Keyboard digit shown for a slot index.</summary>
	public static int KeyFor(int index) => (index + 1) % 10;
}
