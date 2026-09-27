using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;
using Rebirth.Building;
using Rebirth.Items;
using Rebirth.Nexus;
using Rebirth.World;

namespace Rebirth.Life;

/// <summary>A village growing around an incubator on a healed planet.</summary>
public sealed class Settlement
{
	public string Name { get; set; } = "";
	public string Planet { get; set; } = "";
	/// <summary>Label of the station with the incubator (and the depot for deliveries).</summary>
	public string Anchor { get; set; } = "";
	public int Population { get; set; }
	/// <summary>0..100: how close you are. Grows with every request you answer.</summary>
	public float Bond { get; set; }
	public Request? Request { get; set; }
	public float GrowthTimer { get; set; }
	public float RequestTimer { get; set; }
	public float NextRequestIn { get; set; } = 40f;
	public int RequestsDone { get; set; }

	[JsonIgnore] public Node3D? Village { get; set; }
	[JsonIgnore] public int HousesBuilt { get; set; }
	[JsonIgnore] public int NextSlot { get; set; }
}

/// <summary>
/// The people you wake from the DNA you carry: incubators on habitable planets grow villages, the
/// villagers ask for things, and the bond you build with them turns into Resonance.
/// </summary>
public partial class People : Node
{
	public const float HabitableAir = 0.5f;
	public const float HabitableWater = 0.5f;
	public const float HabitableSoil = 0.3f;
	public const float VillageReach = 70f;     // m: build requests count stations this close
	private const float FamilySeconds = 45f;
	private const int MaxHouses = 24;

	public Colony Colony { get; set; } = null!;
	public List<Settlement> Settlements { get; } = new();

	private float _tick;
	private readonly RandomNumberGenerator _rng = new();

	public float ResonancePerMinute => Settlements.Sum(s => s.Population * 0.4f * (0.5f + s.Bond / 100f));

	public static bool IsHabitable(MiniPlanet planet) =>
		planet.Air >= HabitableAir && planet.Water >= HabitableWater && planet.Soil >= HabitableSoil;

	/// <summary>How many people a planet's villages can hold: more soil, more room.</summary>
	public static int Capacity(MiniPlanet planet) => 4 + Mathf.RoundToInt(36f * planet.Soil);

	public override void _Ready() => _rng.Randomize();

	public override void _PhysicsProcess(double delta)
	{
		_tick += (float)delta;
		if (_tick < 1f)
			return;
		float dt = _tick;
		_tick = 0f;
		foreach (var grid in Colony.Grids.Where(g => g.IsStatic && !g.IsBot && g.HasBlock(BlockKind.Incubator)).ToList())
			UpdateIncubator(grid, dt);
		foreach (var settlement in Settlements)
			UpdateVillage(settlement);
	}

	public Settlement? At(BlockGrid grid) => Settlements.FirstOrDefault(s => s.Anchor == grid.Label);

	private void UpdateIncubator(BlockGrid grid, float dt)
	{
		var planet = Colony.BodyOf(grid) is MiniPlanet p && p.GlobalPosition.DistanceTo(grid.GlobalPosition) < p.OuterRadius + 25f ? p : null;
		grid.OnPlanet = planet is not null;
		grid.Habitable = planet is not null && IsHabitable(planet);
		if (planet is null)
			return;
		if (grid.Label is null)
		{
			int n = 1;
			while (Colony.Find($"{planet.Name} Settlement Seed {n}") is not null)
				n++;
			grid.Label = $"{planet.Name} Settlement Seed {n}";
		}

		var settlement = At(grid);
		if (!grid.Habitable)
		{
			if (settlement is not null)
				grid.IncubatorNote = $"{settlement.Name} waits for the planet to recover";
			return;
		}
		if (settlement is null)
		{
			string name = RequestCatalog.VillageNames[Settlements.Count % RequestCatalog.VillageNames.Length];
			settlement = new Settlement { Name = name, Planet = planet.Name, Anchor = grid.Label, Population = 2 };
			Settlements.Add(settlement);
			Colony.Announce($"The first family woke up on {planet.Name}. Welcome to {name}!");
		}

		int capacity = Capacity(planet);
		float interval = FamilySeconds / (1f + settlement.Bond / 50f);
		if (settlement.Population < capacity && grid.PowerSatisfaction > 0.5f)
		{
			settlement.GrowthTimer += dt;
			if (settlement.GrowthTimer >= interval)
			{
				settlement.GrowthTimer = 0f;
				settlement.Population += 2;
				if (settlement.Population % 10 == 0)
					Colony.Announce($"{settlement.Name} has grown to {settlement.Population} people");
			}
			grid.IncubatorNote = $"Next family in {Mathf.Max(0f, interval - settlement.GrowthTimer):0} s  ({settlement.Population}/{capacity} people)";
		}
		else
			grid.IncubatorNote = settlement.Population >= capacity
				? $"{settlement.Name} is full for now ({settlement.Population}): more soil makes room"
				: "Incubating slowly: not enough power";

		UpdateRequest(settlement, grid, dt);
	}

	// ------------------------------------------------------------ requests

	private void UpdateRequest(Settlement settlement, BlockGrid depot, float dt)
	{
		if (settlement.Request is not { } request)
		{
			settlement.RequestTimer += dt;
			if (settlement.RequestTimer >= settlement.NextRequestIn)
			{
				settlement.Request = NewRequest(settlement);
				Colony.Announce($"{settlement.Request.Person} of {settlement.Name} has a request");
			}
			return;
		}
		switch (request.Kind)
		{
			case RequestKind.Deliver when depot.Inventory.Get(request.Item) >= request.Amount:
				depot.Inventory.TryRemove(request.Item, request.Amount);
				ProductionStats.Consumed(request.Item, request.Amount);
				Fulfil(settlement, 10f, $"{request.Person}: \"It arrived! Thank you so much.\"");
				break;
			case RequestKind.Build when CountNear(settlement, request.Block) > request.Baseline:
				Fulfil(settlement, 14f, $"{request.Person}: \"It's wonderful. Everyone came out to look.\"");
				break;
		}
	}

	private Request NewRequest(Settlement settlement)
	{
		var request = new Request { Person = RequestCatalog.Names[_rng.RandiRange(0, RequestCatalog.Names.Length - 1)] };
		// Talks often, deliveries next, build jobs once the village has settled a little.
		float roll = _rng.Randf();
		if (roll < 0.4f)
		{
			request.Kind = RequestKind.Talk;
			request.Dialogue = _rng.RandiRange(0, RequestCatalog.Dialogues.Length - 1);
			request.Text = $"{request.Person} would like to talk to you.";
		}
		else if (roll < 0.75f || settlement.Population < 6)
		{
			var (item, min, max, why) = RequestCatalog.Deliveries[_rng.RandiRange(0, RequestCatalog.Deliveries.Length - 1)];
			request.Kind = RequestKind.Deliver;
			request.Item = item;
			request.Amount = Mathf.Round(_rng.RandiRange(min, max) / 10f) * 10f;
			request.Text = Fill(why.Replace("{amount}", $"{request.Amount:0}"), settlement, request.Person);
		}
		else
		{
			var (block, ask) = RequestCatalog.Builds[_rng.RandiRange(0, RequestCatalog.Builds.Length - 1)];
			request.Kind = RequestKind.Build;
			request.Block = block;
			request.Baseline = CountNear(settlement, block);
			request.Text = Fill(ask, settlement, request.Person);
		}
		// Now and then they have something for you in return.
		if (_rng.Randf() < 0.35f)
		{
			request.GiftItem = new[] { "iron_ingot", "nickel_ingot", "silicon_wafer" }[_rng.RandiRange(0, 2)];
			request.GiftAmount = _rng.RandiRange(10, 40) * 10f;
		}
		return request;
	}

	/// <summary>Answers the open conversation; returns what the villager says back.</summary>
	public string Answer(Settlement settlement, int answer)
	{
		if (settlement.Request is not { Kind: RequestKind.Talk } request)
			return "";
		var chosen = RequestCatalog.Dialogues[request.Dialogue].Answers[answer];
		string reply = Fill(chosen.Reply, settlement, request.Person);
		Fulfil(settlement, chosen.Bond, null);
		return reply;
	}

	private void Fulfil(Settlement settlement, float bond, string? thanks)
	{
		var request = settlement.Request!;
		settlement.Bond = Mathf.Min(100f, settlement.Bond + bond);
		settlement.Request = null;
		settlement.RequestTimer = 0f;
		settlement.NextRequestIn = _rng.RandfRange(100f, 180f);
		settlement.RequestsDone++;
		Colony.AddResonance(15f + bond);
		if (request.GiftAmount > 0f && Colony.Home is { } home)
		{
			ProductionStats.Produced(request.GiftItem, home.Inventory.Add(request.GiftItem, request.GiftAmount));
			Colony.Announce($"{settlement.Name} sent a gift home: {request.GiftAmount:0} kg {ItemCatalog.DisplayName(request.GiftItem)}");
		}
		if (thanks is not null)
			Colony.Announce(thanks);
	}

	private int CountNear(Settlement settlement, BlockKind kind)
	{
		if (Colony.Find(settlement.Anchor) is not { } anchor)
			return 0;
		return Colony.Grids.Where(g => g.IsStatic && !g.IsBot && g.GlobalPosition.DistanceTo(anchor.GlobalPosition) <= VillageReach)
			.Sum(g => g.Blocks.Count(b => b.Value.Definition.Kind == kind));
	}

	public static string Fill(string text, Settlement settlement, string person) =>
		text.Replace("{name}", person).Replace("{village}", settlement.Name).Replace("{planet}", settlement.Planet);

	/// <summary>Progress on the open request, for panels ("120 / 400 kg in the depot").</summary>
	public string Progress(Settlement settlement)
	{
		if (settlement.Request is not { } request || Colony.Find(settlement.Anchor) is not { } depot)
			return "";
		return request.Kind switch
		{
			RequestKind.Deliver => $"{Mathf.Min(depot.Inventory.Get(request.Item), request.Amount):0} / {request.Amount:0} kg {ItemCatalog.DisplayName(request.Item)} in the {depot.Label} depot",
			RequestKind.Build => $"Build a station with a {BlockCatalog.All.First(b => b.Kind == request.Block).DisplayName} within {VillageReach:0} m",
			_ => Core.Keybinds.Fill("Answer from the Nexus or at the incubator [{use}]"),
		};
	}

	// ------------------------------------------------------------ the village itself

	/// <summary>Houses appear around the incubator as the population grows, one for every two or three people.</summary>
	private void UpdateVillage(Settlement settlement)
	{
		int wanted = Mathf.Min(MaxHouses, Mathf.CeilToInt(settlement.Population / 2.5f));
		if (wanted <= settlement.HousesBuilt)
			return;
		if (Colony.Find(settlement.Anchor) is not { } anchor || Colony.Bodies.OfType<MiniPlanet>().FirstOrDefault(p => p.Name == settlement.Planet) is not { } planet)
			return;
		if (settlement.Village is null || !IsInstanceValid(settlement.Village))
		{
			settlement.Village = new Node3D { Name = $"Village {settlement.Name}" };
			Colony.World.AddChild(settlement.Village);
			settlement.HousesBuilt = 0;
			settlement.NextSlot = 0;
		}
		var stations = Colony.Grids.Where(g => g.IsStatic).Select(g => g.GlobalPosition).ToList();
		// Slots spiral out from the incubator; ones that would land on a station are skipped for good.
		while (settlement.HousesBuilt < wanted && settlement.NextSlot < MaxHouses * 3)
		{
			if (Village.TryPlaceHouse(settlement.Village, planet, anchor.GlobalPosition, settlement.NextSlot, stations, Core.StableHash.Of(settlement.Name)))
				settlement.HousesBuilt++;
			settlement.NextSlot++;
		}
	}

	public void Restore(IEnumerable<Settlement> saved)
	{
		Settlements.AddRange(saved);
		foreach (var settlement in Settlements)
			UpdateVillage(settlement);
	}
}
