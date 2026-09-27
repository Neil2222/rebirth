using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Items;

namespace Rebirth.Nexus;

/// <summary>What the colony asks an idle bot to do next.</summary>
internal abstract record BotOrder
{
	/// <summary>Drop off whatever is aboard at home.</summary>
	public sealed record Unload(BlockGrid Home) : BotOrder;
	/// <summary>Carry <paramref name="Load"/> from home to a building site (and help build if nobody is).</summary>
	public sealed record Supply(ConstructionJob Job, Dictionary<string, float> Load) : BotOrder;
	/// <summary>Build from stock already waiting at the site.</summary>
	public sealed record Assemble(ConstructionJob Job) : BotOrder;
	/// <summary>Fetch ingots from a site's storage and bring them home.</summary>
	public sealed record Haul(HaulRoute Route, BlockGrid Source) : BotOrder;
}

/// <summary>
/// A worker bot: a grid with a Bot Core, flown along planned paths (not physics) through a list of
/// small steps: fly somewhere, wait, move items, place a block.
/// </summary>
public sealed class Bot
{
	private delegate bool Step(Colony colony, float dt);

	private readonly List<Step> _steps = new();
	private List<Vector3>? _path;
	private int _waypoint;
	private float _wait = -1f;
	private readonly Dictionary<string, float> _transit = new();   // supply load counted in the job's InTransit
	private HaulRoute? _route;

	public Bot(BlockGrid grid)
	{
		Grid = grid;
		float thrust = grid.Blocks.Sum(b => b.Value.Definition.Thrust);
		Speed = Mathf.Clamp(Colony.BaseBotSpeed + thrust / Mathf.Max(grid.Mass, 1f) * 0.1f, Colony.BaseBotSpeed, Colony.MaxBotSpeed);
	}

	public BlockGrid Grid { get; }
	public float Speed { get; }
	public float Capacity => Grid.Inventory.Capacity;
	public string Status { get; private set; } = "Reporting for work";
	public ConstructionJob? Job { get; private set; }
	public bool Busy => _steps.Count > 0;

	internal void Tick(Colony colony, float dt)
	{
		if (_steps.Count == 0)
			Plan(colony);
		while (_steps.Count > 0 && _steps[0](colony, dt))
		{
			_steps.RemoveAt(0);
			dt = 0f;   // later steps start next tick, except instant ones which finish at once
		}
	}

	/// <summary>Drops the current work; the colony hands out something new next tick.</summary>
	public void Abort()
	{
		_steps.Clear();
		_path = null;
		_wait = -1f;
		if (Job is { } job)
		{
			foreach (var (item, amount) in _transit)
				job.InTransit[item] = Mathf.Max(0f, job.InTransit.GetValueOrDefault(item) - amount);
			if (job.Assembler == this)
				job.Assembler = null;
		}
		_transit.Clear();
		Job = null;
		if (_route is not null)
			_route.Busy--;
		_route = null;
	}

	private void Plan(Colony colony)
	{
		switch (colony.NextOrder(this))
		{
			case BotOrder.Unload unload:
				Status = $"Bringing cargo to {Colony.HomeLabel}";
				FlyTo(Colony.DockSpot(unload.Home));
				WaitFor(Colony.LoadSeconds);
				Do(_ => UnloadInto(unload.Home));
				break;

			case BotOrder.Supply supply:
				Job = supply.Job;
				var home = colony.Home!;
				Status = $"Loading materials for {supply.Job.Name}";
				foreach (var (item, amount) in supply.Load)
				{
					supply.Job.InTransit[item] = supply.Job.InTransit.GetValueOrDefault(item) + amount;
					_transit[item] = amount;
				}
				FlyTo(Colony.DockSpot(home));
				WaitFor(Colony.LoadSeconds);
				Do(_ => LoadFrom(home.Inventory, supply.Load));
				Do(_ => Status = $"Flying to {supply.Job.Name}");
				FlyTo(SiteHover(supply.Job));
				WaitFor(Colony.LoadSeconds);
				Do(_ => DeliverTo(supply.Job));
				Do(c => StartAssembling(c, supply.Job));
				break;

			case BotOrder.Assemble assemble:
				Job = assemble.Job;
				Do(c => StartAssembling(c, assemble.Job));
				break;

			case BotOrder.Haul haul:
				_route = haul.Route;
				_route.Busy++;
				Status = $"Hauling from {haul.Source.Label}";
				FlyTo(Colony.DockSpot(haul.Source));
				WaitFor(Colony.LoadSeconds);
				Do(_ => TakeHaul(haul.Source.Inventory, haul.Route.Cargo));
				Do(c =>
				{
					if (c.Find(haul.Route.To) is { } target)
					{
						Status = $"Bringing {haul.Route.Cargo.ToString().ToLowerInvariant()} to {target.Label}";
						Insert(Fly(Colony.DockSpot(target)), Wait(Colony.LoadSeconds), Instant(_ => UnloadInto(target)));
					}
				});
				Do(_ =>
				{
					_route!.Busy--;
					_route = null;
				});
				break;

			default:
				if (colony.Home is null)
				{
					Status = "No home base";
					return;
				}
				Vector3 park = colony.ParkingSpot(this);
				if (Grid.GlobalPosition.DistanceTo(park) > 1f)
				{
					Status = "Returning to park";
					FlyTo(park);
				}
				else
				{
					Status = "Idle";
					WaitFor(1f);   // look for work again in a moment
				}
				break;
		}
	}

	// ------------------------------------------------------------ actions

	private void UnloadInto(BlockGrid target)
	{
		foreach (var (item, amount) in Grid.Inventory.Items.ToArray())
			Colony.RecordDelivery(target, item, Grid.Inventory.TransferTo(target.Inventory, item, amount));
	}

	private void LoadFrom(Inventory home, Dictionary<string, float> load)
	{
		foreach (var (item, amount) in load)
		{
			float moved = home.TransferTo(Grid.Inventory, item, amount);
			if (moved < amount && Job is { } job)
			{
				// Home had less than planned by now: the rest is no longer on its way.
				job.InTransit[item] = Mathf.Max(0f, job.InTransit.GetValueOrDefault(item) - (amount - moved));
				_transit[item] = moved;
			}
		}
	}

	private void DeliverTo(ConstructionJob job)
	{
		foreach (var (item, amount) in _transit)
			job.InTransit[item] = Mathf.Max(0f, job.InTransit.GetValueOrDefault(item) - amount);
		_transit.Clear();
		foreach (var (item, amount) in Grid.Inventory.Items.ToArray())
			Grid.Inventory.TransferTo(job.Stock, item, amount);
	}

	private void TakeHaul(Inventory source, HaulCargo cargo)
	{
		foreach (var (item, amount) in source.Items.Where(kv => Colony.Carries(cargo, kv.Key)).ToArray())
			source.TransferTo(Grid.Inventory, item, amount);
	}

	/// <summary>Becomes the site's builder if it has none, placing blocks while the stock lasts.</summary>
	private void StartAssembling(Colony colony, ConstructionJob job)
	{
		if (!colony.Jobs.Contains(job) || (job.Assembler is not null && job.Assembler != this))
		{
			Job = null;
			return;
		}
		job.Assembler = this;
		Insert(Instant(c => AssembleNext(c, job)));
	}

	private void AssembleNext(Colony colony, ConstructionJob job)
	{
		if (!colony.Jobs.Contains(job) || !job.CanAffordNext())
		{
			if (job.Assembler == this)
				job.Assembler = null;
			Job = null;
			return;
		}
		var block = job.Order[job.Built];
		Status = $"Building {job.Name} ({job.Built + 1}/{job.Order.Count})";
		Vector3 cell = job.Site * BlockGrid.CellCenter(block.CellVector());
		Insert(
			Fly(cell + job.Site.Basis.Y * (BlockGrid.CellSize * 1.6f)),
			Wait(Colony.BuildSecondsPerBlock),
			Instant(c => Place(c, job)),
			Instant(c => AssembleNext(c, job)));
	}

	private static void Place(Colony colony, ConstructionJob job)
	{
		if (!colony.Jobs.Contains(job) || !job.CanAffordNext())
			return;
		var entry = job.Order[job.Built];
		foreach (var (item, amount) in ConstructionJob.CostOf(entry))
			job.Stock.TryRemove(item, Mathf.Min(amount, job.Stock.Get(item)));
		if (job.Grid is null)
		{
			job.Grid = BlockGrid.Create(colony.World, job.Site, isStatic: true);
			job.Grid.Label = job.Name;
		}
		var definition = BlockCatalog.Get(entry.Id);
		job.Grid.TryAdd(entry.CellVector(), definition, entry.Orientation(), charge: 0.5f, paint: entry.PaintColor(definition));
		job.Built++;
		Sparkle(colony.World, job.Site * BlockGrid.CellCenter(entry.CellVector()), entry.PaintColor(definition));
	}

	/// <summary>A little puff of warm sparks where a block snaps into place.</summary>
	private static void Sparkle(Node3D world, Vector3 at, Color paint)
	{
		var gradient = new Gradient();
		gradient.SetColor(0, new Color(1f, 0.95f, 0.8f));
		gradient.SetColor(1, new Color(paint.R, paint.G, paint.B, 0f));
		var sparks = new CpuParticles3D
		{
			OneShot = true,
			Emitting = true,
			Amount = 28,
			Lifetime = 0.9f,
			Explosiveness = 0.9f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = BlockGrid.CellSize * 0.6f,
			Direction = Vector3.Up,
			Spread = 180f,
			InitialVelocityMin = 1.5f,
			InitialVelocityMax = 4f,
			Gravity = Vector3.Zero,
			DampingMin = 2f,
			DampingMax = 4f,
			ScaleAmountMin = 0.6f,
			ScaleAmountMax = 1.2f,
			ColorRamp = gradient,
			Mesh = new SphereMesh
			{
				Radius = 0.09f,
				Height = 0.18f,
				RadialSegments = 6,
				Rings = 3,
				Material = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					VertexColorUseAsAlbedo = true,
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				},
			},
			Position = at,
		};
		world.AddChild(sparks);
		sparks.GetTree().CreateTimer(2.0).Timeout += sparks.QueueFree;
	}

	private static Vector3 SiteHover(ConstructionJob job)
	{
		float top = job.Design.Bounds().End.Y;
		return job.Site * new Vector3(0f, top + BlockGrid.CellSize * 2f, 0f);
	}

	// ------------------------------------------------------------ step helpers

	private void FlyTo(Vector3 target) => _steps.Add(Fly(target));
	private void WaitFor(float seconds) => _steps.Add(Wait(seconds));
	private void Do(System.Action<Colony> action) => _steps.Add(Instant(action));

	/// <summary>Runs the steps right after the current one.</summary>
	private void Insert(params Step[] steps) => _steps.InsertRange(Mathf.Min(1, _steps.Count), steps);

	private static Step Instant(System.Action<Colony> action) => (colony, _) =>
	{
		action(colony);
		return true;
	};

	private Step Wait(float seconds) => (_, dt) =>
	{
		if (_wait < 0f)
			_wait = seconds;
		_wait -= dt;
		if (_wait > 0f)
			return false;
		_wait = -1f;
		return true;
	};

	private Step Fly(Vector3 target) => (colony, dt) =>
	{
		_path ??= colony.PlanPath(Grid.GlobalPosition, target);
		if (_waypoint >= _path.Count)
		{
			_path = null;
			_waypoint = 0;
			return true;
		}
		Vector3 position = Grid.GlobalPosition;
		Vector3 next = _path[_waypoint];
		Vector3 toNext = next - position;
		float distance = toNext.Length();
		bool last = _waypoint == _path.Count - 1;
		// Ease into the final spot; cruise through the others.
		float speed = last ? Mathf.Min(Speed, 1.5f + distance * 1.5f) : Speed;
		float step = speed * dt;
		if (distance <= Mathf.Max(step, 0.05f))
		{
			Grid.GlobalPosition = next;
			_waypoint++;
			if (_waypoint >= _path.Count)
			{
				_path = null;
				_waypoint = 0;
				return true;
			}
			return false;
		}
		Vector3 direction = toNext / distance;
		Grid.GlobalPosition = position + direction * step;

		// Turn to face where it is going, staying upright relative to the nearest body.
		Vector3 up = colony.UpAt(position);
		Vector3 flat = direction - up * direction.Dot(up);
		if (flat.LengthSquared() > 1e-3f)
		{
			var target = Basis.LookingAt(flat.Normalized(), up);
			var current = Grid.GlobalBasis.Orthonormalized();
			Grid.GlobalBasis = new Basis(current.GetRotationQuaternion().Slerp(target.GetRotationQuaternion(), Mathf.Min(1f, dt * 3f)));
		}
		return false;
	};
}
