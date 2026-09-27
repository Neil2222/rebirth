using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Rebirth.World;

/// <summary>
/// A minable rock body (asteroid, small planet) backed by a dense density field (solid where
/// density &gt; 0) on a 1 m lattice, meshed per 16³ chunk with <see cref="SurfaceNets"/>.
/// Subclasses only describe the shape; storage, meshing, mining and saving live here.
/// </summary>
public abstract partial class VoxelBody : StaticBody3D, IVoxelSource, IMinable, IEditableTerrain
{
	public const int ChunkSize = 16;

	public float Radius { get; set; } = 30f;
	public int Seed { get; set; }

	private int _size;           // lattice points per axis
	private float _half;         // local position of point i is i - _half
	private float[] _density = null!;
	private byte[] _material = null!;
	private int _chunks;         // chunks per axis
	private MeshInstance3D?[] _chunkMeshes = null!;
	private CollisionShape3D?[] _chunkShapes = null!;
	private readonly HashSet<int> _dirtyChunks = new();
	private readonly HashSet<int> _editedPoints = new();
	private ShaderMaterial _surfaceMaterial = null!;

	public override void _Ready()
	{
		_surfaceMaterial = SurfaceNets.CreateTerrainMaterial();
		Generate();
		// Surface extraction is pure array work, so chunks are meshed in parallel; only creating
		// the Godot meshes and shapes has to happen on this thread.
		var geometry = new ChunkGeometry?[_chunks * _chunks * _chunks];
		Parallel.For(0, geometry.Length, i => geometry[i] = ComputeChunk(i));
		for (int i = 0; i < geometry.Length; i++)
			ApplyChunkGeometry(i, geometry[i]);
	}

	public override void _PhysicsProcess(double delta)
	{
		foreach (int chunk in _dirtyChunks)
			ApplyChunkGeometry(chunk, ComputeChunk(chunk));
		_dirtyChunks.Clear();
	}

	public float[] Carve(Vector3 worldCenter, float radius)
	{
		var mined = new float[VoxelMaterials.All.Count];
		Vector3 c = ToLocal(worldCenter) + Vector3.One * _half;
		Vector3I min = ((Vector3I)(c - Vector3.One * (radius + 1f)).Floor()).Clamp(Vector3I.One, Vector3I.One * (_size - 2));
		Vector3I max = ((Vector3I)(c + Vector3.One * (radius + 1f)).Ceil()).Clamp(Vector3I.One, Vector3I.One * (_size - 2));

		for (int z = min.Z; z <= max.Z; z++)
		for (int y = min.Y; y <= max.Y; y++)
		for (int x = min.X; x <= max.X; x++)
		{
			int i = Index(x, y, z);
			float d = _density[i];
			float carved = Mathf.Min(d, new Vector3(x, y, z).DistanceTo(c) - radius);
			if (carved >= d)
				continue;
			mined[_material[i]] += Mathf.Clamp(d, 0f, 1f) - Mathf.Clamp(carved, 0f, 1f);
			_density[i] = carved;
			_editedPoints.Add(i);
			MarkDirty(x, y, z);
		}
		return mined;
	}

	public string TerrainId => Name;

	/// <summary>
	/// Distance from the center to the surface along <paramref name="localDirection"/>, including dug-out
	/// edits: the outermost point where the field turns solid, found by stepping inwards.
	/// </summary>
	public float SurfaceRadius(Vector3 localDirection)
	{
		Vector3 dir = localDirection.Normalized();
		float previous = SampleDensity(dir * MaxSurfaceRadius);
		for (float r = MaxSurfaceRadius - 0.25f; r > 0f; r -= 0.25f)
		{
			float d = SampleDensity(dir * r);
			if (d > 0f)
				return r + 0.25f * d / (d - previous);   // interpolate the crossing
			previous = d;
		}
		return 0f;
	}

	/// <summary>World-space surface point straight "above" the center in <paramref name="worldDirection"/>.</summary>
	public Vector3 SurfacePoint(Vector3 worldDirection)
	{
		Vector3 local = GlobalBasis.Inverse() * worldDirection;
		return GlobalTransform * (local.Normalized() * SurfaceRadius(local));
	}

	/// <summary>
	/// What the ground holds within <paramref name="radius"/> of a world point: kilograms of each ore item
	/// (as mining would yield), for the Nexus's survey of a build spot.
	/// </summary>
	public Dictionary<string, float> Survey(Vector3 worldCenter, float radius)
	{
		var result = new Dictionary<string, float>();
		Vector3 c = ToLocal(worldCenter) + Vector3.One * _half;
		Vector3I min = ((Vector3I)(c - Vector3.One * radius).Floor()).Clamp(Vector3I.Zero, Vector3I.One * (_size - 1));
		Vector3I max = ((Vector3I)(c + Vector3.One * radius).Ceil()).Clamp(Vector3I.Zero, Vector3I.One * (_size - 1));
		for (int z = min.Z; z <= max.Z; z++)
		for (int y = min.Y; y <= max.Y; y++)
		for (int x = min.X; x <= max.X; x++)
		{
			int i = Index(x, y, z);
			if (_density[i] <= 0f || new Vector3(x, y, z).DistanceTo(c) > radius)
				continue;
			var material = VoxelMaterials.All[_material[i]];
			result[material.OreItemId] = result.GetValueOrDefault(material.OreItemId) + Mathf.Min(_density[i], 1f) * material.YieldPerCubicMetre;
		}
		return result;
	}

	/// <summary>Trilinear density at a local position.</summary>
	private float SampleDensity(Vector3 local)
	{
		Vector3 p = local + Vector3.One * _half;
		Vector3I i = (Vector3I)p.Floor();
		Vector3 f = p - (Vector3)i;
		float Lerp3(int dx, int dy) =>
			Mathf.Lerp(Density(i.X + dx, i.Y + dy, i.Z), Density(i.X + dx, i.Y + dy, i.Z + 1), f.Z);
		float x0 = Mathf.Lerp(Lerp3(0, 0), Lerp3(0, 1), f.Y);
		float x1 = Mathf.Lerp(Lerp3(1, 0), Lerp3(1, 1), f.Y);
		return Mathf.Lerp(x0, x1, f.X);
	}

	/// <summary>How far out anything of this body can reach (m): for keeping flights clear of it.</summary>
	public float OuterRadius => MaxSurfaceRadius;

	public (int[] Points, float[] Densities) ExportEdits()
	{
		var points = new int[_editedPoints.Count * 3];
		var densities = new float[_editedPoints.Count];
		int n = 0;
		foreach (int i in _editedPoints)
		{
			points[n * 3] = i % _size;
			points[n * 3 + 1] = i / _size % _size;
			points[n * 3 + 2] = i / (_size * _size);
			densities[n++] = _density[i];
		}
		return (points, densities);
	}

	public void ImportEdits(int[] points, float[] densities)
	{
		for (int n = 0; n < densities.Length; n++)
		{
			int x = points[n * 3], y = points[n * 3 + 1], z = points[n * 3 + 2];
			if (!Inside(x, y, z))
				continue;
			int i = Index(x, y, z);
			_density[i] = densities[n];
			_editedPoints.Add(i);
			MarkDirty(x, y, z);
		}
	}

	private int Index(int x, int y, int z) => x + _size * (y + _size * z);

	private bool Inside(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < _size && y < _size && z < _size;

	/// <summary>Everything outside the field is empty space.</summary>
	public float Density(int x, int y, int z) => Inside(x, y, z) ? _density[Index(x, y, z)] : -1f;

	public byte Material(int x, int y, int z) => Inside(x, y, z) ? _material[Index(x, y, z)] : VoxelMaterials.Stone;

	/// <summary>
	/// A lattice point feeds the cells on both sides of it and the edges starting on either side; a chunk
	/// also meshes the cell just before its origin. Together that reaches the chunks of point-1..point+1.
	/// </summary>
	private void MarkDirty(int x, int y, int z)
	{
		for (int dz = -1; dz <= 1; dz++)
		for (int dy = -1; dy <= 1; dy++)
		for (int dx = -1; dx <= 1; dx++)
		{
			int px = x + dx, py = y + dy, pz = z + dz;
			if (px < 0 || py < 0 || pz < 0)
				continue;
			int cx = px / ChunkSize, cy = py / ChunkSize, cz = pz / ChunkSize;
			if (cx < _chunks && cy < _chunks && cz < _chunks)
				_dirtyChunks.Add(cx + _chunks * (cy + _chunks * cz));
		}
	}

	private void Generate()
	{
		// Keep a margin of empty points around the furthest the surface can reach.
		float reach = MaxSurfaceRadius + 2f;
		_size = Mathf.CeilToInt(reach * 2f) + 4;
		_half = (_size - 1) * 0.5f;
		_chunks = Mathf.CeilToInt((_size - 1) / (float)ChunkSize);
		_chunkMeshes = new MeshInstance3D?[_chunks * _chunks * _chunks];
		_chunkShapes = new CollisionShape3D?[_chunks * _chunks * _chunks];
		_density = new float[_size * _size * _size];
		_material = new byte[_size * _size * _size];

		PrepareShape();
		// Beyond the furthest possible surface every point is empty and needs no evaluation at all.
		Parallel.For(0, _size, z =>
		{
			for (int y = 0; y < _size; y++)
			for (int x = 0; x < _size; x++)
			{
				int i = Index(x, y, z);
				var p = new Vector3(x - _half, y - _half, z - _half);
				bool border = x == 0 || y == 0 || z == 0 || x == _size - 1 || y == _size - 1 || z == _size - 1;
				if (border || p.Length() > reach)
				{
					_density[i] = -1f;
					continue;
				}

				float d = ShapeDensity(p);
				_density[i] = Mathf.Clamp(d, -4f, 4f);
				if (d > -1f)
					_material[i] = ShapeMaterial(p, d);
			}
		});
	}

	/// <summary>Furthest the surface can be from the center, in metres.</summary>
	protected abstract float MaxSurfaceRadius { get; }

	/// <summary>Set up noise before generation. Shape functions below run on worker threads.</summary>
	protected abstract void PrepareShape();

	/// <summary>Signed distance-like density at a local point: positive inside.</summary>
	protected abstract float ShapeDensity(Vector3 p);

	/// <summary>Material at a solid (or nearly solid) point with the given density (depth below the surface).</summary>
	protected abstract byte ShapeMaterial(Vector3 p, float density);

	private ChunkGeometry? ComputeChunk(int chunk)
	{
		int cx = chunk % _chunks, cy = chunk / _chunks % _chunks, cz = chunk / (_chunks * _chunks);
		return SurfaceNets.Extract(this, new Vector3I(cx, cy, cz) * ChunkSize, ChunkSize, -Vector3.One * _half);
	}

	private void ApplyChunkGeometry(int chunk, ChunkGeometry? geometry)
	{
		if (geometry is null)
		{
			if (_chunkMeshes[chunk] is { } emptyMesh)
				emptyMesh.Mesh = null;
			if (_chunkShapes[chunk] is { } emptyShape)
			{
				emptyShape.QueueFree();
				_chunkShapes[chunk] = null;
			}
			return;
		}

		if (_chunkMeshes[chunk] is null)
		{
			_chunkMeshes[chunk] = new MeshInstance3D();
			AddChild(_chunkMeshes[chunk]);
		}
		_chunkMeshes[chunk]!.Mesh = SurfaceNets.CreateMesh(geometry, _surfaceMaterial);

		if (_chunkShapes[chunk] is null)
		{
			_chunkShapes[chunk] = new CollisionShape3D();
			AddChild(_chunkShapes[chunk]);
		}
		_chunkShapes[chunk]!.Shape = SurfaceNets.CreateShape(geometry);
	}
}
