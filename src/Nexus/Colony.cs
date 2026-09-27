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
	/// <summary>How much more building by bot costs than by hand (2.5×, less with Starlight upgrades).</summary>
	public static float AutoBuildCostFactor => Core.Campaign.Active.AutoBuildFactor;
	public const float BuildSecondsPerBlock = 1.2f;
	public const float LoadSeconds = 1.5f;
	/// <summary>m/s with no thrusters of its own, including Starlight upgrades.</summary>
	public static float BaseBotSpeed => 16f * Core.Campaign.Active.BotSpeedFactor;
	public static float MaxBotSpeed => 40f * Core.Campaign.Active.BotSpeedFactor;
	private const float HaulMinimum = 50f;       // kg worth a trip
	private const float SiteSpacing = 22f;       // m between sites on the same body
	/// <summary>How far an uplink (or Home) reaches: bodies whose surface is this close are linked.</summary>
	public const float UplinkRange = 170f;
	private const float RateWindow = 60f;        // seconds of history behind production rates
	private const float SampleInterval = 5f;
	private const int NewsKept = 8;

	/// <summary>World parent of grids and terrain.</summary>
	public Node3D World { get; set; } = null!;

	public List<Bot> Bots { get; } = new();
	public List<ConstructionJob> Jobs { get; } = new();
	public List<HaulRoute> Routes { get; } = new();

	/// <summary>A Curator swarm watches this spot: bots keep away (set by the threats manager).</summary>
	public System.Func<Vector3, bool> Blocked { get; set; } = _ => false;

	/// <summary>Threat state to save (set by the threats manager).</summary>
	public System.Func<Threats.ThreatsSave>? ThreatsToSave { get; set; }

	/// <summary>Grid the player is piloting, which bots leave alone.</summary>
	public System.Func<BlockGrid?> PilotedGrid { get; set; } = () => null;

	/// <summary>Short news for the HUD ("Drill Site on Dune is running").</summary>
	public event System.Action<string>? News;

	/// <summary>
	/// Resonance gathered from living planets: what will one day charge the Breach Lance.
	/// </summary>
	public float Resonance { get; private set; }

	public float ResonancePerMinute => Bodies.OfType<MiniPlanet>().Sum(p => p.ResonancePerMinute) + (PeopleResonance?.Invoke() ?? 0f);

	/// <summary>Villages to save (set by the people manager).</summary>
	public System.Func<List<Life.Settlement>>? SettlementsToSave { get; set; }

	/// <summary>Resonance per minute from villages (set by the people manager).</summary>
	public System.Func<float>? PeopleResonance { get; set; }

	public void AddResonance(float amount) => Resonance += amount;

	/// <summary>Resonance a Breach Lance needs before it can fire.</summary>
	public const float LanceRequired = 1500f;
	/// <summary>How fast a Lance draws stored Resonance into its ball (per second).</summary>
	private const float LanceDrawPerSecond = 10f;

	/// <summary>Resonance gathered in the Lance's ball of light.</summary>
	public float LanceCharge { get; private set; }
	public float LanceFraction => LanceCharge / LanceRequired;
	public bool HasLance => Grids.Any(g => g.HasBlock(BlockKind.BreachLance));

	/// <summary>Seeds take once a planet's air and seas are this far back.</summary>
	public const float GardenThreshold = 0.3f;

	/// <summary>Latest news, newest first, with the time it happened.</summary>
	public List<(double Time, string Text)> RecentNews { get; } = new();

	private float _scanTimer;
	private float _sampleTimer;
	private double _clock;
	// Per grid: timestamped copies of its Produced + Received counters, oldest first.
	private readonly Dictionary<BlockGrid, Queue<(double Time, Dictionary<string, float> Made, Dictionary<string, float> Got)>> _samples = new();

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

	/// <summary>Where the Nexus can reach from: Home and every station with an Uplink.</summary>
	public IEnumerable<BlockGrid> Uplinks => Grids.Where(g => g.IsStatic && !g.IsBot && (g.Label == HomeLabel || g.HasBlock(BlockKind.Uplink)));

	/// <summary>A linked body shows its details in the Nexus and bots may build on it.</summary>
	public bool IsLinked(VoxelBody body) =>
		Uplinks.Any(u => u.GlobalPosition.DistanceTo(body.GlobalPosition) - body.OuterRadius <= UplinkRange);

	/// <summary>The body a station stands on (or floats nearest to).</summary>
	public VoxelBody? BodyOf(Node3D node) =>
		Bodies.OrderBy(b => b.GlobalPosition.DistanceTo(node.GlobalPosition) - b.OuterRadius).FirstOrDefault();

	public void Announce(string text)
	{
		RecentNews.Insert(0, (_clock, text));
		if (RecentNews.Count > NewsKept)
			RecentNews.RemoveAt(RecentNews.Count - 1);
		News?.Invoke(text);
	}

	public BlockGrid? Find(string label) => Grids.FirstOrDefault(g => g.Label == label);

	/// <summary>Ingots needed to have bots build <paramref name="design"/>.</summary>
	public static Dictionary<string, float> AutoBuildCost(Blueprint design) =>
		design.TotalCost().ToDictionary(kv => kv.Key, kv => kv.Value * AutoBuildCostFactor);

	// ------------------------------------------------------------ orders

	/// <summary>Queues a build of <paramref name="design"/> on <paramref name="body"/> at <paramref name="site"/>.</summary>
	public ConstructionJob OrderBuild(Blueprint design, VoxelBody body, Transform3D site)
	{
		// Dark bodies only take an Uplink: that is how the Nexus reaches them.
		if (!IsLinked(body) && !design.Contains(BlockKind.Uplink))
			throw new System.InvalidOperationException($"{body.Name} is not linked to the Nexus");
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

	// ------------------------------------------------------------ moving stations

	/// <summary>How long a station takes to lift off, fly and land somewhere new.</summary>
	public const float MoveSeconds = 8f;

	private sealed class StationMove(BlockGrid grid, Transform3D from, Transform3D to)
	{
		public BlockGrid Grid { get; } = grid;
		public Transform3D From { get; } = from;
		public Transform3D To { get; } = to;
		public float Progress { get; set; }
	}

	private readonly List<StationMove> _moves = new();

	/// <summary>Home and finished sites can move; ships, bots and half-built sites can't.</summary>
	public bool CanMove(BlockGrid grid) =>
		grid.IsStatic && !grid.IsBot && grid.Label is not null && Jobs.All(j => j.Grid != grid) && !IsMoving(grid);

	public bool IsMoving(BlockGrid grid) => _moves.Any(m => m.Grid == grid);

	/// <summary>
	/// Lifts a station off and flies it to <paramref name="to"/> (from <see cref="SuggestSite"/>), e.g. when
	/// its drills have dug out the rock in reach. It keeps its name, storage, routes and people.
	/// </summary>
	public void MoveStation(BlockGrid grid, Transform3D to)
	{
		if (!CanMove(grid))
			return;
		_moves.Add(new StationMove(grid, grid.GlobalTransform, to));
		Announce($"{grid.Label} lifted off for a new spot.");
	}

	/// <summary>Lands every station in flight at once (before saving, so a save never holds one mid-air).</summary>
	public void FinishMoves()
	{
		foreach (var move in _moves.Where(m => GodotObject.IsInstanceValid(m.Grid)))
			move.Grid.GlobalTransform = move.To;
		_moves.Clear();
	}

	private void TickMoves(float dt)
	{
		for (int i = _moves.Count - 1; i >= 0; i--)
		{
			var move = _moves[i];
			if (!GodotObject.IsInstanceValid(move.Grid))
			{
				_moves.RemoveAt(i);
				continue;
			}
			move.Progress = Mathf.Min(move.Progress + dt / MoveSeconds, 1f);
			float s = Mathf.SmoothStep(0f, 1f, move.Progress);
			// A gentle hop: straight up off the ground, over, and down, never through the rock between.
			var up = (move.From.Basis.Y + move.To.Basis.Y).Normalized();
			float hop = 12f + move.From.Origin.DistanceTo(move.To.Origin) * 0.4f;
			var origin = move.From.Origin.Lerp(move.To.Origin, s) + up * hop * Mathf.Sin(Mathf.Pi * s);
			var basis = new Basis(move.From.Basis.GetRotationQuaternion().Slerp(move.To.Basis.GetRotationQuaternion(), s));
			move.Grid.GlobalTransform = new Transform3D(basis, origin);
			if (move.Progress >= 1f)
			{
				move.Grid.GlobalTransform = move.To;
				_moves.RemoveAt(i);
				Announce($"{move.Grid.Label} landed in its new spot.");
			}
		}
	}

	// ------------------------------------------------------------ simulation

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		ProductionStats.Tick(dt);
		TickMoves(dt);
		_scanTimer -= dt;
		_clock += dt;
		if (_scanTimer <= 0f)
		{
			_scanTimer = 0.5f;
			AdoptBots();
			NameUplinks();
		}
		_sampleTimer -= dt;
		if (_sampleTimer <= 0f)
		{
			_sampleTimer = SampleInterval;
			SampleRates();
			RouteIce();
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
		Heal(dt);
	}

	/// <summary>Hands what terraformers made to the planet they stand on, and gathers Resonance.</summary>
	private void Heal(float dt)
	{
		foreach (var grid in Grids)
		{
			if (!grid.IsStatic || grid.IsBot || !grid.Blocks.Any(b => b.Value.Definition.Terraformer))
				continue;
			var planet = BodyOf(grid) is MiniPlanet p && p.GlobalPosition.DistanceTo(grid.GlobalPosition) < p.OuterRadius + 25f ? p : null;
			grid.OnPlanet = planet is not null;
			grid.GardensCanGrow = planet is not null && planet.Air >= GardenThreshold && planet.Water >= GardenThreshold;
			var made = grid.TakeTerraformed();
			if (planet is null)
				continue;
			float before = planet.Vitality;
			foreach (var (vital, kilograms) in made)
				planet.Nourish(vital, kilograms);
			// Milestones every quarter of the way back to life.
			if (Mathf.FloorToInt(planet.Vitality * 4f) > Mathf.FloorToInt(before * 4f))
				Announce(planet.Vitality >= 0.999f ? $"{planet.Name} is fully alive!" : $"{planet.Name} is {Mathf.FloorToInt(planet.Vitality * 4f) * 25}% back to life");
		}
		Resonance += ResonancePerMinute * dt / 60f;
		// A Lance drinks the stored Resonance until its ball is full.
		if (LanceCharge < LanceRequired && Resonance > 0f && HasLance)
		{
			float take = Mathf.Min(Mathf.Min(Resonance, LanceDrawPerSecond * dt), LanceRequired - LanceCharge);
			Resonance -= take;
			LanceCharge += take;
			if (LanceCharge >= LanceRequired)
				Announce("The Breach Lance is full. Fire it from the Nexus!");
		}
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
				Announce($"{grid.Label} reports for work");
			}
			// Bots fly themselves along planned paths instead of through the physics engine.
			grid.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
			grid.Freeze = true;
			Bots.Add(new Bot(grid));
		}
	}

	/// <summary>
	/// Hydrators need ice from elsewhere: every station with one gets an ice route from a site that has
	/// ice in storage, so seas fill without the player wiring it up.
	/// </summary>
	private void RouteIce()
	{
		foreach (var station in Grids.Where(g => g.IsStatic && !g.IsBot && g.Label is not null && g.HasBlock(BlockKind.Hydrator) && g.Inventory.Capacity > 0f).ToList())
		{
			if (Routes.Any(r => r.To == station.Label && r.Cargo == HaulCargo.Ice && r.Enabled))
				continue;
			var source = Grids.Where(g => g != station && g.IsStatic && !g.IsBot && g.Label is not null && g.Inventory.Get("ice") >= 50f)
				.OrderBy(g => g.GlobalPosition.DistanceTo(station.GlobalPosition)).FirstOrDefault();
			if (source is null)
				continue;
			AddRoute(source.Label!, station.Label!, HaulCargo.Ice);
			Announce($"Bots will haul ice from {source.Label} to {station.Label}");
		}
	}

	/// <summary>A station you set an Uplink on becomes a named site; its body lights up in the Nexus.</summary>
	private void NameUplinks()
	{
		_ = Home;   // an older world's unnamed base must become Home, not an uplink site
		foreach (var grid in Grids.Where(g => g.IsStatic && !g.IsBot && g.Label is null && g.HasBlock(BlockKind.Uplink)).ToList())
		{
			var body = BodyOf(grid);
			bool wasDark = body is not null && !Uplinks.Any(u => u != grid && u.GlobalPosition.DistanceTo(body.GlobalPosition) - body.OuterRadius <= UplinkRange);
			string name = $"{body?.Name ?? "Deep space"} Uplink";
			int n = 1;
			while (Find(n == 1 ? name : $"{name} {n}") is not null)
				n++;
			grid.Label = n == 1 ? name : $"{name} {n}";
			Announce(wasDark ? $"{body!.Name} is linked to the Nexus - bots can build there now" : $"{grid.Label} is online");
		}
	}

	private void SampleRates()
	{
		foreach (var grid in _samples.Keys.Where(g => !GodotObject.IsInstanceValid(g) || g.IsQueuedForDeletion()).ToList())
			_samples.Remove(grid);
		foreach (var grid in Grids.Where(g => g.IsStatic && !g.IsBot))
		{
			if (!_samples.TryGetValue(grid, out var queue))
				_samples[grid] = queue = new();
			queue.Enqueue((_clock, new Dictionary<string, float>(grid.Produced), new Dictionary<string, float>(grid.Received)));
			while (queue.Count > 2 && _clock - queue.Peek().Time > RateWindow)
				queue.Dequeue();
		}
	}

	/// <summary>Kilograms per minute a station makes (refining) and receives (bot deliveries), over the last minute.</summary>
	public (Dictionary<string, float> Made, Dictionary<string, float> Received) RatesPerMinute(BlockGrid grid)
	{
		var made = new Dictionary<string, float>();
		var got = new Dictionary<string, float>();
		if (!_samples.TryGetValue(grid, out var queue) || queue.Count < 2)
			return (made, got);
		var first = queue.Peek();
		var last = queue.Last();
		float minutes = (float)(last.Time - first.Time) / 60f;
		if (minutes <= 0f)
			return (made, got);
		foreach (var (item, amount) in last.Made)
			if (amount - first.Made.GetValueOrDefault(item) > 0.01f)
				made[item] = (amount - first.Made.GetValueOrDefault(item)) / minutes;
		foreach (var (item, amount) in last.Got)
			if (amount - first.Got.GetValueOrDefault(item) > 0.01f)
				got[item] = (amount - first.Got.GetValueOrDefault(item)) / minutes;
		return (made, got);
	}

	/// <summary>What holds a station back, in plain words; empty when all is well.</summary>
	public List<string> Problems(BlockGrid grid)
	{
		var problems = new List<string>();
		foreach (var cell in grid.Quarantined)
			problems.Add($"A virus naps in the {grid.Blocks.First(b => b.Key == cell).Value.Definition.DisplayName}: purge it");
		if (Blocked(grid.GlobalPosition))
			problems.Add("A Curator swarm is watching: bots stay away");
		if (grid.PowerDemand > 0.01f && grid.PowerSatisfaction < 0.9f)
			problems.Add($"Low power ({grid.PowerSatisfaction:P0}): add solar panels or a battery");
		if (grid.Inventory.Capacity > 0f && grid.Inventory.FreeSpace < grid.Inventory.Capacity * 0.05f)
			problems.Add("Storage is full");
		foreach (var (cell, block) in grid.Blocks)
			if (block.Definition.Kind == BlockKind.AutoDrill && grid.MachineStatus(cell) == "No rock within reach")
				problems.Add("A drill can't reach rock: move the station to fresh rock");
		float ingots = Haulable(grid.Inventory, HaulCargo.Ingots);
		if (grid.Label != HomeLabel && ingots > 1000f && !Routes.Any(r => r.Enabled && r.From == grid.Label))
			problems.Add($"{ingots:0} kg of ingots piling up: no route hauls them");
		if (grid.Label != HomeLabel && Routes.Any(r => r.Enabled && r.From == grid.Label) && Bots.Count == 0)
			problems.Add("No bots to run its routes");
		return problems;
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
		Announce($"{job.Name} is built and running");
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
			if (Blocked(job.Site.Origin))
			{
				job.Status = "A Curator swarm is watching the site";
				continue;
			}
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
			.Select(r => (Route: r, Source: Find(r.From), Target: Find(r.To)))
			.Where(x => x.Source is not null && x.Target is not null && x.Target.Inventory.FreeSpace >= HaulMinimum
				&& !Blocked(x.Source.GlobalPosition) && !Blocked(x.Target.GlobalPosition)
				&& Haulable(x.Source!.Inventory, x.Route.Cargo) >= HaulMinimum)
			.OrderByDescending(x => Haulable(x.Source!.Inventory, x.Route.Cargo))
			.FirstOrDefault();
		if (route.Source is not null)
			return new BotOrder.Haul(route.Route, route.Source!);
		return null;
	}

	/// <summary>What is still wanted: materials building sites haven't received and fabricators are waiting for.</summary>
	public Dictionary<string, float> Demand()
	{
		var demand = new Dictionary<string, float>();
		void Add(string item, float kg)
		{
			if (kg > 0.5f)
				demand[item] = demand.GetValueOrDefault(item) + kg;
		}
		foreach (var job in Jobs)
			foreach (var (item, kg) in job.Needed())
				Add(item, kg);
		foreach (var grid in Grids)
			foreach (var (cell, queue) in grid.FabricatorQueues)
				if (!queue[0].Paid)
					foreach (var (item, kg) in queue[0].Design.TotalCost())
						Add(item, kg - (grid.StateOf(cell).Input?.Get(item) ?? 0f));
		return demand;
	}

	/// <summary>Everything in storage across all stations.</summary>
	public Dictionary<string, float> Stock()
	{
		var stock = new Dictionary<string, float>();
		foreach (var grid in Grids.Where(g => g.IsStatic && !g.IsBot))
			foreach (var (item, kg) in grid.Inventory.Items)
				stock[item] = stock.GetValueOrDefault(item) + kg;
		return stock;
	}

	/// <summary>Kilograms of <paramref name="cargo"/> a route could pick up from <paramref name="source"/>.</summary>
	internal static float Haulable(Inventory source, HaulCargo cargo) =>
		source.Items.Where(kv => Carries(cargo, kv.Key)).Sum(kv => kv.Value);

	internal static bool Carries(HaulCargo cargo, string item) => cargo switch
	{
		HaulCargo.Ingots => ItemCatalog.Get(item).Category == ItemCategory.Ingot,
		HaulCargo.Ore => ItemCatalog.Get(item).Category == ItemCategory.Ore && item != "ice",
		HaulCargo.Ice => item == "ice",
		_ => true,
	};

	/// <summary>Adds (or re-enables) a route; returns false when it already runs.</summary>
	public bool AddRoute(string from, string to, HaulCargo cargo)
	{
		if (from == to)
			return false;
		var existing = Routes.FirstOrDefault(r => r.From == from && r.To == to && r.Cargo == cargo);
		if (existing is not null)
		{
			bool was = existing.Enabled;
			existing.Enabled = true;
			return !was;
		}
		Routes.Add(new HaulRoute { From = from, To = to, Cargo = cargo });
		return true;
	}

	/// <summary>Bots record what they drop off so the Nexus can show flows.</summary>
	internal static void RecordDelivery(BlockGrid target, string item, float amount) =>
		target.Received[item] = target.Received.GetValueOrDefault(item) + amount;

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
		Settlements = SettlementsToSave?.Invoke() ?? new(),
		Threats = ThreatsToSave?.Invoke() ?? new(),
		Resonance = Resonance,
		LanceCharge = LanceCharge,
		Planets = Bodies.OfType<MiniPlanet>().Select(p => new PlanetSave { Name = p.Name, Air = p.Air, Water = p.Water, Soil = p.Soil }).ToList(),
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
		Routes = Routes.Select(r => new HaulRoute { From = r.From, To = r.To, Cargo = r.Cargo, Enabled = r.Enabled }).ToList(),
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
		Resonance = save.Resonance;
		LanceCharge = save.LanceCharge;
		foreach (var saved in save.Planets)
		{
			if (Bodies.OfType<MiniPlanet>().FirstOrDefault(p => p.Name == saved.Name) is not { } planet)
				continue;
			planet.Air = saved.Air;
			planet.Water = saved.Water;
			planet.Soil = saved.Soil;
		}
		AdoptBots();
	}
}

/// <summary>What a haul route carries.</summary>
public enum HaulCargo { Ingots, Ore, Ice, Everything }

/// <summary>A route bots serve on their own: goods from one station's storage to another's.</summary>
public sealed class HaulRoute
{
	public string From { get; set; } = "";
	public string To { get; set; } = Colony.HomeLabel;
	public HaulCargo Cargo { get; set; } = HaulCargo.Ingots;
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
	public List<Life.Settlement> Settlements { get; set; } = new();
	public Threats.ThreatsSave Threats { get; set; } = new();
	public float Resonance { get; set; }
	public float LanceCharge { get; set; }
	public List<PlanetSave> Planets { get; set; } = new();
	public List<JobSave> Jobs { get; set; } = new();
	public List<HaulRoute> Routes { get; set; } = new();
}

public sealed class PlanetSave
{
	public string Name { get; set; } = "";
	public float Air { get; set; }
	public float Water { get; set; }
	public float Soil { get; set; }
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
