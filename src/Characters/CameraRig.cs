using Godot;
using Godot.Collections;

namespace Rebirth.Characters;

/// <summary>
/// Follows a target (the player's body, or the ship being piloted) in first or third person.
/// Third person sits behind and above a pivot with an over-the-shoulder offset, and is pulled
/// in when geometry is in the way. Free-look orbits around the pivot and springs back on release.
/// </summary>
public partial class CameraRig : Node3D
{
	private const float MaxFreeLookPitch = 1.4f;
	private const float SpringBack = 6f;       // 1/s, how quickly free-look returns
	private const float WallMargin = 0.3f;     // m kept between the camera and whatever blocked it

	public Camera3D Camera { get; private set; } = null!;

	public Node3D? Target { get; set; }
	/// <summary>Pivot and base orientation in the target's space.</summary>
	public Transform3D Anchor { get; set; } = Transform3D.Identity;
	public bool FirstPerson { get; set; }
	public float Distance { get; set; } = 4.5f;
	public float Height { get; set; } = 0.8f;
	public float Side { get; set; } = 0.7f;
	/// <summary>Extra pitch on top of the anchor (the walking head tilt).</summary>
	public float Pitch { get; set; }
	/// <summary>Physics bodies the pull-in check ignores (the target itself).</summary>
	public Array<Rid> Exclude { get; set; } = new();

	public bool FreeLooking { get; set; }
	private Vector2 _freeLook;   // yaw, pitch

	public override void _Ready()
	{
		TopLevel = true;
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		Camera = new Camera3D { Fov = 75f, Near = 0.05f, Far = 20000f, Current = true, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
		AddChild(Camera);
	}

	public void AddFreeLook(Vector2 radians) =>
		_freeLook = new Vector2(_freeLook.X + radians.X, Mathf.Clamp(_freeLook.Y + radians.Y, -MaxFreeLookPitch, MaxFreeLookPitch));

	public override void _Process(double delta)
	{
		if (Target is null)
			return;
		if (!FreeLooking)
			_freeLook = _freeLook.Lerp(Vector2.Zero, 1f - Mathf.Exp(-SpringBack * (float)delta));

		// The interpolated transform keeps the camera smooth between physics ticks.
		Transform3D pivot = Target.GetGlobalTransformInterpolated() * Anchor;
		Basis view = pivot.Basis * new Basis(Vector3.Up, _freeLook.X) * new Basis(Vector3.Right, Pitch + _freeLook.Y);
		if (FirstPerson)
		{
			GlobalTransform = new Transform3D(view.Orthonormalized(), pivot.Origin);
			return;
		}

		Vector3 offset = view * new Vector3(Side, Height, Distance);
		var ray = PhysicsRayQueryParameters3D.Create(pivot.Origin, pivot.Origin + offset, exclude: Exclude);
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
		if (hit.Count > 0)
		{
			float reach = pivot.Origin.DistanceTo(hit["position"].AsVector3()) - WallMargin;
			offset = offset.Normalized() * Mathf.Max(reach, WallMargin);
		}
		GlobalTransform = new Transform3D(view.Orthonormalized(), pivot.Origin + offset);
	}
}
