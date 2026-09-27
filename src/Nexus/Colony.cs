using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Items;
using Rebirth.Persistence;
using Rebirth.World;

namespace Rebirth.Nexus;

/// <summary>
/// Everything the Nexus runs for you: your worker bots, the sites they are building, and the haul
/// routes that bring the goods home. Building this way costs <see cref="AutoBuildCostFactor"/>× the
/// ingots of placing the blocks yourself; in return you only choose a design and a spot.
/// </summary>
public partial class Colony : Node
{
	public const string HomeLabel = "Home";
	public const float AutoBuildCostFactor = 2.5f;
	public const float BuildSecondsPerBlock = 1.2f;
	public const float LoadSeconds = 1.5f;
	public const float BaseBotSpeed = 16f;       // m/s with no thrusters of its own
	public const float MaxBotSpeed = 40f;
	private const float HaulMinimum = 50f;       // kg worth a trip
	private const float SiteSpacing = 22f;       // m between sites on the same body

	/// <summary>World parent of grids and terrain.</summary>
	public Node3D World { get; set; } = null!;

	public List<Bot> Bots { get; } = new();
	public List<ConstructionJob> Jobs { get; } = new();
	public List<HaulRoute> Routes { get; } = new();

	/// <summary>Grid the player is piloting, which bots leave alone.</summary>
	public System.Func<BlockGrid?> PilotedGrid { get; set; } = () => null;

	/// <summary>Short news for the HUD ("Drill Site on Dune is running").</summary>
	public event System.Action<string>? News;

	private float _scanTimer;

	// ------------------------------------------------------------ queries

	public IEnumerable<BlockGrid> Grids => World.GetChildren().OfType<BlockGrid>().Where(g => !g.IsQueuedForDeletion() && g.BlockCount > 0);

	/// <summary>The base the bots work from; old worlds adopt their first station with a fabricator.</summary>
	public BlockGrid? Home
	{
		get
		{
			var home = Grids.FirstOrDefault(g => g.Label == HomeLabel);
			if (home is null && Grids.FirstOrDefault(g => g.IsStatic && !g.IsBot && g.HasBlock(BlockKind.Fabricator)) is { } candidate)
			{
				candidate.Label = HomeLabel;
				home = candidate;
			}
			return home;
		}
	}

	/// <summary>Named stations other than home: the places bots built or you named.</summary>
	public IEnumerable<BlockGrid> Sites => Grids.Where(g => g.IsStatic && !g.IsBot && g.Label is not null && g.Label != HomeLabel && Jobs.All(j => j.Grid != g));

	public IEnumerable<VoxelBody> Bodies => World.GetChildren().OfType<VoxelBody>();

	public BlockGrid? Find(string label) => Grids.FirstOrDefault(g => g.Label == label);

	/// <summary>Ingots needed to have bots build <paramref name="design"/>.</summary>
	public static Dictionary<string, float> AutoBuildCost(Blueprint design) =>
		design.TotalCost().ToDictionary(kv => kv.Key, kv => kv.Value * AutoBuildCostFactor);

	// ------------------------------------------------------------ orders

	/// <summary>Queues a build of <paramref name="design"/> on <paramref name="body"/> at <paramref name="site"/>.</summary>
	public ConstructionJob OrderBuild(Blueprint design, VoxelBody body, Transform3D site)
	{
		int number = 1;
		string baseName = $"{body.Name} {design.Name}";
		while (Find($"{baseName} {number}") is not null || Jobs.Any(j => j.Name == $"{baseName} {number}"))
			number++;
		var job = new ConstructionJob(design, $"{baseName} {number}", body.Name, site);
		Jobs.Add(job);
		ShowHologram(job);
		return job;
	}

	public void CancelJob(ConstructionJob job)
	{
		// Whatever was delivered but not yet used goes back home.
		if (Home is { } home)
			foreach (var (item, amount) in job.Stock.Items.ToArray())
				home.Inventory.Add(item, amount);
		job.Hologram?.QueueFree();
		Jobs.Remove(job);
		foreach (var bot in Bots.Where(b => b.Job == job))
			bot.Abort();
		if (job.Grid is { } grid)
			grid.Label = job.Name;   // what was built so far stays as a site
	}

	/// <summary>
	/// A spot on <paramref name="body"/> for <paramref name="design"/>: upright on the surface, its lowest
	/// blocks just clear of the ground. Aims at <paramref name="near"/> (world) when given, else the sunny
	/// side facing home, and steps around until it is clear of other sites.
	/// </summary>
	public Transform3D SuggestSite(VoxelBody body, Blueprint design, Vector3? near = null)
	{
		Vector3 center = body.GlobalPosition;
		Vector3 aim = near is { } point
			? point - center
			: Sun.Direction + (Home is { } home ? (home.GlobalPosition - center).Normalized() * 0.6f : Vector3.Zero);
		if (aim.LengthSquared() < 1e-4f)
			aim = Vector3.Up;
		aim = aim.Normalized();

		// Spiral outward from the aim until the spot is clear of every other site and job.
		var others = Grids.Where(g => g.IsStatic && !g.IsBot).Select(g => g.GlobalPosition)
			.Concat(Jobs.Select(j => j.Site.Origin)).ToList();
		Vector3 side = aim.Cross(Mathf.Abs(aim.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
		Vector3 dir = aim;
		for (int attempt = 0; attempt < 40; attempt++)
		{
			float angle = attempt * 2.4f;
			float tilt = Mathf.Sqrt(attempt) * SiteSpacing / Mathf.Max(body.Radius, 5f) * 0.6f;
			dir = (aim + (side.Rotated(aim, angle) * Mathf.Tan(Mathf.Min(tilt, 1.3f)))).Normalized();
			Vector3 spot = body.SurfacePoint(dir);
			if (others.All(o => o.DistanceTo(spot) > SiteSpacing))
				break;
		}
		return PlaceOnSurface(body, dir, design);
	}

	/// <summary>Stands the design upright (+Y along <paramref name="up"/>) with its bottom blocks 1 m above the ground.</summary>
	public static Transform3D PlaceOnSurface(VoxelBody body, Vector3 up, Blueprint design, float clearance = 1f)
	{
		up = up.Normalized();
		Vector3 forward = up.Cross(Mathf.Abs(up.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
		var basis = Basis.LookingAt(forward, up);
		Vector3 center = body.GlobalPosition;
		float guess = body.SurfacePoint(up).DistanceTo(center);

		// Each block's footprint must clear the ground under it (the ground can bulge between cells).
		float height = guess;
		foreach (var block in design.Blocks)
		{
			Vector3 cell = BlockGrid.CellCenter(block.CellVector());
			float bottom = cell.Y - BlockGrid.CellSize * 0.5f;
			foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1), Vector2.Zero })
			{
				Vector3 lateral = basis * new Vector3(cell.X + corner.X * BlockGrid.CellSize * 0.5f, 0f, cell.Z + corner.Y * BlockGrid.CellSize * 0.5f);
				float ground = body.SurfacePoint(up * guess + lateral).DistanceTo(center);
				height = Mathf.Max(height, ground - bottom + clearance);
			}
		}
		return new Transform3D(basis, center + up * height);
	}

	// ------------------------------------------------------------ simulation

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		_scanTimer -= dt;
		if (_scanTimer <= 0f)
		{
			_scanTimer = 0.5f;
			AdoptBots();
		}
		Bots.RemoveAll(bot =>
		{
			if (GodotObject.IsInstanceValid(bot.Grid) && !bot.Grid.IsQueuedForDeletion() && bot.Grid.BlockCount > 0)
				return false;
			bot.Abort();
			return true;
		});
		foreach (var bot in Bots)
		{
			if (bot.Grid == PilotedGrid())
				continue;
			bot.Tick(this, dt);
		}
		foreach (var job in Jobs.ToArray())
			UpdateJob(job);
	}

	/// <summary>Any free-flying grid with a Bot Core joins the workforce (printed bots, loaded bots).</summary>
	private void AdoptBots()
	{
		foreach (var grid in Grids)
		{
			if (Bots.Any(b => b.Grid == grid) || grid == PilotedGrid() || !grid.HasBlock(BlockKind.BotCore))
				continue;
			if (!grid.IsBot && grid.IsStatic)
				continue;   // a station that happens to have a core stays a station
			grid.IsBot = true;
			if (grid.Label is null)
			{
				int n = 1;
				while (Grids.Any(g => g.Label == $"Bot {n}"))
					n++;
				grid.Label = $"Bot {n}";
				News?.Invoke($"{grid.Label} reports for work");
			}
			// Bots fly themselves along planned paths instead of through the physics engine.
			grid.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
			grid.Freeze = true;
			Bots.Add(new Bot(grid));
		}
	}

	private void UpdateJob(ConstructionJob job)
	{
		if (job.Grid is { } grid && (!GodotObject.IsInstanceValid(grid) || grid.IsQueuedForDeletion()))
			job.Grid = null;
		if (job.Built < job.Order.Count)
			return;
		job.Hologram?.QueueFree();
		Jobs.Remove(job);
		if (job.Grid is { } done)
		{
			done.Label = job.Name;
			if (done.Inventory.Capacity > 0f && Routes.All(r => r.From != job.Name))
				Routes.Add(new HaulRoute { From = job.Name, To = HomeLabel });
		}
		// Leftover stock (rounding, cancelled claims) goes back home with the next hauler.
		if (Home is { } home)
			foreach (var (item, amount) in job.Stock.Items.ToArray())
				home.Inventory.Add(item, amount);
		News?.Invoke($"{job.Name} is built and running");
	}

	/// <summary>The next thing an idle bot should do, or null to go and park at home.</summary>
	internal BotOrder? NextOrder(Bot bot)
	{
		var home = Home;
		if (home is null)
			return null;

		// Anything still aboard (after a load or a cancelled job) goes home first.
		if (bot.Grid.Inventory.Total > 0.5f)
			return new BotOrder.Unload(home);

		// 1. Construction: bring what the next blocks need, as far as home has it.
		foreach (var job in Jobs)
		{
			var load = job.NextLoad(home.Inventory, bot.Grid.Inventory.Capacity);
			if (load.Count > 0)
				return new BotOrder.Supply(job, load);
			job.Status = job.Built >= job.Order.Count ? "Finishing"
				: job.Needed().Count == 0 ? "Materials on the way"
				: "Waiting for " + string.Join(", ", job.Needed().Where(kv => home.Inventory.Get(kv.Key) < 1f).Select(kv => ItemCatalog.DisplayName(kv.Key)));
			// Stock already at the site but nobody assembling: go and build.
			if (job.Assembler is null && job.CanAffordNext())
				return new BotOrder.Assemble(job);
		}

		// 2. Hauling: the fullest enabled route with enough waiting.
		var route = Routes
			.Where(r => r.Enabled && r.Busy == 0)
			.Select(r => (Route: r, Source: Find(r.From)))
			.Where(x => x.Source is not null && Haulable(x.Source!.Inventory) >= HaulMinimum)
			.OrderByDescending(x => Haulable(x.Source!.Inventory))
			.FirstOrDefault();
		if (route.Source is not null)
			return new BotOrder.Haul(route.Route, route.Source!);
		return null;
	}

	/// <summary>Ingots are always worth fetching; ore only when a site stores lots of it.</summary>
	internal static float Haulable(Inventory source) =>
		source.Items.Where(kv => ItemCatalog.Get(kv.Key).Category == ItemCategory.Ingot).Sum(kv => kv.Value);

	// ------------------------------------------------------------ flight

	/// <summary>
	/// Waypoints from <paramref name="from"/> to <paramref name="to"/>: lift off the body we start on,
	/// fly around anything in the way, and come straight down onto the target.
	/// </summary>
	internal List<Vector3> PlanPath(Vector3 from, Vector3 to)
	{
		var points = new List<Vector3> { from };
		var start = NearestBody(from);
		var end = NearestBody(to);
		if (start is not null)
			points.Add(from + (from - start.GlobalPosition).Normalized() * 10f);
		Vector3 arrival = end is not null ? to + (to - end.GlobalPosition).Normalized() * 10f : to;

		// Detour around bodies crossing the straight line, a few passes deep.
		var middle = new List<Vector3> { points[^1], arrival };
		for (int pass = 0; pass < 3; pass++)
		{
			bool changed = false;
			for (int i = 0; i < middle.Count - 1; i++)
			{
				foreach (var body in Bodies)
				{
					float keep = body.OuterRadius + 8f;
					Vector3 a = middle[i], b = middle[i + 1], c = body.GlobalPosition;
					Vector3 ab = b - a;
					float t = Mathf.Clamp((c - a).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-3f), 0f, 1f);
					Vector3 closest = a + ab * t;
					if (closest.DistanceTo(c) >= keep || t <= 0.02f || t >= 0.98f)
						continue;
					Vector3 away = closest - c;
					if (away.LengthSquared() < 1e-3f)
						away = ab.Cross(Vector3.Up).LengthSquared() > 1e-3f ? ab.Cross(Vector3.Up) : ab.Cross(Vector3.Right);
					middle.Insert(i + 1, c + away.Normalized() * (keep + 4f));
					changed = true;
					break;
				}
			}
			if (!changed)
				break;
		}
		points.AddRange(middle.Skip(1));
		points.Add(to);
		return points;
	}

	private VoxelBody? NearestBody(Vector3 point) =>
		Bodies.Where(b => b.GlobalPosition.DistanceTo(point) < b.OuterRadius + 20f)
			.OrderBy(b => b.GlobalPosition.DistanceTo(point) - b.OuterRadius)
			.FirstOrDefault();

	/// <summary>Up for a bot at <paramref name="point"/>: away from the nearest body, else world up.</summary>
	internal Vector3 UpAt(Vector3 point)
	{
		var body = Bodies.OrderBy(b => b.GlobalPosition.DistanceTo(point) - b.OuterRadius).FirstOrDefault();
		return body is not null && body.GlobalPosition.DistanceTo(point) < body.OuterRadius * 3f
			? (point - body.GlobalPosition).Normalized()
			: Vector3.Up;
	}

	/// <summary>Where a bot waits at home: a ring above the base, one slot each.</summary>
	internal Vector3 ParkingSpot(Bot bot)
	{
		var home = Home!;
		int index = Mathf.Max(0, Bots.IndexOf(bot));
		Vector3 up = home.GlobalBasis.Y;
		Vector3 side = home.GlobalBasis.X;
		float angle = index * Mathf.Tau / 8f + index / 8 * 0.4f;
		return home.GlobalPosition + up * (9f + index / 8 * 3f) + side.Rotated(up, angle) * 9f;
	}

	/// <summary>Where bots hover to load or unload at a station: over its storage.</summary>
	internal static Vector3 DockSpot(BlockGrid station)
	{
		var cargo = station.Blocks.FirstOrDefault(b => b.Value.Definition.Kind == BlockKind.CargoContainer);
		Vector3 local = cargo.Value.Definition is null ? Vector3.Zero : BlockGrid.CellCenter(cargo.Key);
		return station.GlobalTransform * (local + Vector3.Up * (BlockGrid.CellSize * 2.2f));
	}

	// ------------------------------------------------------------ holograms

	internal void ShowHologram(ConstructionJob job)
	{
		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hologram.gdshader") };
		material.SetShaderParameter("tint", Palette.Lemon);
		material.SetShaderParameter("progress", -1f);   // faint outline only: the real blocks fill it in
		var model = BlueprintModel.Create(job.Design, targetHeight: null, material);
		model.Transform = new Transform3D(job.Site.Basis, job.Site * job.Design.Bounds().GetCenter());
		World.AddChild(model);
		job.Hologram = model;
	}

	// ------------------------------------------------------------ saving

	public ColonySave ToSave() => new()
	{
		Jobs = Jobs.Select(j => new JobSave
		{
			Name = j.Name,
			Body = j.BodyName,
			Design = j.Design,
			Position = SaveMath.ToArray(j.Site.Origin),
			Rotation = SaveMath.ToArray(j.Site.Basis.GetRotationQuaternion()),
			Built = j.Built,
			Stock = j.Stock.Items.ToDictionary(kv => kv.Key, kv => kv.Value),
		}).ToList(),
		Routes = Routes.Select(r => new HaulRoute { From = r.From, To = r.To, Enabled = r.Enabled }).ToList(),
	};

	/// <summary>Restores jobs and routes after the grids are back (partly built sites carry the job's name).</summary>
	public void Restore(ColonySave save)
	{
		foreach (var saved in save.Jobs)
		{
			var job = new ConstructionJob(saved.Design, saved.Name, saved.Body, SaveMath.Transform(saved.Position, saved.Rotation))
			{
				Built = saved.Built,
				Grid = Find(saved.Name),
			};
			foreach (var (item, amount) in saved.Stock)
				job.Stock.Add(item, amount);
			Jobs.Add(job);
			ShowHologram(job);
		}
		Routes.AddRange(save.Routes);
		AdoptBots();
	}
}

/// <summary>A route bots serve on their own: ingots from a site's storage to another's.</summary>
public sealed class HaulRoute
{
	public string From { get; set; } = "";
	public string To { get; set; } = Colony.HomeLabel;
	public bool Enabled { get; set; } = true;
	/// <summary>Bots currently on this route.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public int Busy { get; set; }
}

/// <summary>A station the bots are building, block by block, from materials they bring to the site.</summary>
public sealed class ConstructionJob
{
	public ConstructionJob(Blueprint design, string name, string bodyName, Transform3D site)
	{
		Design = design;
		Name = name;
		BodyName = bodyName;
		Site = site;
		Order = BuildOrder(design);
	}

	public Blueprint Design { get; }
	public string Name { get; }
	public string BodyName { get; }
	public Transform3D Site { get; }
	/// <summary>Blocks in building order: bottom first, each touching one already placed.</summary>
	public IReadOnlyList<BlueprintBlock> Order { get; }
	public int Built { get; set; }
	/// <summary>Materials delivered to the site and not used yet (already multiplied by the auto-build factor).</summary>
	public Inventory Stock { get; } = new();
	/// <summary>Materials on their way in bots' holds.</summary>
	public Dictionary<string, float> InTransit { get; } = new();
	public BlockGrid? Grid { get; set; }
	public Bot? Assembler { get; set; }
	public string Status { get; set; } = "Waiting for bots";
	public Node3D? Hologram { get; set; }

	public float Progress => Order.Count == 0 ? 1f : (float)Built / Order.Count;

	public static Dictionary<string, float> CostOf(BlueprintBlock block) =>
		BlockCatalog.Get(block.Id).Cost.ToDictionary(kv => kv.Key, kv => kv.Value * Colony.AutoBuildCostFactor);

	/// <summary>Materials still to bring for the blocks not yet built, after stock and deliveries under way.</summary>
	public Dictionary<string, float> Needed()
	{
		var need = new Dictionary<string, float>();
		for (int i = Built; i < Order.Count; i++)
			foreach (var (item, amount) in CostOf(Order[i]))
				need[item] = need.GetValueOrDefault(item) + amount;
		foreach (var item in need.Keys.ToArray())
		{
			need[item] -= Stock.Get(item) + InTransit.GetValueOrDefault(item);
			if (need[item] < 0.5f)
				need.Remove(item);
		}
		return need;
	}

	/// <summary>
	/// What one bot should carry next: the needs of the next blocks in order, as far as home has them
	/// and the hold allows.
	/// </summary>
	public Dictionary<string, float> NextLoad(Inventory home, float capacity)
	{
		var load = new Dictionary<string, float>();
		var need = Needed();
		float room = capacity;
		foreach (var (item, amount) in need)
		{
			float take = Mathf.Min(Mathf.Min(amount, home.Get(item)), room);
			if (take < 1f)
				continue;
			load[item] = take;
			room -= take;
			if (room < 1f)
				break;
		}
		return load;
	}

	public bool CanAffordNext() => Built < Order.Count && Stock.Has(CostOf(Order[Built]));

	private static List<BlueprintBlock> BuildOrder(Blueprint design)
	{
		var byCell = design.Blocks.ToDictionary(b => b.CellVector());
		var order = new List<BlueprintBlock>();
		var seen = new HashSet<Vector3I>();
		// Breadth-first from the lowest block, preferring lower cells, so the site grows from the ground up.
		var frontier = new List<Vector3I>();
		foreach (var start in byCell.Keys.OrderBy(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Z))
		{
			if (!seen.Add(start))
				continue;
			frontier.Add(start);
			while (frontier.Count > 0)
			{
				var cell = frontier.OrderBy(c => c.Y).First();
				frontier.Remove(cell);
				order.Add(byCell[cell]);
				foreach (var dir in new[] { Vector3I.Right, Vector3I.Left, Vector3I.Up, Vector3I.Down, Vector3I.Back, Vector3I.Forward })
					if (byCell.ContainsKey(cell + dir) && seen.Add(cell + dir))
						frontier.Add(cell + dir);
			}
		}
		return order;
	}
}

/// <summary>Colony state in a save.</summary>
public sealed class ColonySave
{
	public List<JobSave> Jobs { get; set; } = new();
	public List<HaulRoute> Routes { get; set; } = new();
}

public sealed class JobSave
{
	public string Name { get; set; } = "";
	public string Body { get; set; } = "";
	public Blueprint Design { get; set; } = new();
	public float[] Position { get; set; } = [0, 0, 0];
	public float[] Rotation { get; set; } = [0, 0, 0, 1];
	public int Built { get; set; }
	public Dictionary<string, float> Stock { get; set; } = new();
}
