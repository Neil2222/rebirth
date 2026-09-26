using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace Rebirth.Persistence;

/// <summary>Player blueprints on disk (user://blueprints) plus the built-in presets.</summary>
public static class BlueprintLibrary
{
	public const string Directory = "user://blueprints";

	public sealed record Entry(string Name, bool IsPreset, string? Path);

	public static IReadOnlyList<Entry> List()
	{
		var entries = Presets.All.Select(p => new Entry(p.Name, true, null)).ToList();
		using var dir = DirAccess.Open(Directory);
		if (dir is not null)
		{
			foreach (string file in dir.GetFiles().Where(f => f.EndsWith(".json")).OrderBy(f => f))
			{
				string path = $"{Directory}/{file}";
				entries.Add(new Entry(Load(path)?.Name ?? file, false, path));
			}
		}
		return entries;
	}

	public static Blueprint? Load(Entry entry) =>
		entry.IsPreset ? Presets.All.First(p => p.Name == entry.Name) : Load(entry.Path!);

	public static Blueprint? Load(string path)
	{
		string json = FileAccess.GetFileAsString(path);
		if (string.IsNullOrEmpty(json))
			return null;
		try
		{
			return Blueprint.FromJson(json);
		}
		catch (System.Text.Json.JsonException e)
		{
			GD.PushWarning($"Skipping unreadable blueprint {path}: {e.Message}");
			return null;
		}
	}

	/// <summary>Writes the blueprint to a file named after it, replacing an older version. Returns the path.</summary>
	public static string Save(Blueprint blueprint)
	{
		DirAccess.MakeDirRecursiveAbsolute(Directory);
		string path = $"{Directory}/{FileName(blueprint.Name)}.json";
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write)
			?? throw new System.IO.IOException($"Cannot write {path}: {FileAccess.GetOpenError()}");
		file.StoreString(blueprint.ToJson());
		return path;
	}

	private static string FileName(string name)
	{
		var sb = new StringBuilder();
		foreach (char c in name.Trim().ToLowerInvariant())
			sb.Append(char.IsLetterOrDigit(c) ? c : '_');
		return sb.Length == 0 ? "untitled" : sb.ToString();
	}
}
