using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Rebirth.Building;

/// <summary>
/// The shape a block fills its cell with. Frame blocks come in several; <see cref="Custom"/> blocks draw
/// their whole look as a model (machines with their own silhouette), and <see cref="None"/> draws nothing
/// in the grid mesh (tubes build their own pipes).
/// </summary>
public enum BlockShape { Cube, Slope, Corner, InnerCorner, Half, Rounded, Cylinder, Custom, None }

/// <summary>
/// Geometry of every shape in a unit cell (-0.5..0.5), block-local: -Z is the front, +Y is up. From one
/// description come the mesh faces (with UVs for the toy-block shader), the collision hull, and which
/// sides are completely covered (so neighbouring faces can be hidden).
/// </summary>
public static class BlockShapes
{
	/// <summary>One flat or curved patch of a shape's surface.</summary>
	/// <param name="Side">The cell side the face lies on (for hiding it against a neighbour), or null.</param>
	/// <param name="Tangent">Direction of +U, for the shader's bevel.</param>
	public sealed record Face(Vector3[] Points, Vector3[] Normals, Vector2[] Uvs, Vector3I? Side, Vector3 Tangent);

	public sealed record Geometry(IReadOnlyList<Face> Faces, Vector3[] Hull, IReadOnlySet<Vector3I> FullSides, float Volume);

	private static readonly Dictionary<BlockShape, Geometry> Cache = new();
	private static readonly Dictionary<BlockShape, Shape3D> Colliders = new();

	public static readonly Vector3I[] Sides = [Vector3I.Right, Vector3I.Left, Vector3I.Up, Vector3I.Down, Vector3I.Back, Vector3I.Forward];

	/// <summary>Friendly names for the shape picker.</summary>
	public static string Name(BlockShape shape) => shape switch
	{
		BlockShape.Slope => "Slope",
		BlockShape.Corner => "Corner",
		BlockShape.InnerCorner => "Inner corner",
		BlockShape.Half => "Half",
		BlockShape.Rounded => "Rounded",
		BlockShape.Cylinder => "Pillar",
		_ => "Cube",
	};

	public static Geometry Get(BlockShape shape)
	{
		if (Cache.TryGetValue(shape, out var geometry))
			return geometry;
		geometry = shape switch
		{
			BlockShape.Cube => Box(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f)),
			BlockShape.Half => Box(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0f, 0.5f)),
			BlockShape.Slope => Slope(),
			BlockShape.Corner => Corner(),
			BlockShape.InnerCorner => InnerCorner(),
			BlockShape.Rounded => Rounded(),
			BlockShape.Cylinder => Cylinder(),
			_ => new Geometry([], [], new HashSet<Vector3I>(), 1f),
		};
		Cache[shape] = geometry;
		return geometry;
	}

	/// <summary>Whether <paramref name="block"/> fully covers the given grid side of its cell.</summary>
	public static bool Covers(PlacedBlock block, Vector3I gridSide)
	{
		var full = Get(block.Definition.Shape).FullSides;
		if (full.Count == 0)
			return false;
		if (full.Count == 6)
			return true;
		var local = BlockGrid.DominantAxis(block.Orientation.Inverse() * (Vector3)gridSide);
		return full.Contains(local);
	}

	/// <summary>Collision for a block's cell; cubes and machines use a plain box.</summary>
	public static Shape3D Collider(BlockShape shape, Shape3D box)
	{
		if (shape is BlockShape.Cube or BlockShape.Custom or BlockShape.None)
			return box;
		if (Colliders.TryGetValue(shape, out var collider))
			return collider;
		// A hair smaller than the cell, like the box, so touching grids don't snag on each other.
		var points = Get(shape).Hull.Select(p => p * BlockGrid.CellSize * 0.99f).ToArray();
		collider = new ConvexPolygonShape3D { Points = points };
		Colliders[shape] = collider;
		return collider;
	}

	// ------------------------------------------------------------ building blocks

	/// <summary>A flat polygon (convex, any winding) with planar UVs spanning 0..1 across it.</summary>
	private static Face Flat(Vector3 inside, params Vector3[] points)
	{
		var normal = (points[1] - points[0]).Cross(points[2] - points[0]).Normalized();
		var centre = points.Aggregate(Vector3.Zero, (a, p) => a + p) / points.Length;
		if (normal.Dot(centre - inside) < 0f)
			normal = -normal;
		// +U along the X axis where possible (Z when X points out of the face), +V = normal × U.
		var axis = Mathf.Abs(normal.X) > 0.9f ? Vector3.Back : Vector3.Right;
		var u = (axis - normal * axis.Dot(normal)).Normalized();
		var v = normal.Cross(u);
		float minU = points.Min(p => p.Dot(u)), maxU = points.Max(p => p.Dot(u));
		float minV = points.Min(p => p.Dot(v)), maxV = points.Max(p => p.Dot(v));
		var uvs = points.Select(p => new Vector2((p.Dot(u) - minU) / Mathf.Max(maxU - minU, 1e-4f), (p.Dot(v) - minV) / Mathf.Max(maxV - minV, 1e-4f))).ToArray();
		return new Face(points, points.Select(_ => normal).ToArray(), uvs, SideOf(points), u);
	}

	/// <summary>The cell side all points lie on, if any.</summary>
	private static Vector3I? SideOf(Vector3[] points)
	{
		foreach (var side in Sides)
			if (points.All(p => Mathf.IsEqualApprox(p.Dot(side), 0.5f)))
				return side;
		return null;
	}

	private static Geometry Box(Vector3 min, Vector3 max)
	{
		var c = (min + max) * 0.5f;
		Vector3 P(float x, float y, float z) => new(x < 0 ? min.X : max.X, y < 0 ? min.Y : max.Y, z < 0 ? min.Z : max.Z);
		var faces = new List<Face>
		{
			Flat(c, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1)),
			Flat(c, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)),
			Flat(c, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1)),
			Flat(c, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)),
			Flat(c, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1)),
			Flat(c, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1)),
		};
		var hull = new[] { P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1) };
		var full = Sides.Where(s => faces.Any(f => f.Side == s && Area(f) > 0.99f)).ToHashSet();
		var size = max - min;
		return new Geometry(faces, hull, full, size.X * size.Y * size.Z);
	}

	private static float Area(Face face)
	{
		float area = 0f;
		for (int i = 1; i < face.Points.Length - 1; i++)
			area += (face.Points[i] - face.Points[0]).Cross(face.Points[i + 1] - face.Points[0]).Length() * 0.5f;
		return area;
	}

	private static readonly Vector3 A = new(-0.5f, -0.5f, -0.5f), B = new(0.5f, -0.5f, -0.5f), C = new(0.5f, -0.5f, 0.5f), D = new(-0.5f, -0.5f, 0.5f);
	private static readonly Vector3 E = new(-0.5f, 0.5f, -0.5f), F = new(0.5f, 0.5f, -0.5f), G = new(0.5f, 0.5f, 0.5f), H = new(-0.5f, 0.5f, 0.5f);

	/// <summary>A ramp rising from the front edge to the full back face.</summary>
	private static Geometry Slope()
	{
		var inside = new Vector3(0f, -0.2f, 0.2f);
		return new Geometry(
		[
			Flat(inside, A, B, C, D),       // bottom
			Flat(inside, D, C, G, H),       // back
			Flat(inside, A, B, G, H),       // ramp
			Flat(inside, A, D, H),          // left
			Flat(inside, B, C, G),          // right
		], [A, B, C, D, G, H], new HashSet<Vector3I> { Vector3I.Down, Vector3I.Back }, 0.5f);
	}

	/// <summary>A pyramid: square base, peak above the back-left corner.</summary>
	private static Geometry Corner()
	{
		var inside = new Vector3(-0.25f, -0.3f, 0.25f);
		return new Geometry(
		[
			Flat(inside, A, B, C, D),
			Flat(inside, C, D, H),
			Flat(inside, D, A, H),
			Flat(inside, A, B, H),
			Flat(inside, B, C, H),
		], [A, B, C, D, H], new HashSet<Vector3I> { Vector3I.Down }, 1f / 3f);
	}

	/// <summary>A cube with its top-front-right corner cut off.</summary>
	private static Geometry InnerCorner()
	{
		var inside = new Vector3(-0.1f, -0.1f, 0.1f);
		return new Geometry(
		[
			Flat(inside, A, B, C, D),       // bottom
			Flat(inside, D, C, G, H),       // back
			Flat(inside, A, D, H, E),       // left
			Flat(inside, E, H, G),          // top (triangle)
			Flat(inside, B, C, G),          // right (triangle)
			Flat(inside, A, B, E),          // front (triangle)
			Flat(inside, B, G, E),          // the cut
		], [A, B, C, D, E, G, H], new HashSet<Vector3I> { Vector3I.Down, Vector3I.Back, Vector3I.Left }, 5f / 6f);
	}

	/// <summary>Like the slope, but the ramp bulges out in a quarter circle: a soft rounded edge.</summary>
	private static Geometry Rounded()
	{
		const int segments = 8;
		var inside = new Vector3(0f, -0.2f, 0.2f);
		// Arc in the YZ plane around the back-bottom edge, from the front-bottom to the back-top.
		Vector3 Arc(float x, int i, out Vector3 normal)
		{
			float t = i * Mathf.Pi / 2f / segments;
			normal = new Vector3(0f, Mathf.Sin(t), -Mathf.Cos(t));
			return new Vector3(x, -0.5f + Mathf.Sin(t), 0.5f - Mathf.Cos(t));
		}
		var faces = new List<Face> { Flat(inside, A, B, C, D), Flat(inside, D, C, G, H) };
		for (int i = 0; i < segments; i++)
		{
			var p0 = Arc(-0.5f, i, out var n0);
			var p1 = Arc(0.5f, i, out _);
			var p2 = Arc(0.5f, i + 1, out var n2);
			var p3 = Arc(-0.5f, i + 1, out _);
			float v0 = (float)i / segments, v1 = (float)(i + 1) / segments;
			faces.Add(new Face([p0, p1, p2, p3], [n0, n0, n2, n2], [new(0, v0), new(1, v0), new(1, v1), new(0, v1)], null, Vector3.Right));
		}
		// The two quarter-circle sides.
		foreach (float x in new[] { -0.5f, 0.5f })
		{
			var points = new List<Vector3> { new(x, -0.5f, 0.5f) };
			for (int i = 0; i <= segments; i++)
				points.Add(Arc(x, i, out _));
			faces.Add(Flat(inside, points.ToArray()));
		}
		var hull = faces.SelectMany(f => f.Points).Distinct().ToArray();
		return new Geometry(faces, hull, new HashSet<Vector3I> { Vector3I.Down, Vector3I.Back }, Mathf.Pi / 4f);
	}

	/// <summary>A round pillar standing on the cell floor, touching the sides.</summary>
	private static Geometry Cylinder()
	{
		const int segments = 20;
		var faces = new List<Face>();
		var ring = Enumerable.Range(0, segments).Select(i => i * Mathf.Tau / segments).ToArray();
		for (int i = 0; i < segments; i++)
		{
			float a0 = ring[i], a1 = ring[(i + 1) % segments];
			var n0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
			var n1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
			float u0 = (float)i / segments, u1 = (float)(i + 1) / segments;
			faces.Add(new Face(
				[n0 * 0.5f + Vector3.Down * 0.5f, n1 * 0.5f + Vector3.Down * 0.5f, n1 * 0.5f + Vector3.Up * 0.5f, n0 * 0.5f + Vector3.Up * 0.5f],
				[n0, n1, n1, n0], [new(u0, 0), new(u1, 0), new(u1, 1), new(u0, 1)], null, n0.Cross(Vector3.Up).Normalized()));
		}
		foreach (float y in new[] { -0.5f, 0.5f })
			faces.Add(Flat(Vector3.Zero, ring.Select(a => new Vector3(Mathf.Cos(a) * 0.5f, y, Mathf.Sin(a) * 0.5f)).ToArray()));
		var hull = faces.SelectMany(f => f.Points).Distinct().ToArray();
		return new Geometry(faces, hull, new HashSet<Vector3I>(), Mathf.Pi / 4f);
	}
}
