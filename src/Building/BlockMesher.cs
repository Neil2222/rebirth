using System;
using System.Collections.Generic;
using Godot;

namespace Rebirth.Building;

/// <summary>
/// Turns a set of blocks into one mesh of their exposed faces (faces between neighbouring blocks
/// are skipped). Used by live grids and by static models of blueprints.
/// </summary>
public static class BlockMesher
{
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

	private static ShaderMaterial? _material;

	/// <summary>The toy-block material shared by every block mesh (see block.gdshader).</summary>
	public static ShaderMaterial Material => _material ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/block.gdshader") };

	/// <param name="health">Integrity fraction (0..1) per cell; damaged blocks get scorched.</param>
	/// <param name="material">Defaults to <see cref="Material"/>.</param>
	/// <returns>Null when there is nothing to draw.</returns>
	public static ArrayMesh? Build(IReadOnlyDictionary<Vector3I, PlacedBlock> blocks, Func<Vector3I, float> health, Material? material = null)
	{
		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var colors = new List<Color>();
		var uvs = new List<Vector2>();
		var uv2s = new List<Vector2>();
		var tangents = new List<float>();
		var indices = new List<int>();
		const float h = BlockGrid.CellSize * 0.5f;

		foreach (var (cell, block) in blocks)
		{
			Vector3 center = BlockGrid.CellCenter(cell);
			// See block.gdshader for the channel layout.
			Color color = block.Paint.SrgbToLinear();
			var uv2 = new Vector2(health(cell), (cell.X * 73 + cell.Y * 19 + cell.Z * 7) % 101 / 101f);
			foreach (var (dir, u, v) in Faces)
			{
				if (blocks.ContainsKey(cell + dir))
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
					uv2s.Add(uv2);
					// Tangent along +U; with w = 1 Godot derives the binormal n × u = v, i.e. along +V.
					tangents.AddRange([u.X, u.Y, u.Z, 1f]);
				}
				// Godot treats clockwise triangles as front-facing.
				indices.AddRange([b, b + 2, b + 1, b, b + 3, b + 2]);
			}
		}

		if (indices.Count == 0)
			return null;

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.Tangent] = tangents.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV2] = uv2s.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		mesh.SurfaceSetMaterial(0, material ?? Material);
		return mesh;
	}
}
