using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Items;
using Rebirth.World;

namespace Rebirth.Building;

/// <summary>A small pod of one item travelling through a grid's logistics network.</summary>
public sealed class Parcel
{
	public required string Item { get; init; }
	public required float Amount { get; init; }
	/// <summary>Cells from source to destination, both included.</summary>
	public required List<Vector3I> Path { get; init; }
	/// <summary>Delivered into shared storage rather than a machine's input.</summary>
	public required bool ToStorage { get; init; }
	/// <summary>Distance travelled, in cells along <see cref="Path"/>.</summary>
	public float Progress { get; set; }

	public Vector3I Destination => Path[^1];
}

// Logistics: blocks that carry parcels (tubes, cargo, machines) form a network wherever they touch.
// Machines push finished goods out as parcels and pull what they need from storage; parcels take the
// shortest route, and machines that need an item are served before storage. Everything travels as
// visible pods at a fixed speed, so a factory's flow can be seen at a glance.
public partial class BlockGrid
{
	public const float ParcelSize = 50f;           // kg per parcel
	public const float ParcelSpeed = 4f;           // cells per second
	/// <summary>Ore a refinery works through per second, including Starlight upgrades.</summary>
	public static float RefineryOrePerSecond => 40f * Core.Campaign.Active.RefineryFactor;
	/// <summary>Share of storage kept free of ore, so refined goods always have somewhere to go.</summary>
	public const float IngotReserve = 0.2f;
	public const float DrillInterval = 2.0f;       // seconds between bites
	public const float DrillBite = 1.0f;           // radius (m) of rock removed per bite
	public const float DrillReach = 14f;           // metres in front of the drill face
	public const float DrillSpread = 2.5f;         // radius (m) of the patch under the face it works
	private const float DispatchInterval = 0.25f;

	/// <summary>Order in which refineries ask for ore.</summary>
	private static readonly string[] RefiningPriority = ["iron_ore", "nickel_ore", "silicon_ore", "stone"];
	private static readonly Vector3I[] Directions =
		[Vector3I.Right, Vector3I.Left, Vector3I.Up, Vector3I.Down, Vector3I.Back, Vector3I.Forward];

	private readonly List<Parcel> _parcels = new();
	private Dictionary<Vector3I, int>? _networkOf;                // logistics cell -> network id
	private readonly Dictionary<(Vector3I Cell, string Item), float> _incoming = new();
	private float _incomingStorage;
	private float _dispatchTimer;

	private readonly Dictionary<Vector3I, float> _drillTimers = new();
	private readonly Dictionary<Vector3I, string> _machineStatus = new();
	private float _drillDraw;
	private float _activeRefineryDraw;

	private MultiMeshInstance3D? _parcelView;

	public int ParcelCount => _parcels.Count;

	/// <summary>Kilograms made here since the world loaded (refinery output), for the Nexus's rates.</summary>
	public Dictionary<string, float> Produced { get; } = new();
	/// <summary>Kilograms bots have delivered into this grid's storage since the world loaded.</summary>
	public Dictionary<string, float> Received { get; } = new();

	/// <summary>True while at least one refinery is processing ore.</summary>
	public bool Refining => _activeRefineryDraw > 0f;

	/// <summary>What a machine is doing (drill, refinery), for the HUD.</summary>
	public string MachineStatus(Vector3I cell) => _machineStatus.GetValueOrDefault(cell, "Idle");

	/// <summary>Size of the logistics network the cell belongs to (blocks), or 0 when it isn't part of one.</summary>
	public int NetworkSize(Vector3I cell)
	{
		var networks = Networks();
		return networks.TryGetValue(cell, out int id) ? networks.Values.Count(n => n == id) : 0;
	}

	private void InvalidateNetworks() => _networkOf = null;

	private Dictionary<Vector3I, int> Networks()
	{
		if (_networkOf is not null)
			return _networkOf;
		_networkOf = new Dictionary<Vector3I, int>();
		int next = 0;
		var stack = new Stack<Vector3I>();
		foreach (var (start, block) in _blocks)
		{
			if (!block.Definition.Logistics || _networkOf.ContainsKey(start))
				continue;
			_networkOf[start] = next;
			stack.Push(start);
			while (stack.Count > 0)
			{
				var cell = stack.Pop();
				foreach (var dir in Directions)
				{
					var neighbour = cell + dir;
					if (!_networkOf.ContainsKey(neighbour) && _blocks.TryGetValue(neighbour, out var b) && b.Definition.Logistics)
					{
						_networkOf[neighbour] = next;
						stack.Push(neighbour);
					}
				}
			}
			next++;
		}
		return _networkOf;
	}

	/// <summary>Shortest route between two logistics cells, or null when they are not connected.</summary>
	private List<Vector3I>? Route(Vector3I from, Vector3I to)
	{
		var cameFrom = new Dictionary<Vector3I, Vector3I> { [from] = from };
		var queue = new Queue<Vector3I>();
		queue.Enqueue(from);
		while (queue.Count > 0)
		{
			var cell = queue.Dequeue();
			if (cell == to)
			{
				var path = new List<Vector3I> { to };
				while (path[^1] != from)
					path.Add(cameFrom[path[^1]]);
				path.Reverse();
				return path;
			}
			foreach (var dir in Directions)
			{
				var next = cell + dir;
				if (!cameFrom.ContainsKey(next) && _blocks.TryGetValue(next, out var b) && b.Definition.Logistics)
				{
					cameFrom[next] = cell;
					queue.Enqueue(next);
				}
			}
		}
		return null;
	}

	/// <summary>True when both cells are logistics blocks joined by a chain of touching logistics blocks.</summary>
	public bool SameNetwork(Vector3I a, Vector3I b)
	{
		var networks = Networks();
		return networks.TryGetValue(a, out int na) && networks.TryGetValue(b, out int nb) && na == nb;
	}

	private float Incoming(Vector3I cell, string item) => _incoming.GetValueOrDefault((cell, item));

	private float IncomingTotal(Vector3I cell) => _incoming.Where(kv => kv.Key.Cell == cell).Sum(kv => kv.Value);

	private void Launch(List<Vector3I> path, string item, float amount, bool toStorage)
	{
		_parcels.Add(new Parcel { Item = item, Amount = amount, Path = path, ToStorage = toStorage });
		if (toStorage)
			_incomingStorage += amount;
		else
			_incoming[(path[^1], item)] = Incoming(path[^1], item) + amount;
	}

	// ------------------------------------------------------------ simulation

	private void UpdateLogistics(float dt)
	{
		UpdateDrills(dt);
		UpdateRefineries(dt);
		UpdateTerraformers(dt);
		MoveParcels(dt);

		_dispatchTimer -= dt;
		if (_dispatchTimer > 0f)
			return;
		_dispatchTimer = DispatchInterval;
		PushOutputs();
		PullForDemands();
	}

	private void MoveParcels(float dt)
	{
		for (int i = _parcels.Count - 1; i >= 0; i--)
		{
			var parcel = _parcels[i];
			parcel.Progress += dt * ParcelSpeed;
			if (parcel.Progress < parcel.Path.Count - 1)
				continue;
			_parcels.RemoveAt(i);
			Deliver(parcel);
		}
	}

	private void Deliver(Parcel parcel)
	{
		if (parcel.ToStorage)
		{
			_incomingStorage = Mathf.Max(0f, _incomingStorage - parcel.Amount);
			Inventory.Add(parcel.Item, parcel.Amount);
			return;
		}
		var key = (parcel.Destination, parcel.Item);
		_incoming[key] = Mathf.Max(0f, Incoming(parcel.Destination, parcel.Item) - parcel.Amount);
		if (_incoming[key] <= 0f)
			_incoming.Remove(key);
		// The destination may have been removed or filled up meanwhile; storage catches the rest.
		float accepted = _state.TryGetValue(parcel.Destination, out var state) && state.Input is { } input
			? input.Add(parcel.Item, parcel.Amount)
			: 0f;
		if (accepted < parcel.Amount)
			Inventory.Add(parcel.Item, parcel.Amount - accepted);
	}

	/// <summary>Machines with finished goods send one parcel each: to a machine that wants it, else storage.</summary>
	private void PushOutputs()
	{
		var networks = Networks();
		foreach (var (cell, state) in _state)
		{
			if (state.Output is not { } output || output.Items.Count == 0 || !networks.ContainsKey(cell) || _quarantined.Contains(cell))
				continue;
			var (item, have) = output.Items.First();
			float amount = Mathf.Min(ParcelSize, have);

			var route = RouteToConsumer(cell, item, amount) ?? RouteToStorage(cell, item, amount);
			if (route is not { } found)
				continue;   // nowhere to go: the machine backs up until something frees
			output.TryRemove(item, amount);
			Launch(found.Path, item, amount, found.ToStorage);
		}
	}

	/// <summary>Machines missing inputs get one parcel each from storage, if storage is on their network.</summary>
	private void PullForDemands()
	{
		foreach (var (cell, block) in _blocks)
		{
			foreach (var (item, wanted) in Demand(cell, block))
			{
				float amount = Mathf.Min(Mathf.Min(ParcelSize, wanted), Inventory.Get(item));
				if (amount < 0.5f)
					continue;
				if (NearestStorageRoute(cell) is not { } route)
					break;
				route.Reverse();   // from the cargo container to the machine
				Inventory.TryRemove(item, amount);
				Launch(route, item, amount, toStorage: false);
				break;
			}
		}
	}

	/// <summary>What a machine still needs, after counting what is already on its way.</summary>
	private IEnumerable<(string Item, float Amount)> Demand(Vector3I cell, PlacedBlock block)
	{
		var state = _state[cell];
		if (state.Input is not { } input || _quarantined.Contains(cell))
			yield break;
		switch (block.Definition.Kind)
		{
			case BlockKind.Refinery:
				float room = input.FreeSpace - IncomingTotal(cell);
				if (room >= 1f)
					foreach (string ore in RefiningPriority)
						yield return (ore, room);
				break;
			case BlockKind.AirProcessor or BlockKind.Hydrator or BlockKind.SeedGarden:
				float space = input.FreeSpace - IncomingTotal(cell);
				if (space >= 1f)
					yield return (TerraformInput(block.Definition.Kind)!, space);
				break;
			case BlockKind.Fabricator:
				if (FabricatorQueue(cell) is not { Count: > 0 } queue || queue[0].Paid)
					break;
				foreach (var (item, needed) in queue[0].Design.TotalCost())
				{
					float missing = needed - input.Get(item) - Incoming(cell, item);
					if (missing >= 0.5f)
						yield return (item, missing);
				}
				break;
		}
	}

	private (List<Vector3I> Path, bool ToStorage)? RouteToConsumer(Vector3I from, string item, float amount)
	{
		List<Vector3I>? best = null;
		foreach (var (cell, block) in _blocks)
		{
			if (cell == from || !Demand(cell, block).Any(d => d.Item == item && d.Amount >= amount * 0.5f))
				continue;
			if (Route(from, cell) is { } path && (best is null || path.Count < best.Count))
				best = path;
		}
		return best is null ? null : (best, false);
	}

	private (List<Vector3I> Path, bool ToStorage)? RouteToStorage(Vector3I from, string item, float amount)
	{
		float reserve = ItemCatalog.Get(item).Category == ItemCategory.Ore ? Inventory.Capacity * IngotReserve : 0f;
		if (Inventory.FreeSpace - _incomingStorage - reserve < amount)
			return null;
		return NearestStorageRoute(from) is { } path ? (path, true) : null;
	}

	/// <summary>Route from <paramref name="from"/> to the closest cargo container on its network.</summary>
	private List<Vector3I>? NearestStorageRoute(Vector3I from)
	{
		List<Vector3I>? best = null;
		foreach (var (cell, block) in _blocks)
		{
			if (block.Definition.Kind != BlockKind.CargoContainer)
				continue;
			if (Route(from, cell) is { } path && (best is null || path.Count < best.Count))
				best = path;
		}
		return best;
	}

	private void UpdateRefineries(float dt)
	{
		float draw = 0f;
		foreach (var (cell, block) in _blocks)
		{
			if (block.Definition.Kind != BlockKind.Refinery || Napping(cell))
				continue;
			var state = _state[cell];
			string? ore = RefiningPriority.FirstOrDefault(o => state.Input!.Get(o) > 0f);
			if (ore is null)
			{
				_machineStatus[cell] = "Waiting for ore";
				continue;
			}
			float amount = Mathf.Min(state.Input!.Get(ore), RefineryOrePerSecond * PowerSatisfaction * dt);
			float produced = ItemCatalog.Refining[ore].Values.Sum() * amount;
			if (produced > state.Output!.FreeSpace)
			{
				_machineStatus[cell] = "Output full";
				continue;
			}
			state.Input.TryRemove(ore, amount);
			foreach (var (product, ratio) in ItemCatalog.Refining[ore])
			{
				state.Output.Add(product, amount * ratio);
				Produced[product] = Produced.GetValueOrDefault(product) + amount * ratio;
			}
			draw += block.Definition.PowerDraw;
			_machineStatus[cell] = $"Refining {ItemCatalog.DisplayName(ore)}";
		}
		_activeRefineryDraw = draw;
	}

	private void UpdateDrills(float dt)
	{
		float draw = 0f;
		foreach (var (cell, block) in _blocks)
		{
			if (block.Definition.Kind != BlockKind.AutoDrill || Napping(cell))
				continue;
			var output = _state[cell].Output!;
			if (output.FreeSpace <= 0f)
			{
				_machineStatus[cell] = "Output full";
				continue;
			}

			// Between bites the drill just keeps turning (and drawing power) while it has something to chew.
			float timer = _drillTimers.GetValueOrDefault(cell) - dt * PowerSatisfaction;
			_drillTimers[cell] = timer;
			bool busy = MachineStatus(cell).StartsWith("Drilling");
			if (timer > 0f)
			{
				if (busy)
					draw += block.Definition.PowerDraw;
				continue;
			}
			_drillTimers[cell] = DrillInterval;

			// Work a patch rather than a single shaft: each bite aims somewhere in a disc under the face,
			// trying a few spots so a dug-out middle doesn't stop the drill while rock remains around it.
			Vector3 front = GlobalBasis * (block.Orientation * Vector3.Forward);
			Vector3 face = GlobalTransform * CellCenter(cell) + front * (CellSize * 0.5f);
			Vector3 side = front.Cross(Mathf.Abs(front.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
			Vector3 other = front.Cross(side);
			var space = GetWorld3D().DirectSpaceState;
			bool bitten = false;
			for (int attempt = 0; attempt < 6 && !bitten; attempt++)
			{
				float angle = GD.Randf() * Mathf.Tau, spread = Mathf.Sqrt(GD.Randf()) * DrillSpread;
				Vector3 from = face + (side * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * spread;
				var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + front * DrillReach, exclude: [GetRid()]));
				if (hit.Count == 0 || hit["collider"].AsGodotObject() is not IMinable rock)
					continue;
				float[] mined = rock.Carve(hit["position"].AsVector3() + front * 0.4f, DrillBite);
				for (int m = 0; m < mined.Length; m++)
					output.Add(VoxelMaterials.All[m].OreItemId, mined[m] * VoxelMaterials.All[m].YieldPerCubicMetre);
				bitten = true;
			}
			if (!bitten)
			{
				_machineStatus[cell] = "No rock within reach";
				continue;
			}
			draw += block.Definition.PowerDraw;
			_machineStatus[cell] = PowerSatisfaction < 0.999f ? "Drilling (low power)" : "Drilling";
		}
		_drillDraw = draw;
	}

	/// <summary>Where every parcel is headed, for saving: its contents count as already delivered.</summary>
	public IEnumerable<Parcel> ParcelsInFlight => _parcels;

	// ------------------------------------------------------------ visuals

	/// <summary>Set by the colony: how full a Breach Lance here is (0..1), which drives its rings and tip.</summary>
	public float LanceCharge { get; set; }

	private void SpinDrillBits(float delta)
	{
		foreach (var (cell, block) in _blocks)
		{
			if (block.Definition.Kind == BlockKind.BreachLance && _decorations.TryGetValue(cell, out var lance))
			{
				if (lance.GetNodeOrNull<Node3D>(BlockVisuals.LanceRingsName) is { } rings)
					rings.RotateObjectLocal(Vector3.Up, (0.2f + LanceCharge * 3f) * delta);
				if (lance.GetNodeOrNull<Node3D>(BlockVisuals.LanceGlowName) is { } tip)
					tip.Scale = Vector3.One * (0.5f + LanceCharge * 1.2f + 0.08f * Mathf.Sin((float)Time.GetTicksMsec() / 200f));
				continue;
			}
			if (block.Definition.Kind != BlockKind.AutoDrill || !MachineStatus(cell).StartsWith("Drilling"))
				continue;
			if (_decorations.TryGetValue(cell, out var decoration) && decoration.GetNodeOrNull<Node3D>(BlockVisuals.DrillBitName) is { } bit)
				bit.RotateObjectLocal(Vector3.Up, 9f * delta);
		}
	}

	/// <summary>Tubes draw glass pipes towards every neighbouring logistics block, so they depend on their neighbours.</summary>
	private void RebuildTubeVisuals()
	{
		foreach (var (cell, block) in _blocks)
		{
			if (block.Definition.Kind != BlockKind.Tube)
				continue;
			if (_decorations.Remove(cell, out var old))
				old.QueueFree();
			var links = Directions.Where(d => _blocks.TryGetValue(cell + d, out var n) && n.Definition.Logistics);
			var tube = BlockVisuals.CreateTube(block.Paint, links);
			tube.Position = CellCenter(cell);
			AddChild(tube);
			_decorations[cell] = tube;
		}
	}


	private void DrawParcels(float delta)
	{
		if (_parcels.Count == 0 && _parcelView is null)
			return;
		if (_parcelView is null)
		{
			var mesh = new SphereMesh { Radius = 0.38f, Height = 0.66f, RadialSegments = 12, Rings = 6 };
			// A soft glow in the item's own colour so pods stand out inside the glass.
			mesh.Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/parcel.gdshader") };
			_parcelView = new MultiMeshInstance3D
			{
				Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				// Positions are set every frame from parcel progress; interpolating them again would lag.
				PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
			};
			AddChild(_parcelView);
		}

		var multimesh = _parcelView.Multimesh;
		if (multimesh.InstanceCount < _parcels.Count)
			multimesh.InstanceCount = Mathf.Max(16, _parcels.Count * 2);
		multimesh.VisibleInstanceCount = _parcels.Count;
		for (int i = 0; i < _parcels.Count; i++)
		{
			var parcel = _parcels[i];
			int segment = Mathf.Min((int)parcel.Progress, parcel.Path.Count - 2);
			float t = parcel.Progress - segment;
			Vector3 position = segment < 0
				? CellCenter(parcel.Path[0])
				: CellCenter(parcel.Path[segment]).Lerp(CellCenter(parcel.Path[segment + 1]), t);
			// A little bounce so pods look like they are rolling along.
			float wobble = 1f + 0.08f * Mathf.Sin(parcel.Progress * Mathf.Tau);
			multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * wobble), position));
			multimesh.SetInstanceColor(i, ItemCatalog.Get(parcel.Item).Color);
		}
	}
}
