using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Rebirth.Building;

/// <summary>
/// A rigid structure made of cubic blocks on an integer lattice. Cell (0,0,0) sits at the
/// body origin; cell centers are <c>cell * CellSize</c> in local space.
/// Static grids (frozen) are stations; dynamic grids are ships (see BlockGrid.Flight.cs).
/// </summary>
public partial class BlockGrid : RigidBody3D
{
	public const float CellSize = 2.5f;

	private readonly Dictionary<Vector3I, PlacedBlock> _blocks = new();
	private readonly Dictionary<Vector3I, CollisionShape3D> _shapes = new();
	private readonly Dictionary<Vector3I, Node3D> _decorations = new();
	// Slightly undersized so separate grids with touching faces (e.g. just after a split) do not collide.
	private readonly BoxShape3D _cellShape = new() { Size = Vector3.One * CellSize * 0.99f };
	private MeshInstance3D _meshInstance = null!;
	private float _totalMass;
	private Vector3 _massMoment;

	/// <summary>Raised after a block is removed, before the grid frees itself when it became empty.</summary>
	public event Action<Vector3I>? BlockRemoved;

	public int BlockCount => _blocks.Count;
	public bool IsStatic => Freeze;

	/// <summary>
	/// A design being edited in the Forge: may be empty or disconnected while you work on it, and runs
	/// no simulation (power, refining, damage).
	/// </summary>
	public bool DesignMode { get; init; }

	/// <summary>Creates an empty grid under <paramref name="parent"/>.</summary>
	public static BlockGrid Create(Node parent, Transform3D transform, bool isStatic, bool designMode = false)
	{
		var grid = new BlockGrid { Name = "Grid", Transform = transform, Freeze = isStatic, FreezeMode = FreezeModeEnum.Static, DesignMode = designMode };
		parent.AddChild(grid, forceReadableName: true);
		return grid;
	}

	public override void _Ready()
	{
		_meshInstance = new MeshInstance3D();
		AddChild(_meshInstance);
		CenterOfMassMode = CenterOfMassModeEnum.Custom;
		LinearDampMode = DampMode.Replace;
		AngularDampMode = DampMode.Replace;
		LinearDamp = 0f;
		AngularDamp = 0f;
		ContactMonitor = true;
		MaxContactsReported = 16;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (DesignMode)
			return;
		UpdatePower((float)delta);
		UpdateRefining((float)delta);
		ApplyPendingDamage();
	}

	public bool Has(Vector3I cell) => _blocks.ContainsKey(cell);

	public bool TryGet(Vector3I cell, out PlacedBlock block) => _blocks.TryGetValue(cell, out block);

	public BlockState StateOf(Vector3I cell) => _state[cell];

	public IEnumerable<KeyValuePair<Vector3I, PlacedBlock>> Blocks => _blocks;

	public static Vector3 CellCenter(Vector3I cell) => (Vector3)cell * CellSize;

	public static Vector3I LocalToCell(Vector3 local) =>
		new(Mathf.RoundToInt(local.X / CellSize), Mathf.RoundToInt(local.Y / CellSize), Mathf.RoundToInt(local.Z / CellSize));

	/// <summary>Grid-local transform of a block: its orientation, centered on its cell.</summary>
	public Transform3D BlockTransform(Vector3I cell) => new(_blocks[cell].Orientation, CellCenter(cell));

	public void ToggleStatic()
	{
		Freeze = !Freeze;
		if (!Freeze)
			Sleeping = false;
	}

	/// <param name="charge">Initial battery charge as a fraction of capacity (ignored for other blocks).</param>
	/// <param name="paint">Neon colour; the block type's default when omitted.</param>
	public bool TryAdd(Vector3I cell, BlockDefinition definition, Basis orientation, float charge = 0.25f, Color? paint = null)
	{
		var state = new BlockState { Integrity = definition.MaxIntegrity, StoredEnergy = definition.BatteryCapacity * charge };
		return TryAdd(cell, new PlacedBlock(definition, orientation, paint ?? definition.Paint), state);
	}

	/// <summary>Adds a block with existing state, e.g. restored from a save.</summary>
	public bool TryAdd(Vector3I cell, PlacedBlock block, BlockState state)
	{
		if (!AddInternal(cell, block, state))
			return false;
		OnBlocksChanged();
		return true;
	}

	/// <summary>Adds many blocks with a single mesh and capability rebuild; occupied cells are skipped.</summary>
	public void AddMany(IEnumerable<(Vector3I Cell, PlacedBlock Block, BlockState State)> blocks)
	{
		foreach (var (cell, block, state) in blocks)
			AddInternal(cell, block, state);
		OnBlocksChanged();
	}

	/// <summary>Removes every block (design grids only; live grids would free themselves).</summary>
	public void Clear()
	{
		foreach (var cell in _blocks.Keys.ToArray())
			RemoveInternal(cell);
		OnBlocksChanged();
	}

	/// <summary>Removes a block. Any part no longer connected to the rest breaks off as its own grid.</summary>
	public bool Remove(Vector3I cell)
	{
		if (!RemoveInternal(cell))
			return false;
		if (_blocks.Count == 0 && !DesignMode)
		{
			QueueFree();
			return true;
		}
		if (!DesignMode)
			SplitDisconnected();
		OnBlocksChanged();
		return true;
	}

	public void Repaint(Vector3I cell, Color paint)
	{
		if (!_blocks.TryGetValue(cell, out var block) || block.Paint == paint)
			return;
		_blocks[cell] = block with { Paint = paint };
		if (_decorations.Remove(cell, out var old))
			old.QueueFree();
		AddDecoration(cell, _blocks[cell]);
		RebuildMesh();
	}

	private void AddDecoration(Vector3I cell, PlacedBlock block)
	{
		if (BlockVisuals.CreateDecoration(block.Definition, block.Paint) is not { } decoration)
			return;
		decoration.Transform = new Transform3D(block.Orientation, CellCenter(cell));
		AddChild(decoration);
		_decorations[cell] = decoration;
	}

	private bool AddInternal(Vector3I cell, PlacedBlock block, BlockState state)
	{
		if (!_blocks.TryAdd(cell, block))
			return false;
		_state[cell] = state;

		var shape = new CollisionShape3D { Shape = _cellShape, Position = CellCenter(cell) };
		AddChild(shape);
		_shapes[cell] = shape;

		AddDecoration(cell, block);

		ApplyMassDelta(cell, block.Definition.Mass);
		return true;
	}

	/// <summary>Removes the block's data, collider and visuals without rebuilding the mesh or checking connectivity.</summary>
	private bool RemoveInternal(Vector3I cell)
	{
		if (!_blocks.Remove(cell, out var block))
			return false;
		_state.Remove(cell);

		_shapes.Remove(cell, out var shape);
		shape!.QueueFree();
		if (_decorations.Remove(cell, out var decoration))
			decoration.QueueFree();

		ApplyMassDelta(cell, -block.Definition.Mass);
		BlockRemoved?.Invoke(cell);
		return true;
	}

	private void OnBlocksChanged()
	{
		RebuildMesh();
		RebuildFlightCapabilities();
		RebuildPowerAndCargo();
	}

	private void ApplyMassDelta(Vector3I cell, float mass)
	{
		_totalMass += mass;
		_massMoment += CellCenter(cell) * mass;
		if (_blocks.Count == 0)
			return;
		Mass = _totalMass;
		CenterOfMass = _massMoment / _totalMass;
	}

	private void RebuildMesh() =>
		_meshInstance.Mesh = BlockMesher.Build(_blocks, cell => _state[cell].Integrity / _blocks[cell].Definition.MaxIntegrity);
}
