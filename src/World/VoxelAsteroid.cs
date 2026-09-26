using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Driftworks.World;

/// <summary>
/// Minable asteroid backed by a dense density field (solid where density &gt; 0) on a 1 m lattice,
/// meshed per 16³ chunk with <see cref="SurfaceNets"/>.
/// </summary>
public partial class VoxelAsteroid : StaticBody3D, IVoxelSource, IMinable
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
	private StandardMaterial3D _surfaceMaterial = null!;

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
			MarkDirty(x, y, z);
		}
		return mined;
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
		// Noise can push the surface out to ~1.35 R; keep a margin of empty points around it.
		_size = Mathf.CeilToInt(Radius * 2.8f) + 4;
		_half = (_size - 1) * 0.5f;
		_chunks = Mathf.CeilToInt((_size - 1) / (float)ChunkSize);
		_chunkMeshes = new MeshInstance3D?[_chunks * _chunks * _chunks];
		_chunkShapes = new CollisionShape3D?[_chunks * _chunks * _chunks];
		_density = new float[_size * _size * _size];
		_material = new byte[_size * _size * _size];

		var shape = new FastNoiseLite { Seed = Seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1.3f / Radius, FractalOctaves = 4 };
		var detail = new FastNoiseLite { Seed = Seed + 1, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.15f };
		var ores = new OreVeins(Seed + 2);

		// Noise is at most 1, so beyond this radius every point is empty and needs no noise at all.
		float reach = Radius * 1.35f + 2f;
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

				float d = Radius * (1f + 0.35f * shape.GetNoise3Dv(p)) - p.Length() + 1.5f * detail.GetNoise3Dv(p);
				_density[i] = Mathf.Clamp(d, -4f, 4f);
				if (d > -1f)
					_material[i] = ores.At(p, VoxelMaterials.Stone);
			}
		});
	}

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
