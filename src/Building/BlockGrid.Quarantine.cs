using System.Collections.Generic;
using Godot;

namespace Rebirth.Building;

// A virus from the Curator can put a machine in quarantine: it simply stops (nothing breaks) under a
// soft purple shimmer until someone purges it (a circuit puzzle, or a Firewall nearby).
public partial class BlockGrid
{
	private readonly HashSet<Vector3I> _quarantined = new();
	private readonly Dictionary<Vector3I, MeshInstance3D> _quarantineMarks = new();
	private static ShaderMaterial? _quarantineMaterial;

	public IReadOnlyCollection<Vector3I> Quarantined => _quarantined;

	public bool IsQuarantined(Vector3I cell) => _quarantined.Contains(cell);

	/// <summary>Machines a virus can nap in.</summary>
	public static bool Infectable(BlockDefinition block) => block.Kind is BlockKind.AutoDrill or BlockKind.Refinery or BlockKind.Fabricator
		or BlockKind.AirProcessor or BlockKind.Hydrator or BlockKind.SeedGarden or BlockKind.Incubator;

	public void Quarantine(Vector3I cell)
	{
		if (!_blocks.ContainsKey(cell) || !_quarantined.Add(cell))
			return;
		_quarantineMaterial ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/quarantine.gdshader") };
		var mark = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = Vector3.One * CellSize * 1.12f },
			MaterialOverride = _quarantineMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = CellCenter(cell),
		};
		AddChild(mark);
		_quarantineMarks[cell] = mark;
		_machineStatus[cell] = QuarantineStatus;
		_fabricatorStatus[cell] = QuarantineStatus;
	}

	public const string QuarantineStatus = "Quarantined: a virus is napping in it";

	public void Purge(Vector3I cell)
	{
		if (!_quarantined.Remove(cell))
			return;
		// Let the machine report its own state again from the next tick.
		_machineStatus.Remove(cell);
		_fabricatorStatus.Remove(cell);
		if (_quarantineMarks.Remove(cell, out var mark))
			mark.QueueFree();
	}

	/// <summary>Skips a machine this tick if it is quarantined (and says so).</summary>
	private bool Napping(Vector3I cell)
	{
		if (!_quarantined.Contains(cell))
			return false;
		_machineStatus[cell] = QuarantineStatus;
		return true;
	}
}
