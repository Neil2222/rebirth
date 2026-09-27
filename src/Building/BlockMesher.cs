using System;
using System.Collections.Generic;
using Godot;

namespace Rebirth.Building;

/// <summary>
/// Turns a set of blocks into one mesh of their exposed faces. Each block draws the faces of its shape
/// (see <see cref="BlockShapes"/>), turned with the block; a face on a cell side is skipped when the
/// neighbour there covers that side completely. Used by live grids and by static models of blueprints.
/// </summary>
public static class BlockMesher
{
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

		foreach (var (cell, block) in blocks)
		{
			var geometry = BlockShapes.Get(block.Definition.Shape);
			if (geometry.Faces.Count == 0)
				continue;   // tubes and machines with their own model draw themselves
			Vector3 center = BlockGrid.CellCenter(cell);
			Basis turn = block.Orientation;
			// See block.gdshader for the channel layout.
			Color color = block.Paint.SrgbToLinear();
			var uv2 = new Vector2(health(cell), (cell.X * 73 + cell.Y * 19 + cell.Z * 7) % 101 / 101f);
			foreach (var face in geometry.Faces)
			{
				if (face.Side is { } side)
				{
					var gridSide = BlockGrid.DominantAxis(turn * (Vector3)side);
					if (blocks.TryGetValue(cell + gridSide, out var neighbour) && BlockShapes.Covers(neighbour, -gridSide))
						continue;
				}
				int b = vertices.Count;
				Vector3 tangent = turn * face.Tangent;
				Vector3 faceNormal = Vector3.Zero;
				for (int i = 0; i < face.Points.Length; i++)
				{
					vertices.Add(center + turn * (face.Points[i] * BlockGrid.CellSize));
					var n = turn * face.Normals[i];
					faceNormal += n;
					normals.Add(n);
					uvs.Add(face.Uvs[i]);
					colors.Add(color);
					uv2s.Add(uv2);
					// Tangent along +U; with w = 1 Godot derives the binormal n × u = v, i.e. along +V.
					tangents.AddRange([tangent.X, tangent.Y, tangent.Z, 1f]);
				}
				// A fan over the polygon. Godot treats clockwise triangles (seen from outside) as front-facing.
				for (int i = 1; i < face.Points.Length - 1; i++)
				{
					var p0 = vertices[b];
					var p1 = vertices[b + i];
					var p2 = vertices[b + i + 1];
					if ((p1 - p0).Cross(p2 - p0).Dot(faceNormal) > 0f)
						indices.AddRange([b, b + i + 1, b + i]);
					else
						indices.AddRange([b, b + i, b + i + 1]);
				}
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
