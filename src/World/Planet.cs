using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Driftworks.World;

/// <summary>
/// A planet far too large for a dense voxel array. Terrain density is a procedural function
/// (radius + height(direction) - distance), with only mined points stored as sparse edits.
/// Near the camera, 16³ voxel chunks are streamed in on worker threads; everything else is drawn
/// by a low-resolution far mesh whose shader hides itself where the voxel chunks take over.
/// </summary>
public partial class Planet : StaticBody3D, IVoxelSource, IMinable
{
	public const int ChunkSize = 16;
	private const int ChunkShift = 4;               // log2(ChunkSize), floor division that works for negatives
	private const float MaxRelief = 40f;            // |height| never exceeds this
	private const float ChunkReach = ChunkSize;     // chunk center to any vertex it can emit (half diagonal + one cell)
	private const float FarMeshSink = 1.5f;         // far mesh sits below the real surface so chunks cover it
	private const int MaxChunksInFlight = 16;
	private const int MaxChunkAppliesPerFrame = 12;

	public float Radius { get; set; } = 600f;
	public int Seed { get; set; }
	public float StreamRadius { get; set; } = 150f;
	public float SurfaceGravity { get; set; } = 9.81f;

	public int LoadedChunks => _loaded.Count(kv => kv.Value.Mesh is not null);
	public int PendingChunks => _inFlight.Count;

	private FastNoiseLite _height = null!;
	private FastNoiseLite _detail = null!;
	private OreVeins _ores = null!;
	private StandardMaterial3D _terrainMaterial = null!;

	// Sparse terrain edits and the chunks they touch (those can no longer be assumed fully solid).
	private readonly ConcurrentDictionary<Vector3I, float> _edits = new();
	private readonly ConcurrentDictionary<Vector3I, byte> _editedChunks = new();

	private sealed class ChunkNodes
	{
		public MeshInstance3D? Mesh;
		public CollisionShape3D? Shape;
	}

	private readonly Dictionary<Vector3I, ChunkNodes> _loaded = new();
	private readonly HashSet<Vector3I> _inFlight = new();
	private readonly ConcurrentQueue<(Vector3I Chunk, ChunkGeometry? Geometry)> _finished = new();
	private readonly HashSet<Vector3I> _dirty = new();
	private List<Vector3I> _wanted = new();
	private double _nextStreamingUpdate;

	public override void _Ready()
	{
		_height = new FastNoiseLite { Seed = Seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f / 220f, FractalOctaves = 5 };
		_detail = new FastNoiseLite { Seed = Seed + 1, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.12f };
		_ores = new OreVeins(Seed + 2);
		_terrainMaterial = SurfaceNets.CreateTerrainMaterial();

		AddChild(BuildFarMesh());
		AddChild(BuildAtmosphere());
		AddChild(BuildGravityField());
	}

	/// <summary>Terrain height above <see cref="Radius"/> for a unit direction from the center.</summary>
	private float Height(Vector3 direction) => MaxRelief * 0.85f * _height.GetNoise3Dv(direction * Radius);

	public float Density(int x, int y, int z)
	{
		var point = new Vector3I(x, y, z);
		if (_edits.TryGetValue(point, out float edited))
			return edited;
		var p = new Vector3(x, y, z);
		float r = p.Length();
		if (r < 1f)
			return Radius;
		return Radius + Height(p / r) - r + 1.2f * _detail.GetNoise3Dv(p);
	}

	public byte Material(int x, int y, int z)
	{
		var p = new Vector3(x, y, z);
		float depth = Radius + Height(p.Normalized()) - p.Length();
		return depth < 1f ? VoxelMaterials.Regolith : _ores.At(p, depth < 4f ? VoxelMaterials.Regolith : VoxelMaterials.Stone);
	}

	public float[] Carve(Vector3 worldCenter, float radius)
	{
		var mined = new float[VoxelMaterials.All.Count];
		Vector3 c = ToLocal(worldCenter);
		var min = (Vector3I)(c - Vector3.One * (radius + 1f)).Floor();
		var max = (Vector3I)(c + Vector3.One * (radius + 1f)).Ceil();
		for (int z = min.Z; z <= max.Z; z++)
		for (int y = min.Y; y <= max.Y; y++)
		for (int x = min.X; x <= max.X; x++)
		{
			float d = Density(x, y, z);
			float carved = Mathf.Min(d, new Vector3(x, y, z).DistanceTo(c) - radius);
			if (carved >= d)
				continue;
			mined[Material(x, y, z)] += Mathf.Clamp(d, 0f, 1f) - Mathf.Clamp(carved, 0f, 1f);
			_edits[new Vector3I(x, y, z)] = carved;
			MarkEdited(x, y, z);
		}
		return mined;
	}

	/// <summary>A lattice point is read by the chunks of point-1..point+1 on each axis (see SurfaceNets).</summary>
	private void MarkEdited(int x, int y, int z)
	{
		for (int dz = -1; dz <= 1; dz++)
		for (int dy = -1; dy <= 1; dy++)
		for (int dx = -1; dx <= 1; dx++)
		{
			var chunk = new Vector3I((x + dx) >> ChunkShift, (y + dy) >> ChunkShift, (z + dz) >> ChunkShift);
			_editedChunks[chunk] = 0;
			_dirty.Add(chunk);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Edits must show up immediately (the drill stands in the hole), so loaded chunks rebuild synchronously.
		if (_dirty.Count == 0)
			return;
		foreach (var chunk in _dirty.ToArray())
		{
			if (_inFlight.Contains(chunk))
				continue; // rebuilt once the in-flight result lands
			_dirty.Remove(chunk);
			if (_loaded.ContainsKey(chunk))
				Apply(chunk, ComputeChunk(chunk));
		}
	}

	public override void _Process(double delta)
	{
		Vector3 camera = ToLocal(GetViewport().GetCamera3D()?.GlobalPosition ?? GlobalPosition);

		if (Time.GetTicksMsec() / 1000.0 >= _nextStreamingUpdate)
		{
			_nextStreamingUpdate = Time.GetTicksMsec() / 1000.0 + 0.25;
			_wanted = WantedChunks(camera);
			UnloadFarChunks(camera);
		}

		foreach (var chunk in _wanted)
		{
			if (_inFlight.Count >= MaxChunksInFlight)
				break;
			if (_loaded.ContainsKey(chunk) || !_inFlight.Add(chunk))
				continue;
			Task.Run(() => _finished.Enqueue((chunk, ComputeChunk(chunk))));
		}

		for (int i = 0; i < MaxChunkAppliesPerFrame && _finished.TryDequeue(out var result); i++)
		{
			_inFlight.Remove(result.Chunk);
			if (ChunkCenter(result.Chunk).DistanceTo(camera) > StreamRadius + 48f)
				continue; // camera moved on while it was being built
			Apply(result.Chunk, result.Geometry);
		}
	}

	private static Vector3 ChunkCenter(Vector3I chunk) => (Vector3)(chunk * ChunkSize) + Vector3.One * (ChunkSize * 0.5f);

	/// <summary>Chunks near the camera that can intersect the terrain shell, nearest first.</summary>
	private List<Vector3I> WantedChunks(Vector3 camera)
	{
		var wanted = new List<(float Distance, Vector3I Chunk)>();
		var min = (Vector3I)((camera - Vector3.One * StreamRadius) / ChunkSize).Floor();
		var max = (Vector3I)((camera + Vector3.One * StreamRadius) / ChunkSize).Floor();
		for (int z = min.Z; z <= max.Z; z++)
		for (int y = min.Y; y <= max.Y; y++)
		for (int x = min.X; x <= max.X; x++)
		{
			var chunk = new Vector3I(x, y, z);
			Vector3 center = ChunkCenter(chunk);
			float distance = center.DistanceTo(camera);
			if (distance > StreamRadius)
				continue;
			float r = center.Length();
			if (r - ChunkReach > Radius + MaxRelief + 2f)
				continue; // all sky
			if (r + ChunkReach < Radius - MaxRelief - 2f && !_editedChunks.ContainsKey(chunk))
				continue; // all rock
			wanted.Add((distance, chunk));
		}
		return wanted.OrderBy(w => w.Distance).Select(w => w.Chunk).ToList();
	}

	private void UnloadFarChunks(Vector3 camera)
	{
		foreach (var chunk in _loaded.Keys.ToArray())
		{
			if (ChunkCenter(chunk).DistanceTo(camera) <= StreamRadius + 48f)
				continue;
			var nodes = _loaded[chunk];
			nodes.Mesh?.QueueFree();
			nodes.Shape?.QueueFree();
			_loaded.Remove(chunk);
		}
	}

	/// <summary>
	/// Worker-thread safe. Samples the height at the chunk's corners first: most shell chunks lie
	/// wholly above or below the local surface and need no per-voxel work at all.
	/// </summary>
	private ChunkGeometry? ComputeChunk(Vector3I chunk)
	{
		if (!_editedChunks.ContainsKey(chunk))
		{
			bool anyAbove = false, anyBelow = false;
			for (int c = 0; c < 9; c++)
			{
				Vector3 p = c < 8
					? (Vector3)(chunk * ChunkSize) + new Vector3(c & 1, (c >> 1) & 1, (c >> 2) & 1) * ChunkSize
					: ChunkCenter(chunk);
				float altitude = p.Length() - (Radius + Height(p.Normalized()));
				// A generous margin covers the terrain varying between the samples.
				anyAbove |= altitude > -ChunkSize;
				anyBelow |= altitude < ChunkSize;
			}
			if (!anyAbove || !anyBelow)
				return null;
		}
		return SurfaceNets.Extract(this, chunk * ChunkSize, ChunkSize, Vector3.Zero);
	}

	private void Apply(Vector3I chunk, ChunkGeometry? geometry)
	{
		if (!_loaded.TryGetValue(chunk, out var nodes))
			_loaded[chunk] = nodes = new ChunkNodes();

		if (geometry is null)
		{
			nodes.Mesh?.QueueFree();
			nodes.Shape?.QueueFree();
			nodes.Mesh = null;
			nodes.Shape = null;
			return;
		}

		if (nodes.Mesh is null)
		{
			nodes.Mesh = new MeshInstance3D();
			AddChild(nodes.Mesh);
		}
		nodes.Mesh.Mesh = SurfaceNets.CreateMesh(geometry, _terrainMaterial);

		if (nodes.Shape is null)
		{
			nodes.Shape = new CollisionShape3D();
			AddChild(nodes.Shape);
		}
		nodes.Shape.Shape = SurfaceNets.CreateShape(geometry);
	}

	private MeshInstance3D BuildFarMesh()
	{
		var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 256, Rings = 128 };
		var arrays = sphere.GetMeshArrays();
		var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
		var colors = new Color[vertices.Length];
		Color low = VoxelMaterials.All[VoxelMaterials.Regolith].Color.Darkened(0.25f);
		Color high = VoxelMaterials.All[VoxelMaterials.Regolith].Color.Lightened(0.15f);
		for (int i = 0; i < vertices.Length; i++)
		{
			Vector3 dir = vertices[i].Normalized();
			float h = Height(dir);
			vertices[i] = dir * (Radius + h - FarMeshSink);
			colors[i] = low.Lerp(high, Mathf.Clamp(0.5f + h / (2f * MaxRelief), 0f, 1f));
		}

		// Smooth normals from face normals, flipped outward where the winding disagrees.
		var normals = new Vector3[vertices.Length];
		for (int i = 0; i < indices.Length; i += 3)
		{
			Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
			Vector3 n = (b - a).Cross(c - a);
			normals[indices[i]] += n;
			normals[indices[i + 1]] += n;
			normals[indices[i + 2]] += n;
		}
		for (int i = 0; i < normals.Length; i++)
		{
			Vector3 n = normals[i].Normalized();
			normals[i] = n.Dot(vertices[i]) < 0f ? -n : n;
		}

		var mesh = new ArrayMesh();
		var surface = new Godot.Collections.Array();
		surface.Resize((int)Mesh.ArrayType.Max);
		surface[(int)Mesh.ArrayType.Vertex] = vertices;
		surface[(int)Mesh.ArrayType.Normal] = normals;
		surface[(int)Mesh.ArrayType.Color] = colors;
		surface[(int)Mesh.ArrayType.Index] = indices;
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface);

		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/planet_far.gdshader") };
		material.SetShaderParameter("near_radius", StreamRadius - 24f);
		mesh.SurfaceSetMaterial(0, material);
		// Its fragments are discarded around the camera, which would punch holes in shadow maps.
		return new MeshInstance3D { Name = "FarMesh", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
	}

	private MeshInstance3D BuildAtmosphere()
	{
		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/atmosphere.gdshader") };
		return new MeshInstance3D
		{
			Name = "Atmosphere",
			Mesh = new SphereMesh { Radius = Radius * 1.08f, Height = Radius * 2.16f, RadialSegments = 96, Rings = 48, Material = material },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	/// <summary>1 g at the surface, falling off with the square of the distance, out to 2.5 radii.</summary>
	private Area3D BuildGravityField()
	{
		var area = new Area3D
		{
			Name = "Gravity",
			GravitySpaceOverride = Area3D.SpaceOverride.Combine,
			GravityPoint = true,
			GravityPointCenter = Vector3.Zero,
			GravityPointUnitDistance = Radius,
			Gravity = SurfaceGravity,
		};
		area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius * 2.5f } });
		return area;
	}
}
