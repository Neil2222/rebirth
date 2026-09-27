using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Nexus;

namespace Rebirth.Threats;

/// <summary>
/// The Curator's gentle interference. Now and then a virus naps in a machine (quarantine: it stops, it
/// doesn't break) or a curious swarm settles over a site (bots stay away). Nothing escalates and
/// nothing is lost: purge viruses with a circuit puzzle or a Firewall; talk to, gift, or outwit swarms.
/// Starts only once you have a site of your own and have played a few minutes.
/// </summary>
public partial class Threats : Node
{
	public const float GraceSeconds = 300f;
	private const float MinGap = 360f, MaxGap = 600f;
	private const float FirewallSeconds = 20f;
	private const float SwarmBoredSeconds = 900f;
	public const float SwarmReach = 45f;
	private const int MaxViruses = 2;

	public Colony Colony { get; set; } = null!;
	public List<Swarm> Swarms { get; } = new();

	private float _clock;
	private float _next = GraceSeconds;
	private float _tick;
	private readonly Dictionary<(BlockGrid, Vector3I), float> _firewallTimers = new();
	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready() => _rng.Randomize();

	/// <summary>Bots keep away from anything a swarm is watching.</summary>
	public bool Blocked(Vector3 position) => Swarms.Any(s => s.GlobalPosition.DistanceTo(position) < SwarmReach);

	public Swarm? SwarmNear(Vector3 position, float reach) =>
		Swarms.Where(s => s.GlobalPosition.DistanceTo(position) < reach).OrderBy(s => s.GlobalPosition.DistanceTo(position)).FirstOrDefault();

	public override void _PhysicsProcess(double delta)
	{
		_tick += (float)delta;
		if (_tick < 1f)
			return;
		float dt = _tick;
		_tick = 0f;
		_clock += dt;
		RunFirewalls(dt);
		foreach (var swarm in Swarms.ToList())
		{
			swarm.Age += dt;
			if (swarm.Age > SwarmBoredSeconds)
				Resolve(swarm, 0f, $"The swarm over {swarm.Site} got bored and drifted away.");
		}
		// Only once there is something of your own out there, and after a calm start.
		if (_clock < _next || !Colony.Sites.Any())
			return;
		_next = _clock + _rng.RandfRange(MinGap, MaxGap);
		if (_rng.Randf() < 0.35f && Swarms.Count == 0)
			SpawnSwarm();
		else
			Infect();
	}

	// ------------------------------------------------------------ viruses

	/// <summary>Puts a random working machine in quarantine. Returns false when there was nothing to infect.</summary>
	public bool Infect()
	{
		var stations = Colony.Grids.Where(g => g.IsStatic && !g.IsBot).ToList();
		if (stations.Sum(g => g.Quarantined.Count) >= MaxViruses)
			return false;
		var candidates = stations.SelectMany(g => g.Blocks.Where(b => BlockGrid.Infectable(b.Value.Definition) && !g.IsQuarantined(b.Key)).Select(b => (Grid: g, Cell: b.Key, Block: b.Value))).ToList();
		if (candidates.Count == 0)
			return false;
		var (grid, cell, block) = candidates[_rng.RandiRange(0, candidates.Count - 1)];
		grid.Quarantine(cell);
		Colony.Announce($"A little virus crept into the {block.Definition.DisplayName} at {grid.Label ?? "a station"}. It's napping in quarantine.");
		return true;
	}

	/// <summary>A solved puzzle chases the virus out.</summary>
	public void Purge(BlockGrid grid, Vector3I cell)
	{
		if (!grid.IsQuarantined(cell))
			return;
		grid.Purge(cell);
		Colony.AddResonance(10f);
		Colony.Announce($"Virus purged from {grid.Label ?? "a station"}. +10 Resonance");
	}

	/// <summary>Powered Firewalls clean every quarantined machine within uplink range after a short while.</summary>
	private void RunFirewalls(float dt)
	{
		var firewalls = Colony.Grids.Where(g => g.HasBlock(BlockKind.Firewall) && g.PowerSatisfaction > 0.5f).ToList();
		var seen = new HashSet<(BlockGrid, Vector3I)>();
		foreach (var grid in Colony.Grids.Where(g => g.Quarantined.Count > 0).ToList())
		{
			var guard = firewalls.FirstOrDefault(f => f.GlobalPosition.DistanceTo(grid.GlobalPosition) <= Colony.UplinkRange);
			if (guard is null)
				continue;
			foreach (var cell in grid.Quarantined.ToList())
			{
				var key = (grid, cell);
				seen.Add(key);
				float t = _firewallTimers.GetValueOrDefault(key) + dt;
				_firewallTimers[key] = t;
				if (t < FirewallSeconds)
					continue;
				grid.Purge(cell);
				Colony.Announce($"The Firewall at {guard.Label ?? "a station"} chased a virus out of {grid.Label ?? "a station"}.");
			}
		}
		foreach (var key in _firewallTimers.Keys.Where(k => !seen.Contains(k)).ToList())
			_firewallTimers.Remove(key);
	}

	// ------------------------------------------------------------ swarms

	public bool SpawnSwarm()
	{
		var sites = Colony.Sites.Where(s => !Blocked(s.GlobalPosition)).ToList();
		if (sites.Count == 0)
			return false;
		var site = sites[_rng.RandiRange(0, sites.Count - 1)];
		var up = Colony.UpAt(site.GlobalPosition);
		var swarm = new Swarm { Site = site.Label!, Name = "CuratorSwarm" };
		Colony.World.AddChild(swarm, forceReadableName: true);
		swarm.GlobalPosition = site.GlobalPosition + up * 14f;
		Swarms.Add(swarm);
		Colony.Announce($"A curious swarm of the Curator settled over {site.Label}. Bots won't go near it.");
		return true;
	}

	/// <param name="resonance">What sending it off peacefully is worth.</param>
	public void Resolve(Swarm swarm, float resonance, string news)
	{
		// A puzzle can still be open when the swarm drifts off on its own: it only pays out once.
		if (!Swarms.Remove(swarm))
			return;
		swarm.Leave();
		if (resonance > 0f)
			Colony.AddResonance(resonance);
		Colony.Announce(news);
	}

	// ------------------------------------------------------------ saving

	public ThreatsSave ToSave() => new()
	{
		Clock = _clock,
		Next = _next,
		Swarms = Swarms.Select(s => new SwarmSave { Site = s.Site, Position = [s.GlobalPosition.X, s.GlobalPosition.Y, s.GlobalPosition.Z], Age = s.Age }).ToList(),
	};

	public void Restore(ThreatsSave save)
	{
		_clock = save.Clock;
		_next = save.Next > 0f ? save.Next : GraceSeconds;
		foreach (var saved in save.Swarms)
		{
			var swarm = new Swarm { Site = saved.Site, Age = saved.Age, Name = "CuratorSwarm" };
			Colony.World.AddChild(swarm, forceReadableName: true);
			swarm.GlobalPosition = new Vector3(saved.Position[0], saved.Position[1], saved.Position[2]);
			Swarms.Add(swarm);
		}
	}
}

public sealed class ThreatsSave
{
	public float Clock { get; set; }
	public float Next { get; set; }
	public List<SwarmSave> Swarms { get; set; } = new();
}

public sealed class SwarmSave
{
	public string Site { get; set; } = "";
	public float[] Position { get; set; } = [0, 0, 0];
	public float Age { get; set; }
}

/// <summary>A cloud of tiny lavender diamonds circling over a site, blinking in patterns.</summary>
public partial class Swarm : Node3D
{
	private const int Count = 60;

	public string Site { get; set; } = "";
	public float Age { get; set; }

	private MultiMeshInstance3D _view = null!;
	private readonly Vector3[] _axes = new Vector3[Count];
	private readonly float[] _radii = new float[Count], _speeds = new float[Count], _phases = new float[Count];
	private float _time;
	private float _leaving = -1f;

	public override void _Ready()
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(uint)Core.StableHash.Of(Site) };
		for (int i = 0; i < Count; i++)
		{
			_axes[i] = new Vector3(rng.Randfn(), rng.Randfn(), rng.Randfn()).Normalized();
			_radii[i] = rng.RandfRange(3f, 8f);
			_speeds[i] = rng.RandfRange(0.4f, 1.2f) * (rng.Randf() < 0.5f ? -1f : 1f);
			_phases[i] = rng.RandfRange(0f, Mathf.Tau);
		}
		var mesh = new SphereMesh { Radius = 0.35f, Height = 0.9f, RadialSegments = 4, Rings = 2 };
		mesh.Material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.82f, 0.72f, 1f),
			EmissionEnabled = true,
			Emission = new Color(0.7f, 0.55f, 1f),
			EmissionEnergyMultiplier = 1.4f,
		};
		_view = new MultiMeshInstance3D
		{
			Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = Count },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
			CustomAabb = new Aabb(-Vector3.One * 20f, Vector3.One * 40f),
		};
		AddChild(_view);
	}

	/// <summary>Scatters outwards and fades, then frees itself.</summary>
	public void Leave() => _leaving = 0f;

	public override void _Process(double delta)
	{
		_time += (float)delta;
		float spread = 1f;
		if (_leaving >= 0f)
		{
			_leaving += (float)delta;
			spread = 1f + _leaving * 6f;
			if (_leaving > 2.5f)
			{
				QueueFree();
				return;
			}
		}
		var multimesh = _view.Multimesh;
		for (int i = 0; i < Count; i++)
		{
			var axis = _axes[i];
			var side = axis.Cross(Mathf.Abs(axis.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
			var p = side.Rotated(axis, _phases[i] + _time * _speeds[i]) * _radii[i] * spread;
			// Blink in waves so it looks like it is thinking.
			float blink = 0.6f + 0.4f * Mathf.Sin(_time * 3f + i * 0.7f);
			multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * blink), p));
		}
	}
}
