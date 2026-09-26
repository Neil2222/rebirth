using System;
using System.Collections.Generic;
using Godot;

namespace Driftworks.Building;

/// <summary>
/// A rigid structure made of cubic blocks on an integer lattice. Cell (0,0,0) sits at the
/// body origin; cell centers are <c>cell * CellSize</c> in local space.
/// Static grids (frozen) are stations; dynamic grids are ships (see BlockGrid.Flight.cs).
/// </summary>
public partial class BlockGrid : RigidBody3D
{
	public const float CellSize = 2.5f;

	// Outward direction plus two tangents with u × v = dir, so corner order below is consistent.
	private static readonly (Vector3I Dir, Vector3 U, Vector3 V)[] Faces =
	[
		(Vector3I.Right, Vector3.Up, Vector3.Back),
		(Vector3I.Left, Vector3.Back, Vector3.Up),
		(Vector3I.Up, Vector3.Back, Vector3.Right),
		(Vector3I.Down, Vector3.Right, Vector3.Back),
		(Vector3I.Back, Vector3.Right, Vector3.Up),
		(Vector3I.Forward, Vector3.Up, Vector3.Right),
	];

	private readonly Dictionary<Vector3I, PlacedBlock> _blocks = new();
	private readonly Dictionary<Vector3I, CollisionShape3D> _shapes = new();
	private readonly Dictionary<Vector3I, Node3D> _decorations = new();
	private readonly BoxShape3D _cellShape = new() { Size = Vector3.One * CellSize };
	private MeshInstance3D _meshInstance = null!;
	private ShaderMaterial _material = null!;
	private float _totalMass;
	private Vector3 _massMoment;

	/// <summary>Raised after a block is removed, before the grid frees itself when it became empty.</summary>
	public event Action<Vector3I>? BlockRemoved;

	public int BlockCount => _blocks.Count;
	public bool IsStatic => Freeze;

	/// <summary>Creates an empty grid under <paramref name="parent"/>.</summary>
	public static BlockGrid Create(Node parent, Transform3D transform, bool isStatic)
	{
		var grid = new BlockGrid { Name = "Grid", Transform = transform, Freeze = isStatic, FreezeMode = FreezeModeEnum.Static };
		parent.AddChild(grid, forceReadableName: true);
		return grid;
	}

	public override void _Ready()
	{
		_material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/armor.gdshader") };
		_meshInstance = new MeshInstance3D();
		AddChild(_meshInstance);
		CenterOfMassMode = CenterOfMassModeEnum.Custom;
		LinearDampMode = DampMode.Replace;
		AngularDampMode = DampMode.Replace;
		LinearDamp = 0f;
		AngularDamp = 0f;
	}

	public bool Has(Vector3I cell) => _blocks.ContainsKey(cell);

	public bool TryGet(Vector3I cell, out PlacedBlock block) => _blocks.TryGetValue(cell, out block);

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

	public bool TryAdd(Vector3I cell, BlockDefinition definition, Basis orientation)
	{
		var block = new PlacedBlock(definition, orientation);
		if (!_blocks.TryAdd(cell, block))
			return false;

		var shape = new CollisionShape3D { Shape = _cellShape, Position = CellCenter(cell) };
		AddChild(shape);
		_shapes[cell] = shape;

		if (BlockVisuals.CreateDecoration(definition) is { } decoration)
		{
			decoration.Transform = new Transform3D(orientation, CellCenter(cell));
			AddChild(decoration);
			_decorations[cell] = decoration;
		}

		ApplyMassDelta(cell, definition.Mass);
		OnBlocksChanged();
		return true;
	}

	public bool Remove(Vector3I cell)
	{
		if (!_blocks.Remove(cell, out var block))
			return false;

		_shapes.Remove(cell, out var shape);
		shape!.QueueFree();
		if (_decorations.Remove(cell, out var decoration))
			decoration.QueueFree();

		BlockRemoved?.Invoke(cell);
		if (_blocks.Count == 0)
		{
			QueueFree();
			return true;
		}
		ApplyMassDelta(cell, -block.Definition.Mass);
		OnBlocksChanged();
		return true;
	}

	private void OnBlocksChanged()
	{
		RebuildMesh();
		RebuildFlightCapabilities();
	}

	private void ApplyMassDelta(Vector3I cell, float mass)
	{
		_totalMass += mass;
		_massMoment += CellCenter(cell) * mass;
		Mass = _totalMass;
		CenterOfMass = _massMoment / _totalMass;
	}

	private void RebuildMesh()
	{
		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var colors = new List<Color>();
		var uvs = new List<Vector2>();
		var indices = new List<int>();
		const float h = CellSize * 0.5f;

		foreach (var (cell, block) in _blocks)
		{
			Vector3 center = CellCenter(cell);
			Color color = block.Definition.Color.SrgbToLinear();
			foreach (var (dir, u, v) in Faces)
			{
				if (_blocks.ContainsKey(cell + dir))
					continue;

				Vector3 n = dir;
				Vector3 faceCenter = center + n * h;
				int b = vertices.Count;
				vertices.Add(faceCenter + (-u - v) * h);
				vertices.Add(faceCenter + (u - v) * h);
				vertices.Add(faceCenter + (u + v) * h);
				vertices.Add(faceCenter + (-u + v) * h);
				uvs.Add(new Vector2(0, 0));
				uvs.Add(new Vector2(1, 0));
				uvs.Add(new Vector2(1, 1));
				uvs.Add(new Vector2(0, 1));
				for (int i = 0; i < 4; i++)
				{
					normals.Add(n);
					colors.Add(color);
				}
				// Godot treats clockwise triangles as front-facing.
				indices.AddRange([b, b + 2, b + 1, b, b + 3, b + 2]);
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		mesh.SurfaceSetMaterial(0, _material);
		_meshInstance.Mesh = mesh;
	}
}
