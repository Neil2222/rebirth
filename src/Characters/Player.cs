using Driftworks.Building;
using Godot;

namespace Driftworks.Characters;

/// <summary>
/// Zero-g astronaut with a 6DOF jetpack. Rotation is driven directly by input
/// (mouse = yaw/pitch, Q/E = roll); translation goes through physics so the
/// player collides with and pushes objects.
/// </summary>
public partial class Player : RigidBody3D
{
	[Export] public float ThrustForce = 1500f;     // Newtons per axis
	[Export] public float MaxSpeed = 100f;         // m/s, same cap as Space Engineers
	[Export] public float MouseSensitivity = 0.0025f;
	[Export] public float RollSpeed = 1.6f;        // rad/s

	public bool DampenersOn { get; private set; } = true;
	public bool JetpackOn { get; private set; } = true;
	public Camera3D Camera { get; private set; } = null!;
	public BuildTool BuildTool { get; private set; } = null!;

	private Vector2 _pendingMouse;

	public override void _Ready()
	{
		Mass = 100f;
		CanSleep = false;
		LockRotation = true;
		LinearDampMode = DampMode.Replace;
		LinearDamp = 0f;
		PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.4f, Bounce = 0.1f };
		CustomIntegrator = false;

		AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f } });

		Camera = new Camera3D { Position = new Vector3(0, 0.6f, 0), Fov = 75f, Near = 0.05f, Far = 20000f, Current = true };
		AddChild(Camera);

		BuildTool = new BuildTool { Camera = Camera, Body = this };
		AddChild(BuildTool);

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
			_pendingMouse += motion.Relative;
		else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
		{
			// Consume the click so it only grabs the mouse and doesn't also place a block.
			Input.MouseMode = Input.MouseModeEnum.Captured;
			GetViewport().SetInputAsHandled();
		}
		else if (e.IsActionPressed("release_mouse"))
			Input.MouseMode = Input.MouseModeEnum.Visible;
		else if (e.IsActionPressed("toggle_dampeners"))
			DampenersOn = !DampenersOn;
		else if (e.IsActionPressed("toggle_jetpack"))
			JetpackOn = !JetpackOn;
	}

	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		float dt = state.Step;
		Basis basis = state.Transform.Basis;

		// Rotation: post-multiply so every axis is relative to the player's current orientation.
		float roll = Input.GetAxis("roll_left", "roll_right");
		if (_pendingMouse != Vector2.Zero || roll != 0f)
		{
			basis *= new Basis(Vector3.Up, -_pendingMouse.X * MouseSensitivity);
			basis *= new Basis(Vector3.Right, -_pendingMouse.Y * MouseSensitivity);
			basis *= new Basis(Vector3.Back, -roll * RollSpeed * dt);
			_pendingMouse = Vector2.Zero;
			state.Transform = new Transform3D(basis.Orthonormalized(), state.Transform.Origin);
		}
		state.AngularVelocity = Vector3.Zero;

		if (!JetpackOn)
			return;

		// Thrust per local axis; dampeners counter drift on any axis without input.
		var input = new Vector3(
			Input.GetAxis("move_left", "move_right"),
			Input.GetAxis("move_down", "move_up"),
			Input.GetAxis("move_forward", "move_back"));
		Vector3 localVelocity = basis.Inverse() * state.LinearVelocity;
		float maxAccel = ThrustForce / Mass;
		Vector3 accel = Vector3.Zero;
		for (int axis = 0; axis < 3; axis++)
		{
			if (Mathf.Abs(input[axis]) > 0.01f)
				accel[axis] = input[axis] * maxAccel;
			else if (DampenersOn)
				accel[axis] = Mathf.Clamp(-localVelocity[axis] / dt, -maxAccel, maxAccel);
		}

		state.LinearVelocity = (state.LinearVelocity + basis * accel * dt).LimitLength(MaxSpeed);
	}
}
