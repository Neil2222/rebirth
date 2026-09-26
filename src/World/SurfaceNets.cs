using System;
using System.Collections.Generic;
using Godot;

namespace Rebirth.World;

/// <summary>A density field on an integer lattice; solid where density &gt; 0.</summary>
public interface IVoxelSource
{
	/// <summary>Must be safe to call from worker threads.</summary>
	float Density(int x, int y, int z);
	byte Material(int x, int y, int z);
}

/// <summary>Terrain that can be dug out.</summary>
public interface IMinable
{
	/// <summary>Removes a sphere of material. Returns the mined volume (m³) per material index.</summary>
	float[] Carve(Vector3 worldCenter, float radius);
}

public sealed record ChunkGeometry(Vector3[] Vertices, Vector3[] Normals, Color[] Colors, int[] Indices);

/// <summary>
/// Surface Nets meshing: one vertex per cell that straddles the surface (the average of its edge
/// crossings), one quad per lattice edge that crosses it. A chunk emits vertices for cells -1..n-1
/// and quads for edges starting at points 0..n-1, so neighbouring chunks derive identical shared
/// vertices from the same field and meet without seams.
/// </summary>
public static class SurfaceNets
{
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

	/// <param name="origin">Lattice point at the chunk's minimum corner.</param>
	/// <param name="pointOffset">Local-space position of lattice point (0,0,0).</param>
	/// <returns>Null when the chunk contains no surface.</returns>
	public static ChunkGeometry? Extract(IVoxelSource source, Vector3I origin, int n, Vector3 pointOffset)
	{
		// Sample the field once: cells -1..n-1 and edges from points 0..n-1 read points -1..n. Without
		// this every point would be evaluated up to 8 times, which hurts for procedural (noise) fields.
		int s = n + 2;
		var field = new float[s * s * s];
		for (int z = -1; z <= n; z++)
		for (int y = -1; y <= n; y++)
		for (int x = -1; x <= n; x++)
			field[(x + 1) + s * ((y + 1) + s * (z + 1))] = source.Density(origin.X + x, origin.Y + y, origin.Z + z);

		int m = n + 1;
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
				d[c] = Sample(x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1));
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
			byte material = source.Material(gx + (solidest & 1), gy + ((solidest >> 1) & 1), gz + ((solidest >> 2) & 1));

			cellVertex[slot] = vertices.Count;
			vertices.Add(new Vector3(gx, gy, gz) + sum / crossings + pointOffset);
			// A perfectly symmetric cell has no gradient; normalising zero would give NaN, which bloom smears across the screen.
			normals.Add(gradient.LengthSquared() > 1e-12f ? -gradient.Normalized() : Vector3.Up);
			var voxel = VoxelMaterials.All[material];
			// Linear colour; the shader must not pow() it (pow is undefined for some inputs and the NaNs bloom).
			colors.Add(voxel.Color.SrgbToLinear() with { A = voxel.Glow });
		}

		for (int z = 0; z < n; z++)
		for (int y = 0; y < n; y++)
		for (int x = 0; x < n; x++)
		{
			var p = new Vector3I(x, y, z);
			float d0 = Sample(x, y, z);
			for (int axis = 0; axis < 3; axis++)
			{
				Vector3I ea = Unit(axis), eb = Unit((axis + 1) % 3), ec = Unit((axis + 2) % 3);
				Vector3I q = p + ea;
				if ((d0 > 0f) == (Sample(q.X, q.Y, q.Z) > 0f))
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
		int Slot(Vector3I cell) => (cell.X + 1) + m * ((cell.Y + 1) + m * (cell.Z + 1));
		float Sample(int x, int y, int z) => field[(x + 1) + s * ((y + 1) + s * (z + 1))];
	}

	public static ArrayMesh CreateMesh(ChunkGeometry geometry, Material material)
	{
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = geometry.Vertices;
		arrays[(int)Mesh.ArrayType.Normal] = geometry.Normals;
		arrays[(int)Mesh.ArrayType.Color] = geometry.Colors;
		arrays[(int)Mesh.ArrayType.Index] = geometry.Indices;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		mesh.SurfaceSetMaterial(0, material);
		return mesh;
	}

	/// <summary>Always a fresh shape: replacing it makes the physics server rebuild its acceleration structure.</summary>
	public static ConcavePolygonShape3D CreateShape(ChunkGeometry geometry)
	{
		var faces = new Vector3[geometry.Indices.Length];
		for (int i = 0; i < faces.Length; i++)
			faces[i] = geometry.Vertices[geometry.Indices[i]];
		return new ConcavePolygonShape3D { Data = faces };
	}

	/// <summary>Soft cartoon terrain material shared by all voxel terrain (see terrain.gdshader).</summary>
	public static ShaderMaterial CreateTerrainMaterial() => new() { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };
}
