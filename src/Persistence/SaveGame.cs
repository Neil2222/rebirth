using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Rebirth.Persistence;

/// <summary>Everything needed to restore a world on top of its procedural base.</summary>
public sealed class SaveGame
{
	public const int CurrentVersion = 1;

	public int Version { get; set; } = CurrentVersion;
	/// <summary>Which Box (star cluster) this world is in.</summary>
	public int Box { get; set; } = 1;
	public DateTime SavedAt { get; set; } = DateTime.Now;
	public PlayerSave Player { get; set; } = new();
	public List<GridSave> Grids { get; set; } = new();
	public List<TerrainSave> Terrain { get; set; } = new();
	/// <summary>The design on the Forge's bench, so unfinished work survives a restart.</summary>
	public Blueprint? ForgeDesign { get; set; }
	public Nexus.ColonySave Colony { get; set; } = new();
	/// <summary>Worlds from before the tutorial existed count as finished.</summary>
	public ProgressSave Progress { get; set; } = new();

	public string ToJson() => JsonSerializer.Serialize(this, Blueprint.Json);

	public static SaveGame FromJson(string json) =>
		JsonSerializer.Deserialize<SaveGame>(json, Blueprint.Json) ?? throw new JsonException("Empty save");
}

/// <summary>How far the player is: tutorial step and what is unlocked.</summary>
public sealed class ProgressSave
{
	/// <summary>Current tutorial step, or -1 once it is finished or skipped.</summary>
	public int TutorialStep { get; set; } = -1;
	public bool NexusUnlocked { get; set; } = true;
}

public sealed class PlayerSave
{
	public float[] Position { get; set; } = [0, 0, 0];
	public float[] Rotation { get; set; } = [0, 0, 0, 1];
	public float[] Velocity { get; set; } = [0, 0, 0];
	public bool Jetpack { get; set; } = true;
	public bool Dampeners { get; set; } = true;
	public bool Creative { get; set; }
	public bool Light { get; set; } = true;
	public bool FirstPerson { get; set; }
	/// <summary>Robot body design; null keeps the default.</summary>
	public Blueprint? Body { get; set; }
	public Dictionary<string, float> Inventory { get; set; } = new();
}

public sealed class GridSave
{
	public float[] Position { get; set; } = [0, 0, 0];
	public float[] Rotation { get; set; } = [0, 0, 0, 1];
	public float[] LinearVelocity { get; set; } = [0, 0, 0];
	public float[] AngularVelocity { get; set; } = [0, 0, 0];
	public bool Static { get; set; }
	public bool Dampeners { get; set; } = true;
	public string? Label { get; set; }
	public bool Bot { get; set; }
	public Blueprint Blocks { get; set; } = new();
	public Dictionary<string, float> Inventory { get; set; } = new();
	public List<FabricatorSave> Fabricators { get; set; } = new();
	public List<MachineSave> Machines { get; set; } = new();
}

/// <summary>A machine's buffers; parcels still in the tubes are saved as already delivered.</summary>
public sealed class MachineSave
{
	public int[] Cell { get; set; } = [0, 0, 0];
	public Dictionary<string, float> Input { get; set; } = new();
	public Dictionary<string, float> Output { get; set; } = new();
}

public sealed class FabricatorSave
{
	public int[] Cell { get; set; } = [0, 0, 0];
	public List<Blueprint> Queue { get; set; } = new();
	/// <summary>Progress of the first job, which has been paid for when <see cref="Paid"/> is set.</summary>
	public float Progress { get; set; }
	public bool Paid { get; set; }
}

public sealed class TerrainSave
{
	public string Id { get; set; } = "";
	/// <summary>Lattice points as x, y, z triples.</summary>
	public int[] Points { get; set; } = [];
	public float[] Densities { get; set; } = [];
}

/// <summary>Compact array forms of Godot math types for JSON.</summary>
public static class SaveMath
{
	public static float[] ToArray(Vector3 v) => [v.X, v.Y, v.Z];
	public static float[] ToArray(Quaternion q) => [q.X, q.Y, q.Z, q.W];
	public static Vector3 Vector(float[] a) => new(a[0], a[1], a[2]);
	public static Quaternion Rotation(float[] a) => new Quaternion(a[0], a[1], a[2], a[3]).Normalized();

	public static (float[] Position, float[] Rotation) ToArrays(Transform3D t) =>
		(ToArray(t.Origin), ToArray(t.Basis.GetRotationQuaternion()));

	public static Transform3D Transform(float[] position, float[] rotation) =>
		new(new Basis(Rotation(rotation)), Vector(position));
}
