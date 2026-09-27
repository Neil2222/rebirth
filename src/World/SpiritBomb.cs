using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.Life;
using Rebirth.Nexus;

namespace Rebirth.World;

/// <summary>
/// The Breach Lance's ball of light. Everyone you have woken — every village, every living planet —
/// sends little white orbs up to it, and it grows as it fills with Resonance. When it is full you fire
/// it: every house lends its light at once, the ball swells, flies to the Box wall and bursts it open.
/// </summary>
public partial class SpiritBomb : Node3D
{
	public const float BoxRadius = 1000f;
	private const int MaxOrbs = 1500;
	private const float BallMin = 0.8f, BallFull = 7f, BallFired = 26f;

	public Colony Colony { get; set; } = null!;
	public People People { get; set; } = null!;
	public BoxWall Wall { get; set; } = null!;

	/// <summary>Raised when the sequence is over and the next Box should load.</summary>
	public event Action? Breached;
	public bool Firing => _phase != Phase.Idle;

	private enum Phase { Idle, Gathering, Launch, Impact, Fade }

	private sealed class Orb
	{
		public Vector3 From, Control;
		public float T, Duration;
	}

	private readonly List<Orb> _orbs = new();
	private readonly RandomNumberGenerator _rng = new();
	private MultiMeshInstance3D _orbView = null!;
	private MeshInstance3D _ball = null!;
	private ShaderMaterial _ballMaterial = null!;
	private OmniLight3D _ballLight = null!;
	private float _spawnTimer;

	// Firing sequence.
	private Phase _phase;
	private float _time;
	private Camera3D _camera = null!;
	private Camera3D? _previousCamera;
	private Vector3 _launchFrom, _hitPoint, _up;
	private CanvasLayer _overlay = null!;
	private ColorRect _white = null!;
	private Label _caption = null!;

	public override void _Ready()
	{
		_rng.Randomize();
		var orbMesh = new SphereMesh { Radius = 0.35f, Height = 0.7f, RadialSegments = 8, Rings = 4 };
		orbMesh.Material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.95f, 0.98f, 1f),
			EmissionEnabled = true,
			Emission = new Color(0.8f, 0.9f, 1f),
			EmissionEnergyMultiplier = 2.5f,
		};
		_orbView = new MultiMeshInstance3D
		{
			Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = orbMesh, InstanceCount = MaxOrbs, VisibleInstanceCount = 0 },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
			// Orbs fly across the whole cluster; never cull the batch.
			CustomAabb = new Aabb(-Vector3.One * BoxRadius * 1.2f, Vector3.One * BoxRadius * 2.4f),
		};
		AddChild(_orbView);

		_ballMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/spirit_ball.gdshader") };
		_ball = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 },
			MaterialOverride = _ballMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
			PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
		};
		AddChild(_ball);
		_ballLight = new OmniLight3D { LightColor = new Color(0.85f, 0.92f, 1f), LightEnergy = 0f, OmniRange = 30f };
		_ball.AddChild(_ballLight);

		_camera = new Camera3D { Far = 20000f, Fov = 65f, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
		_overlay = new CanvasLayer { Layer = 30, Visible = false };
		AddChild(_overlay);
		_white = new ColorRect { Color = new Color(1f, 0.98f, 0.94f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_white.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_overlay.AddChild(_white);
		_caption = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_caption.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
		_caption.GrowHorizontal = Control.GrowDirection.Both;
		_caption.OffsetTop = -150;
		_caption.OffsetLeft = -500;
		_caption.OffsetRight = 500;
		_caption.AddThemeFontSizeOverride("font_size", 30);
		_caption.AddThemeColorOverride("font_color", new Color(1f, 0.97f, 0.9f));
		_caption.AddThemeConstantOverride("outline_size", 10);
		_caption.AddThemeColorOverride("font_outline_color", new Color(0.18f, 0.12f, 0.24f, 0.9f));
		_overlay.AddChild(_caption);
	}

	public override void _ExitTree()
	{
		if (_camera.GetParent() is null)
			_camera.Free();
	}

	/// <summary>The station holding the Lance, if one is built.</summary>
	private (BlockGrid Grid, Vector3 Tip, Vector3 Up)? Lance()
	{
		foreach (var grid in Colony.Grids)
		{
			foreach (var (cell, block) in grid.Blocks)
			{
				if (block.Definition.Kind != BlockKind.BreachLance)
					continue;
				Vector3 up = (grid.GlobalBasis * (block.Orientation * Vector3.Up)).Normalized();
				return (grid, grid.GlobalTransform * BlockGrid.CellCenter(cell) + up * (BlockGrid.CellSize * 0.5f + 6.3f), up);
			}
		}
		return null;
	}

	/// <summary>Where the light comes from: every house, and the surface of every living planet.</summary>
	private Vector3? RandomSource(bool housesOnly)
	{
		var houses = People.Settlements.Where(s => s.Village is not null && IsInstanceValid(s.Village))
			.SelectMany(s => s.Village!.GetChildren().OfType<Node3D>()).ToList();
		var planets = Colony.Bodies.OfType<MiniPlanet>().Where(p => p.Vitality > 0.05f).ToList();
		if (houses.Count > 0 && (housesOnly || planets.Count == 0 || _rng.Randf() < 0.7f))
			return houses[_rng.RandiRange(0, houses.Count - 1)].GlobalPosition + Vector3.Up * 2f;
		if (planets.Count == 0)
			return null;
		var planet = planets[_rng.RandiRange(0, planets.Count - 1)];
		var dir = new Vector3(_rng.Randfn(), _rng.Randfn(), _rng.Randfn()).Normalized();
		return planet.GlobalPosition + dir * (planet.Radius + 2f);
	}

	private Vector3 BallCenter(Vector3 tip, Vector3 up, float radius) => tip + up * (radius + 1.5f);

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		var lance = Lance();
		if (_phase == Phase.Idle)
		{
			_ball.Visible = lance is not null;
			if (lance is null)
			{
				_orbs.Clear();
				DrawOrbs(Vector3.Zero);
				return;
			}
			var (grid, tip, up) = lance.Value;
			float fraction = Colony.LanceFraction;
			grid.LanceCharge = fraction;
			float radius = Mathf.Lerp(BallMin, BallFull, fraction);
			Vector3 center = BallCenter(tip, up, radius);
			PlaceBall(center, radius, 0.55f + fraction * 0.45f);
			// While it is filling, a gentle trickle of light drifts in from everyone.
			if (fraction < 1f && Colony.ResonancePerMinute > 0.01f)
				Spawn(dt, 1.5f + Colony.ResonancePerMinute * 0.05f, center, housesOnly: false);
			DrawOrbs(center);
			return;
		}
		UpdateSequence(dt, lance);
	}

	private void PlaceBall(Vector3 center, float radius, float brightness)
	{
		_ball.GlobalTransform = new Transform3D(Basis.Identity.Scaled(Vector3.One * radius), center);
		_ballMaterial.SetShaderParameter("brightness", brightness);
		_ballLight.LightEnergy = brightness * 2f;
		_ballLight.OmniRange = 12f + radius * 3f;
	}

	private void Spawn(float dt, float perSecond, Vector3 target, bool housesOnly, float speed = 45f)
	{
		_spawnTimer += dt * perSecond;
		while (_spawnTimer >= 1f)
		{
			_spawnTimer -= 1f;
			if (_orbs.Count >= MaxOrbs || RandomSource(housesOnly) is not { } from)
				continue;
			float distance = from.DistanceTo(target);
			// Arc outwards a little so the streams read as streams, not straight lines.
			Vector3 mid = (from + target) * 0.5f + new Vector3(_rng.Randfn(), _rng.Randfn(), _rng.Randfn()) * distance * 0.15f;
			_orbs.Add(new Orb { From = from, Control = mid, Duration = Mathf.Clamp(distance / speed, 1.2f, 9f) * _rng.RandfRange(0.8f, 1.2f) });
		}
	}

	private void DrawOrbs(Vector3 target, float scale = 1f)
	{
		var multimesh = _orbView.Multimesh;
		int shown = 0;
		for (int i = _orbs.Count - 1; i >= 0; i--)
		{
			var orb = _orbs[i];
			orb.T += (float)GetProcessDeltaTime() / orb.Duration;
			if (orb.T >= 1f)
			{
				_orbs.RemoveAt(i);
				continue;
			}
		}
		foreach (var orb in _orbs)
		{
			float t = orb.T;
			Vector3 p = orb.From.Lerp(orb.Control, t).Lerp(orb.Control.Lerp(target, t), t);
			float size = scale * (1f + 0.4f * Mathf.Sin(t * Mathf.Pi));
			multimesh.SetInstanceTransform(shown++, new Transform3D(Basis.Identity.Scaled(Vector3.One * size), p));
		}
		multimesh.VisibleInstanceCount = shown;
	}

	// ------------------------------------------------------------ firing

	/// <summary>Starts the breach. Returns false when there is no full Lance.</summary>
	public bool Fire()
	{
		if (Firing || Lance() is not { } lance || Colony.LanceFraction < 0.999f)
			return false;
		_up = lance.Up;
		_launchFrom = BallCenter(lance.Tip, lance.Up, BallFull);
		// Straight up from the Lance until it meets the wall.
		float b = _launchFrom.Dot(_up);
		float c = _launchFrom.LengthSquared() - BoxRadius * BoxRadius;
		float along = -b + Mathf.Sqrt(Mathf.Max(0f, b * b - c));
		_hitPoint = _launchFrom + _up * along;
		Wall.HoleDirection = _hitPoint.Normalized();

		_previousCamera = GetViewport().GetCamera3D();
		GetParent().AddChild(_camera);
		_camera.Current = true;
		_overlay.Visible = true;
		_phase = Phase.Gathering;
		_time = 0f;
		return true;
	}

	private void UpdateSequence(float dt, (BlockGrid Grid, Vector3 Tip, Vector3 Up)? lance)
	{
		_time += dt;
		Vector3 side = _up.Cross(Mathf.Abs(_up.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
		switch (_phase)
		{
			case Phase.Gathering:
			{
				// Everyone raises their hands: a flood of light from every house, and the ball swells.
				const float duration = 7f;
				float k = Mathf.Min(1f, _time / duration);
				float radius = Mathf.Lerp(BallFull, BallFired, k * k);
				Vector3 center = _launchFrom + _up * (radius - BallFull);
				PlaceBall(center, radius, 1f + k);
				Spawn(dt, 320f, center, housesOnly: false, speed: 110f);
				DrawOrbs(center, 2.2f);
				if (lance is { } l)
					l.Grid.LanceCharge = 1f + k * 2f;
				// Look up at the ball from below and aside, backing off as it grows.
				Vector3 eye = _launchFrom - _up * 10f + side * (45f + radius * 2.2f) - _up * radius * 0.6f;
				_camera.GlobalTransform = new Transform3D(Basis.LookingAt(center - eye, _up), eye);
				_caption.Text = k < 0.5f ? "Everyone, lend me your light..." : $"{People.Settlements.Sum(s => s.Population)} people raise their hands.";
				if (_time >= duration)
					Next(Phase.Launch);
				break;
			}
			case Phase.Launch:
			{
				const float duration = 4.5f;
				float k = Mathf.Min(1f, _time / duration);
				float ease = k * k * (3f - 2f * k);
				Vector3 start = _launchFrom + _up * (BallFired - BallFull);
				Vector3 center = start.Lerp(_hitPoint, ease);
				PlaceBall(center, BallFired, 2f);
				DrawOrbs(center);
				// Chase it from behind and below.
				Vector3 eye = center - _up * (BallFired * 3.5f + 20f) + side * 18f;
				_camera.GlobalTransform = new Transform3D(Basis.LookingAt(center + _up * 40f - eye, side), eye);
				_caption.Text = "";
				if (_time >= duration)
					Next(Phase.Impact);
				break;
			}
			case Phase.Impact:
			{
				const float duration = 3.5f;
				float k = Mathf.Min(1f, _time / duration);
				Wall.Hole = Mathf.Min(1f, k * 1.6f);
				PlaceBall(_hitPoint, BallFired * (1f + k * 3f), 2f * (1f - k));
				_ball.Visible = k < 0.9f;
				Vector3 eye = _hitPoint - _up * 260f + side * 60f;
				_camera.GlobalTransform = new Transform3D(Basis.LookingAt(_hitPoint - eye, side), eye);
				_white.Color = new Color(1f, 0.98f, 0.94f, Mathf.Max(0f, 0.8f - k * 2f));
				_caption.Text = k > 0.4f ? "The Box is broken open." : "";
				if (_time >= duration)
					Next(Phase.Fade);
				break;
			}
			case Phase.Fade:
			{
				const float duration = 2.5f;
				float k = Mathf.Min(1f, _time / duration);
				_white.Color = new Color(1f, 0.98f, 0.94f, k);
				_caption.Text = "Beyond it: a new sky.";
				if (_time >= duration + 0.5f)
				{
					_phase = Phase.Idle;
					Breached?.Invoke();
				}
				break;
			}
		}
	}

	private void Next(Phase phase)
	{
		_phase = phase;
		_time = 0f;
		if (phase == Phase.Impact)
		{
			_white.Color = new Color(1f, 0.98f, 0.94f, 0.8f);
			Shatter();
		}
	}

	/// <summary>Pieces of the Box wall blown outwards where the ball struck, glinting in its colour.</summary>
	private void Shatter()
	{
		var gradient = new Gradient();
		gradient.SetColor(0, new Color(1f, 0.97f, 0.9f));
		gradient.SetColor(1, new Color(0.86f, 0.78f, 1f, 0f));
		var shards = new CpuParticles3D
		{
			OneShot = true,
			Emitting = true,
			Amount = 450,
			Lifetime = 3.2f,
			Explosiveness = 0.95f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = 30f,
			Direction = -_hitPoint.Normalized(),
			Spread = 70f,
			InitialVelocityMin = 40f,
			InitialVelocityMax = 170f,
			Gravity = Vector3.Zero,
			DampingMin = 10f,
			DampingMax = 30f,
			AngularVelocityMin = -360f,
			AngularVelocityMax = 360f,
			ParticleFlagAlignY = true,
			ScaleAmountMin = 0.6f,
			ScaleAmountMax = 1.6f,
			ColorRamp = gradient,
			Mesh = new PrismMesh
			{
				Size = new Vector3(4f, 7f, 0.4f),
				Material = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					VertexColorUseAsAlbedo = true,
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				},
			},
			Position = _hitPoint,
			VisibilityAabb = new Aabb(-Vector3.One * 600f, Vector3.One * 1200f),
		};
		GetParent().AddChild(shards);
	}
}
