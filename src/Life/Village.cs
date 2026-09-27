using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Building;
using Rebirth.World;

namespace Rebirth.Life;

/// <summary>
/// Little toy houses for the villages: rounded walls in soft colours, a pitched roof, a round door and
/// two warm windows (the only light is from real lamps: these are lit rooms). Placed on a spiral around
/// the incubator, standing up on the planet's curve.
/// </summary>
public static class Village
{
	private static readonly Color[] Walls =
		[new(0.97f, 0.9f, 0.8f), new(1f, 0.8f, 0.68f), new(0.8f, 0.9f, 0.78f), new(0.85f, 0.86f, 0.96f), new(1f, 0.9f, 0.62f)];
	private static readonly Color[] Roofs =
		[new(0.86f, 0.42f, 0.36f), new(0.46f, 0.56f, 0.72f), new(0.55f, 0.4f, 0.55f), new(0.9f, 0.58f, 0.3f)];

	/// <returns>False when the slot would land on top of a station (the slot is then skipped).</returns>
	public static bool TryPlaceHouse(Node3D village, MiniPlanet planet, Vector3 anchor, int slot, List<Vector3> stations, int seed)
	{
		Vector3 center = planet.GlobalPosition;
		Vector3 up = (anchor - center).Normalized();
		Vector3 tangent = up.Cross(Mathf.Abs(up.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
		Vector3 bitangent = up.Cross(tangent);
		float angle = slot * 2.39996f;
		float distance = 12f + 4.5f * Mathf.Sqrt(slot);
		Vector3 guess = anchor + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * distance;
		Vector3 dir = (guess - center).Normalized();
		Vector3 ground = planet.SurfacePoint(dir);
		// Not on top of a station, and not in the sea (a little margin for the tide to come).
		if (stations.Any(s => s.DistanceTo(ground) < 8f) || ground.DistanceTo(center) < planet.SeaLevel + 0.8f)
			return false;

		var rng = new RandomNumberGenerator { Seed = (ulong)(uint)(seed * 31 + slot) };
		Vector3 toAnchor = anchor - ground;
		Vector3 facing = (toAnchor - dir * toAnchor.Dot(dir)).Normalized();
		if (facing.LengthSquared() < 0.5f)
			facing = tangent;
		// Front door faces the incubator, like a little village square.
		var basis = Basis.LookingAt(facing, dir);
		var house = Build(Walls[rng.RandiRange(0, Walls.Length - 1)], Roofs[rng.RandiRange(0, Roofs.Length - 1)], rng.RandfRange(0.85f, 1.2f));
		village.AddChild(house);
		house.GlobalTransform = new Transform3D(basis, ground - dir * 0.3f);
		return true;
	}

	private static Node3D Build(Color wall, Color roof, float size)
	{
		var house = new StaticBody3D();
		float w = 3.2f * size, h = 2.4f * size, d = 3.4f * size;
		house.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(w, h, d) }, Position = new Vector3(0, h * 0.5f, 0) });

		house.AddChild(Mesh(new BoxMesh { Size = new Vector3(w, h, d) }, Plastic(wall), new Vector3(0, h * 0.5f, 0)));
		// Pitched roof: a three-sided prism lying along the depth.
		var prism = new PrismMesh { Size = new Vector3(w * 1.15f, h * 0.6f, d * 1.1f) };
		house.AddChild(Mesh(prism, Plastic(roof), new Vector3(0, h + h * 0.3f, 0)));
		// Round door on the front (-Z), and two lit windows.
		house.AddChild(Mesh(new CylinderMesh { TopRadius = 0.45f * size, BottomRadius = 0.45f * size, Height = 0.12f }, Plastic(new Color(0.5f, 0.32f, 0.22f)),
			new Vector3(0, 0.75f * size, -d * 0.5f - 0.02f), new Basis(Vector3.Right, Mathf.Pi / 2f)));
		var glow = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.82f, 0.5f), EmissionEnabled = true, Emission = new Color(1f, 0.72f, 0.4f), EmissionEnergyMultiplier = 1.6f };
		foreach (float x in new[] { -w * 0.3f, w * 0.3f })
			house.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.55f * size, 0.55f * size, 0.08f) }, glow, new Vector3(x, h * 0.62f, -d * 0.5f - 0.03f)));
		house.AddChild(Mesh(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.26f, Height = 1f * size }, Plastic(new Color(0.75f, 0.7f, 0.66f)), new Vector3(w * 0.28f, h + h * 0.45f, d * 0.2f)));
		// A soft porch light so villages twinkle when seen from above.
		house.AddChild(new OmniLight3D { Position = new Vector3(0, h * 0.8f, -d * 0.5f - 0.8f), LightColor = new Color(1f, 0.78f, 0.5f), LightEnergy = 0.8f, OmniRange = 5f });
		return house;
	}

	private static StandardMaterial3D Plastic(Color color) => new() { AlbedoColor = color, Roughness = 0.55f };

	private static MeshInstance3D Mesh(Mesh mesh, Material material, Vector3 position, Basis? basis = null) =>
		new() { Mesh = mesh, MaterialOverride = material, Transform = new Transform3D(basis ?? Basis.Identity, position) };
}
