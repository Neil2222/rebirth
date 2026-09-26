using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Characters;
using Rebirth.World;

namespace Rebirth.Persistence;

/// <summary>
/// Writes and restores worlds. Loading reloads the scene first, so the procedural base
/// (asteroids, planet) is rebuilt from scratch and the save is applied on top.
/// </summary>
public static class SaveSystem
{
	public const string Directory = "user://saves";
	public const string QuickSlot = "quicksave";
	public const string AutoSlot = "autosave";

	/// <summary>Save to apply once the reloaded scene is ready; null starts a fresh world.</summary>
	public static SaveGame? PendingLoad { get; set; }

	public static string PathFor(string slot) => $"{Directory}/{slot}.json";

	public static bool Exists(string slot) => FileAccess.FileExists(PathFor(slot));

	/// <summary>The most recently written of the autosave and quicksave, if any.</summary>
	public static string? LatestSlot() =>
		new[] { AutoSlot, QuickSlot }
			.Where(Exists)
			.OrderByDescending(slot => FileAccess.GetModifiedTime(PathFor(slot)))
			.FirstOrDefault();

	public static SaveGame Capture(Node world, Player player, BlockGrid? forgeDesign)
	{
		var save = new SaveGame { Player = player.ToSave() };
		foreach (var grid in world.GetChildren().OfType<BlockGrid>())
		{
			if (grid.IsQueuedForDeletion() || grid.BlockCount == 0)
				continue;
			var (position, rotation) = SaveMath.ToArrays(grid.GlobalTransform);
			save.Grids.Add(new GridSave
			{
				Position = position,
				Rotation = rotation,
				LinearVelocity = SaveMath.ToArray(grid.LinearVelocity),
				AngularVelocity = SaveMath.ToArray(grid.AngularVelocity),
				Static = grid.IsStatic,
				Dampeners = grid.Controls.Dampeners,
				Blocks = Blueprint.FromGrid(grid, grid.Name, includeState: true),
				Inventory = grid.Inventory.Items.ToDictionary(kv => kv.Key, kv => kv.Value),
			});
		}
		foreach (var terrain in world.GetChildren().OfType<IEditableTerrain>())
		{
			var (points, densities) = terrain.ExportEdits();
			if (densities.Length > 0)
				save.Terrain.Add(new TerrainSave { Id = terrain.TerrainId, Points = points, Densities = densities });
		}
		if (forgeDesign is { BlockCount: > 0 })
			save.ForgeDesign = Blueprint.FromGrid(forgeDesign, forgeDesign.Name);
		return save;
	}

	public static void Write(SaveGame save, string slot)
	{
		DirAccess.MakeDirRecursiveAbsolute(Directory);
		// Write to a temporary file first so a crash mid-save never destroys the previous save.
		string path = PathFor(slot);
		string temp = path + ".tmp";
		using (var file = FileAccess.Open(temp, FileAccess.ModeFlags.Write)
			?? throw new System.IO.IOException($"Cannot write {temp}: {FileAccess.GetOpenError()}"))
		{
			file.StoreString(save.ToJson());
		}
		DirAccess.RenameAbsolute(temp, path);
	}

	public static SaveGame? Read(string slot)
	{
		if (!Exists(slot))
			return null;
		try
		{
			return SaveGame.FromJson(FileAccess.GetFileAsString(PathFor(slot)));
		}
		catch (System.Text.Json.JsonException e)
		{
			GD.PushError($"Save {slot} is unreadable: {e.Message}");
			return null;
		}
	}

	/// <summary>Restores grids, terrain edits and the player into a freshly built world.</summary>
	public static void Apply(SaveGame save, Node world, Player player)
	{
		foreach (var terrain in world.GetChildren().OfType<IEditableTerrain>())
		{
			var edits = save.Terrain.FirstOrDefault(t => t.Id == terrain.TerrainId);
			if (edits is not null)
				terrain.ImportEdits(edits.Points, edits.Densities);
		}

		foreach (var saved in save.Grids)
		{
			var grid = BlockGrid.Create(world, SaveMath.Transform(saved.Position, saved.Rotation), saved.Static);
			saved.Blocks.BuildInto(grid);
			grid.LinearVelocity = SaveMath.Vector(saved.LinearVelocity);
			grid.AngularVelocity = SaveMath.Vector(saved.AngularVelocity);
			grid.Controls = grid.Controls with { Dampeners = saved.Dampeners };
			foreach (var (item, amount) in saved.Inventory)
				grid.Inventory.Add(item, amount);
		}

		player.ApplySave(save.Player);
	}
}
