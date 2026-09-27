using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Characters;
using Rebirth.World;

namespace Rebirth.Persistence;

/// <summary>
/// Writes and restores worlds. A save slot is a folder holding one whole game: the campaign
/// (campaign.json: Boxes, Starlight, upgrades) and the world of every Box visited (box1.json, ...).
/// Loading reloads the scene first, so the procedural base is rebuilt and the save applied on top.
/// </summary>
public static class SaveSystem
{
	public const string Root = "user://saves";
	public const int SlotCount = 6;
	private const string ActiveFile = Root + "/active.txt";

	/// <summary>World to apply once the reloaded scene is ready; null starts a fresh world.</summary>
	public static SaveGame? PendingLoad { get; set; }

	/// <summary>The slot being played: autosave, F5 and travel write here.</summary>
	public static int ActiveSlot { get; private set; } = 1;

	static SaveSystem()
	{
		MigrateLegacy();
		if (FileAccess.FileExists(ActiveFile) && int.TryParse(FileAccess.GetFileAsString(ActiveFile).Trim(), out int slot) && slot is >= 1 and <= SlotCount)
			ActiveSlot = slot;
	}

	public static string SlotDir(int slot) => $"{Root}/slot{slot}";

	public static string WorldPath(int box, int slot) => $"{SlotDir(slot)}/box{box}.json";

	public static void SetActive(int slot)
	{
		ActiveSlot = slot;
		DirAccess.MakeDirRecursiveAbsolute(Root);
		using var file = FileAccess.Open(ActiveFile, FileAccess.ModeFlags.Write);
		file?.StoreString(slot.ToString());
	}

	/// <summary>A slot is in use once it holds a campaign.</summary>
	public static bool Used(int slot) => FileAccess.FileExists($"{SlotDir(slot)}/campaign.json");

	public static void WriteWorld(SaveGame save) => WriteText(WorldPath(save.Box, ActiveSlot), save.ToJson());

	public static SaveGame? ReadWorld(int box) => ReadWorld(box, ActiveSlot);

	public static SaveGame? ReadWorld(int box, int slot)
	{
		string path = WorldPath(box, slot);
		if (!FileAccess.FileExists(path))
			return null;
		try
		{
			return SaveGame.FromJson(FileAccess.GetFileAsString(path));
		}
		catch (System.Text.Json.JsonException e)
		{
			GD.PushError($"Save {path} is unreadable: {e.Message}");
			return null;
		}
	}

	/// <summary>Writes through a temporary file, so a crash mid-save never destroys the previous save.</summary>
	public static void WriteText(string path, string text)
	{
		DirAccess.MakeDirRecursiveAbsolute(path.GetBaseDir());
		string temp = path + ".tmp";
		using (var file = FileAccess.Open(temp, FileAccess.ModeFlags.Write)
			?? throw new System.IO.IOException($"Cannot write {temp}: {FileAccess.GetOpenError()}"))
		{
			file.StoreString(text);
		}
		DirAccess.RenameAbsolute(temp, path);
	}

	public static void DeleteSlot(int slot)
	{
		string dir = SlotDir(slot);
		if (!DirAccess.DirExistsAbsolute(dir))
			return;
		foreach (string file in DirAccess.GetFilesAt(dir))
			DirAccess.RemoveAbsolute($"{dir}/{file}");
		DirAccess.RemoveAbsolute(dir);
	}

	/// <summary>Replaces <paramref name="to"/> with a copy of every file in <paramref name="from"/>.</summary>
	public static void CopySlot(int from, int to)
	{
		DeleteSlot(to);
		DirAccess.MakeDirRecursiveAbsolute(SlotDir(to));
		if (!DirAccess.DirExistsAbsolute(SlotDir(from)))
			return;
		foreach (string file in DirAccess.GetFilesAt(SlotDir(from)))
			DirAccess.CopyAbsolute($"{SlotDir(from)}/{file}", $"{SlotDir(to)}/{file}");
	}

	/// <summary>Saves from before slots existed (loose autosave/quicksave and campaign) move into slot 1.</summary>
	private static void MigrateLegacy()
	{
		if (DirAccess.DirExistsAbsolute(SlotDir(1)))
			return;
		const string legacyCampaign = "user://campaign.json";
		var worlds = new[] { "autosave", "quicksave" }.Select(n => $"{Root}/{n}.json").Where(FileAccess.FileExists)
			.OrderByDescending(FileAccess.GetModifiedTime).ToList();
		if (!FileAccess.FileExists(legacyCampaign) && worlds.Count == 0)
			return;
		DirAccess.MakeDirRecursiveAbsolute(SlotDir(1));
		if (FileAccess.FileExists(legacyCampaign))
			DirAccess.RenameAbsolute(legacyCampaign, $"{SlotDir(1)}/campaign.json");
		foreach (string file in DirAccess.GetFilesAt(Root).Where(f => f.StartsWith("box") && f.EndsWith(".json") && !f.Contains("archive")))
			DirAccess.RenameAbsolute($"{Root}/{file}", $"{SlotDir(1)}/{file}");
		if (worlds.Count > 0)
		{
			try
			{
				var latest = SaveGame.FromJson(FileAccess.GetFileAsString(worlds[0]));
				WriteText(WorldPath(latest.Box, 1), latest.ToJson());
			}
			catch (System.Text.Json.JsonException e)
			{
				GD.PushWarning($"Old save {worlds[0]} could not be moved: {e.Message}");
			}
		}
	}

	public static SaveGame Capture(Node world, Player player, Blueprint? forgeDesign, Nexus.ColonySave colony, ProgressSave progress)
	{
		var save = new SaveGame { Player = player.ToSave(), Colony = colony, Progress = progress };
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
				Label = grid.Label,
				Bot = grid.IsBot,
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
			grid.Label = saved.Label;
			grid.IsBot = saved.Bot;
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
