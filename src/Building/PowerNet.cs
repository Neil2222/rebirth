using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Rebirth.Building;

/// <summary>
/// Joins stations into shared power networks: two stations whose Power Pylons stand within
/// <see cref="BlockGrid.PylonReach"/> of each other are cabled together (a pylon also reaches stations
/// without one, so old sites can be plugged in), and everything cabled together
/// shares its generators and batteries as if it were one grid. Only stations (anchored grids) take part.
/// Also draws the cables: gently sagging lines from pylon top to pylon top.
/// </summary>
public static class PowerNet
{
	/// <summary>Totals for a whole network (one station on its own counts as a network of one).</summary>
	public readonly record struct Summary(int Stations, float Generation, float Demand, float Stored, float Capacity);

	private static readonly HashSet<BlockGrid> Members = new();
	private static readonly Dictionary<BlockGrid, List<BlockGrid>> NetOf = new();
	private static readonly List<(Vector3 A, Vector3 B, Vector3 Down)> Cables = new();
	private static ulong _frame = ulong.MaxValue;
	private static MeshInstance3D? _cableMesh;
	private static string _cableKey = "";

	/// <summary>Called when a grid's blocks change: it takes part while it has a pylon.</summary>
	public static void Register(BlockGrid grid, bool hasPylon)
	{
		if (hasPylon)
			Members.Add(grid);
		else
			Members.Remove(grid);
	}

	/// <summary>
	/// Balances every cabled network once per physics tick. Returns true when <paramref name="grid"/> is part
	/// of a network of two or more stations (so it must not balance itself alone).
	/// </summary>
	public static bool Tick(BlockGrid grid, float dt)
	{
		ulong frame = Engine.GetPhysicsFrames();
		if (frame != _frame)
		{
			_frame = frame;
			// Regrouping walks every station; a few times a second is plenty for things that barely move.
			if (frame % 15 == 0 || NetOf.Keys.Any(g => !Live(g)))
			{
				Regroup();
				DrawCables();
			}
			foreach (var net in NetOf.Values.Distinct())
				BlockGrid.Balance(net, dt);
		}
		return NetOf.ContainsKey(grid);
	}

	/// <summary>The stations cabled to <paramref name="grid"/>, itself included (just itself when alone).</summary>
	public static IReadOnlyList<BlockGrid> Network(BlockGrid grid) => NetOf.TryGetValue(grid, out var net) ? net : [grid];

	private static bool Live(BlockGrid grid) =>
		GodotObject.IsInstanceValid(grid) && grid.IsInsideTree() && !grid.IsQueuedForDeletion() && grid.IsStatic && !grid.IsBot && grid.BlockCount > 0;

	/// <summary>
	/// Every few ticks: strings cables (shortest first, one per pair of not-yet-joined stations) between
	/// pylons in reach of each other, and from pylons to stations without one of their own, and groups the
	/// stations they join.
	/// </summary>
	private static void Regroup()
	{
		Members.RemoveWhere(g => !GodotObject.IsInstanceValid(g) || g.IsQueuedForDeletion());
		NetOf.Clear();
		Cables.Clear();
		var live = Members.Where(Live).ToList();
		if (live.Count == 0)
			return;
		var pylons = live.SelectMany(g => g.Pylons.Select(cell => (Grid: g, Top: g.PylonTop(cell), Up: g.PylonUp(cell)))).ToList();
		// Stations without a pylon plug in at their roof.
		var plugs = live[0].GetParent().GetChildren().OfType<BlockGrid>().Where(g => Live(g) && g.Pylons.Count == 0)
			.Select(g => (Grid: g, Top: g.RoofPoint(), Up: g.GlobalBasis.Y)).ToList();

		var links = new List<(float Distance, (BlockGrid Grid, Vector3 Top, Vector3 Up) A, (BlockGrid Grid, Vector3 Top, Vector3 Up) B)>();
		for (int i = 0; i < pylons.Count; i++)
		{
			for (int j = i + 1; j < pylons.Count; j++)
				if (pylons[i].Grid != pylons[j].Grid && pylons[i].Top.DistanceTo(pylons[j].Top) is var d && d <= BlockGrid.PylonReach)
					links.Add((d, pylons[i], pylons[j]));
			foreach (var plug in plugs)
				if (pylons[i].Top.DistanceTo(plug.Top) is var d && d <= BlockGrid.PylonReach)
					links.Add((d, pylons[i], plug));
		}
		if (links.Count == 0)
			return;

		// Kruskal over stations: the shortest cable between two not-yet-joined stations is the one strung.
		var parent = new Dictionary<BlockGrid, BlockGrid>();
		BlockGrid Find(BlockGrid g)
		{
			while (parent.TryGetValue(g, out var p) && p != g)
				g = p;
			return g;
		}
		foreach (var (_, a, b) in links.OrderBy(l => l.Distance))
		{
			var (ga, gb) = (Find(a.Grid), Find(b.Grid));
			if (ga == gb)
				continue;
			parent[ga] = gb;
			parent.TryAdd(gb, gb);
			Cables.Add((a.Top, b.Top, -(a.Up + b.Up).Normalized()));
		}
		foreach (var group in parent.Keys.GroupBy(Find))
		{
			var net = group.ToList();
			foreach (var grid in net)
				NetOf[grid] = net;
		}
	}

	// ------------------------------------------------------------ cables

	private static void DrawCables()
	{
		string key = string.Join(";", Cables.Select(c => $"{c.A.X:0.0},{c.A.Y:0.0},{c.A.Z:0.0}-{c.B.X:0.0},{c.B.Y:0.0},{c.B.Z:0.0}"));
		if (_cableMesh is not null && !GodotObject.IsInstanceValid(_cableMesh))
			_cableMesh = null;
		if (key == _cableKey && _cableMesh is not null)
			return;
		_cableKey = key;
		if (Cables.Count == 0)
		{
			_cableMesh?.QueueFree();
			_cableMesh = null;
			return;
		}
		if (_cableMesh is null)
		{
			var world = Members.First(Live).GetParent();
			_cableMesh = new MeshInstance3D { Name = "PowerCables", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			world.AddChild(_cableMesh);
		}
		_cableMesh.Mesh = BuildCables();
	}

	private static ArrayMesh BuildCables()
	{
		const int segments = 20, sides = 6;
		const float radius = 0.07f;
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		st.SetMaterial(new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.2f, 0.24f), Roughness = 0.55f });
		foreach (var (a, b, down) in Cables)
		{
			float sag = 0.6f + a.DistanceTo(b) * 0.05f;
			var points = new Vector3[segments + 1];
			for (int i = 0; i <= segments; i++)
			{
				float t = (float)i / segments;
				points[i] = a.Lerp(b, t) + down * (sag * 4f * t * (1f - t));
			}
			for (int i = 0; i < segments; i++)
			{
				Vector3 dir = (points[i + 1] - points[i]).Normalized();
				Vector3 side = dir.Cross(Mathf.Abs(dir.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
				Vector3 up = side.Cross(dir);
				for (int k = 0; k < sides; k++)
				{
					float a0 = k * Mathf.Tau / sides, a1 = (k + 1) * Mathf.Tau / sides;
					Vector3 n0 = side * Mathf.Cos(a0) + up * Mathf.Sin(a0);
					Vector3 n1 = side * Mathf.Cos(a1) + up * Mathf.Sin(a1);
					Vector3 p00 = points[i] + n0 * radius, p01 = points[i] + n1 * radius;
					Vector3 p10 = points[i + 1] + n0 * radius, p11 = points[i + 1] + n1 * radius;
					foreach (var (p, n) in new[] { (p00, n0), (p10, n0), (p11, n1), (p00, n0), (p11, n1), (p01, n1) })
					{
						st.SetNormal(n);
						st.AddVertex(p);
					}
				}
			}
		}
		return st.Commit();
	}
}
