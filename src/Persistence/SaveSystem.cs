using System.Collections.Generic;
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

	public static SaveGame Capture(Node world, Player player, Blueprint? forgeDesign)
	{
		var save = new SaveGame { Player = player.ToSave() };
		foreach (var grid in world.GetChildren().OfType<BlockGrid>())
		{
			if (grid.IsQueuedForDeletion() || grid.BlockCount == 0)
				continue;
			var (position, rotation) = SaveMath.ToArrays(grid.GlobalTransform);
			var (machines, storage) = CaptureLogistics(grid);
			save.Grids.Add(new GridSave
			{
				Position = position,
				Rotation = rotation,
				LinearVelocity = SaveMath.ToArray(grid.LinearVelocity),
				AngularVelocity = SaveMath.ToArray(grid.AngularVelocity),
				Static = grid.IsStatic,
				Dampeners = grid.Controls.Dampeners,
				Blocks = Blueprint.FromGrid(grid, grid.Name, includeState: true),
				Inventory = storage,
				Machines = machines,
				Fabricators = grid.FabricatorQueues.Select(f => new FabricatorSave
				{
					Cell = [f.Cell.X, f.Cell.Y, f.Cell.Z],
					Queue = f.Queue.Select(job => job.Design).ToList(),
					Progress = f.Queue[0].Progress,
					Paid = f.Queue[0].Paid,
				}).ToList(),
			});
		}
		foreach (var terrain in world.GetChildren().OfType<IEditableTerrain>())
		{
			var (points, densities) = terrain.ExportEdits();
			if (densities.Length > 0)
				save.Terrain.Add(new TerrainSave { Id = terrain.TerrainId, Points = points, Densities = densities });
		}
		save.ForgeDesign = forgeDesign;
		return save;
	}

	/// <summary>Machine buffers and shared storage, with parcels in transit counted at their destination.</summary>
	private static (List<MachineSave> Machines, Dictionary<string, float> Storage) CaptureLogistics(BlockGrid grid)
	{
		var storage = grid.Inventory.Items.ToDictionary(kv => kv.Key, kv => kv.Value);
		var machines = new Dictionary<Vector3I, MachineSave>();
		MachineSave For(Vector3I cell) => machines.TryGetValue(cell, out var m) ? m : machines[cell] = new MachineSave { Cell = [cell.X, cell.Y, cell.Z] };

		foreach (var (cell, _) in grid.Blocks)
		{
			var state = grid.StateOf(cell);
			if (state.Input is { Items.Count: > 0 } input)
				For(cell).Input = input.Items.ToDictionary(kv => kv.Key, kv => kv.Value);
			if (state.Output is { Items.Count: > 0 } output)
				For(cell).Output = output.Items.ToDictionary(kv => kv.Key, kv => kv.Value);
		}
		foreach (var parcel in grid.ParcelsInFlight)
		{
			var into = parcel.ToStorage || !grid.Has(parcel.Destination) || grid.StateOf(parcel.Destination).Input is null
				? storage
				: For(parcel.Destination).Input;
			into[parcel.Item] = into.GetValueOrDefault(parcel.Item) + parcel.Amount;
		}
		return (machines.Values.ToList(), storage);
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
			foreach (var machine in saved.Machines)
			{
				var state = grid.StateOf(new Vector3I(machine.Cell[0], machine.Cell[1], machine.Cell[2]));
				foreach (var (item, amount) in machine.Input)
					state.Input?.Add(item, amount);
				foreach (var (item, amount) in machine.Output)
					state.Output?.Add(item, amount);
			}
			foreach (var fabricator in saved.Fabricators)
			{
				var jobs = fabricator.Queue.Select((design, i) => new FabricatorJob
				{
					Design = design,
					Progress = i == 0 ? fabricator.Progress : 0f,
					Paid = i == 0 && fabricator.Paid,
				}).ToList();
				grid.RestoreFabricator(new Vector3I(fabricator.Cell[0], fabricator.Cell[1], fabricator.Cell[2]), jobs);
			}
		}

		player.ApplySave(save.Player);
	}
}
