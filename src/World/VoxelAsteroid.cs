using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Driftworks.World;

/// <summary>
/// Minable asteroid backed by a density field (solid where density &gt; 0) on a 1 m lattice.
/// The surface is extracted per 16³ chunk with Surface Nets: one vertex per cell that straddles
/// the surface, one quad per lattice edge that crosses it. Neighbouring chunks derive shared
/// vertices from the same global field, so there are no seams.
/// </summary>
public partial class VoxelAsteroid : StaticBody3D
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
		_surfaceMaterial = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			VertexColorIsSrgb = true,
			Roughness = 0.95f,
		};
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

	/// <summary>Removes a sphere of rock. Returns the mined volume (m³) per material index.</summary>
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

	/// <summary>Density at a lattice point; everything outside the field is empty space.</summary>
	private float Density(int x, int y, int z) =>
		x < 0 || y < 0 || z < 0 || x >= _size || y >= _size || z >= _size ? -1f : _density[Index(x, y, z)];

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
		var veins = new FastNoiseLite { Seed = Seed + 2, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.07f, FractalOctaves = 2 };
		var oreKind = new FastNoiseLite { Seed = Seed + 3, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.02f };

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
				if (d > -1f && veins.GetNoise3Dv(p) > 0.45f)
				{
					float kind = oreKind.GetNoise3Dv(p);
					_material[i] = kind < -0.2f ? VoxelMaterials.Iron : kind < 0.25f ? VoxelMaterials.Nickel : VoxelMaterials.Silicon;
				}
			}
		});
	}

	private static readonly (int A, int B)[] CellEdges = BuildCellEdges();

	// Corner index = dx | dy << 1 | dz << 2; an edge joins two corners that differ in one bit.
	private static (int, int)[] BuildCellEdges()
	{
		var edges = new List<(int, int)>();
		for (int c = 0; c < 8; c++)
		for (int bit = 1; bit <= 4; bit <<= 1)
			if ((c & bit) == 0)
				edges.Add((c, c | bit));
		return edges.ToArray();
	}

	private static Vector3 CornerOffset(int c) => new(c & 1, (c >> 1) & 1, (c >> 2) & 1);

	private sealed record ChunkGeometry(Vector3[] Vertices, Vector3[] Normals, Color[] Colors, int[] Indices);

	/// <summary>Extracts one chunk's surface. Reads the density field only, so it may run on worker threads.</summary>
	private ChunkGeometry? ComputeChunk(int chunk)
	{
		const int n = ChunkSize;
		int cx = chunk % _chunks, cy = chunk / _chunks % _chunks, cz = chunk / (_chunks * _chunks);
		var origin = new Vector3I(cx, cy, cz) * n;

		// Vertices for cells -1..n-1 (relative to origin); quads for edges starting at points 0..n-1.
		const int m = n + 1;
		var cellVertex = new int[m * m * m];
		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var colors = new List<Color>();
		var indices = new List<int>();
		Span<float> d = stackalloc float[8];

		for (int z = -1; z < n; z++)
		for (int y = -1; y < n; y++)
		for (int x = -1; x < n; x++)
		{
			int slot = (x + 1) + m * ((y + 1) + m * (z + 1));
			cellVertex[slot] = -1;
			int gx = origin.X + x, gy = origin.Y + y, gz = origin.Z + z;
			int mask = 0;
			for (int c = 0; c < 8; c++)
			{
				d[c] = Density(gx + (c & 1), gy + ((c >> 1) & 1), gz + ((c >> 2) & 1));
				if (d[c] > 0f)
					mask |= 1 << c;
			}
			if (mask == 0 || mask == 0xFF)
				continue;

			Vector3 sum = Vector3.Zero;
			int crossings = 0;
			foreach (var (a, b) in CellEdges)
			{
				if ((d[a] > 0f) == (d[b] > 0f))
					continue;
				float t = d[a] / (d[a] - d[b]);
				sum += CornerOffset(a).Lerp(CornerOffset(b), t);
				crossings++;
			}

			var gradient = new Vector3(
				d[1] + d[3] + d[5] + d[7] - d[0] - d[2] - d[4] - d[6],
				d[2] + d[3] + d[6] + d[7] - d[0] - d[1] - d[4] - d[5],
				d[4] + d[5] + d[6] + d[7] - d[0] - d[1] - d[2] - d[3]);

			int solidest = 0;
			for (int c = 1; c < 8; c++)
				if (d[c] > d[solidest])
					solidest = c;
			byte material = MaterialAt(gx + (solidest & 1), gy + ((solidest >> 1) & 1), gz + ((solidest >> 2) & 1));

			cellVertex[slot] = vertices.Count;
			vertices.Add(new Vector3(gx, gy, gz) + sum / crossings - Vector3.One * _half);
			normals.Add(-gradient.Normalized());
			colors.Add(VoxelMaterials.All[material].Color);
		}

		for (int z = 0; z < n; z++)
		for (int y = 0; y < n; y++)
		for (int x = 0; x < n; x++)
		{
			var p = new Vector3I(x, y, z);
			float d0 = Density(origin.X + x, origin.Y + y, origin.Z + z);
			for (int axis = 0; axis < 3; axis++)
			{
				Vector3I ea = Unit(axis), eb = Unit((axis + 1) % 3), ec = Unit((axis + 2) % 3);
				Vector3I q = origin + p + ea;
				if ((d0 > 0f) == (Density(q.X, q.Y, q.Z) > 0f))
					continue;

				int v0 = cellVertex[Slot(p - eb - ec)], v1 = cellVertex[Slot(p - ec)], v2 = cellVertex[Slot(p)], v3 = cellVertex[Slot(p - eb)];
				if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
					continue;
				// v0→v1→v2→v3 runs counter-clockwise seen from +axis; Godot's front faces are clockwise.
				if (d0 > 0f)
					indices.AddRange([v0, v3, v2, v0, v2, v1]);
				else
					indices.AddRange([v0, v1, v2, v0, v2, v3]);
			}
		}

		return indices.Count == 0 ? null : new ChunkGeometry(vertices.ToArray(), normals.ToArray(), colors.ToArray(), indices.ToArray());

		static Vector3I Unit(int axis) => axis == 0 ? Vector3I.Right : axis == 1 ? Vector3I.Up : Vector3I.Back;
		static int Slot(Vector3I cell) => (cell.X + 1) + m * ((cell.Y + 1) + m * (cell.Z + 1));
	}

	private byte MaterialAt(int x, int y, int z) =>
		x < 0 || y < 0 || z < 0 || x >= _size || y >= _size || z >= _size ? VoxelMaterials.Stone : _material[Index(x, y, z)];

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

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = geometry.Vertices;
		arrays[(int)Mesh.ArrayType.Normal] = geometry.Normals;
		arrays[(int)Mesh.ArrayType.Color] = geometry.Colors;
		arrays[(int)Mesh.ArrayType.Index] = geometry.Indices;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		mesh.SurfaceSetMaterial(0, _surfaceMaterial);

		var faces = new Vector3[geometry.Indices.Length];
		for (int i = 0; i < faces.Length; i++)
			faces[i] = geometry.Vertices[geometry.Indices[i]];

		if (_chunkMeshes[chunk] is null)
		{
			_chunkMeshes[chunk] = new MeshInstance3D();
			AddChild(_chunkMeshes[chunk]);
		}
		_chunkMeshes[chunk]!.Mesh = mesh;

		// Replace rather than mutate the shape so the physics server rebuilds its acceleration structure.
		var shape = new ConcavePolygonShape3D { Data = faces };
		if (_chunkShapes[chunk] is null)
		{
			_chunkShapes[chunk] = new CollisionShape3D();
			AddChild(_chunkShapes[chunk]);
		}
		_chunkShapes[chunk]!.Shape = shape;
	}
}
