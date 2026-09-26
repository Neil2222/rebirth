using Driftworks.Building;
using Driftworks.Items;
using Godot;

namespace Driftworks.Characters;

/// <summary>
/// Zero-g astronaut with a 6DOF jetpack. Rotation is driven directly by input
/// (mouse = yaw/pitch, Q/E = roll); translation goes through physics so the
/// player collides with and pushes objects. Can sit in a cockpit to pilot a grid.
/// </summary>
public partial class Player : RigidBody3D
{
	[Export] public float ThrustForce = 1500f;     // Newtons per axis
	[Export] public float MaxSpeed = 100f;         // m/s, same cap as Space Engineers
	[Export] public float MouseSensitivity = 0.0025f;
	[Export] public float RollSpeed = 1.6f;        // rad/s
	[Export] public float ShipMouseRate = 0.06f;   // rad/s of ship rotation per pixel of mouse movement per tick
	[Export] public float ShipMaxTurnRate = 2.5f;  // rad/s

	private const uint PlayerCollisionLayer = 1;

	public bool DampenersOn { get; private set; } = true;
	public bool JetpackOn { get; private set; } = true;
	public Camera3D Camera { get; private set; } = null!;
	public BuildTool BuildTool { get; private set; } = null!;
	public HandDrill Drill { get; private set; } = null!;
	public Inventory Inventory { get; } = new();

	/// <summary>Item in hand, or null for the empty hand.</summary>
	public ToolbarItem? Equipped { get; private set; }

	/// <summary>Grid being piloted, or null when on foot.</summary>
	public BlockGrid? PilotedGrid { get; private set; }

	private Vector3I _cockpitCell;
	private Node3D? _seat;
	private Vector2 _pendingMouse;

	public override void _Ready()
	{
		Mass = 100f;
		CanSleep = false;
		LockRotation = true;
		LinearDampMode = DampMode.Replace;
		LinearDamp = 0f;
		PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.4f, Bounce = 0.1f };
		FreezeMode = FreezeModeEnum.Kinematic;

		AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f } });

		Camera = new Camera3D { Position = new Vector3(0, 0.6f, 0), Fov = 75f, Near = 0.05f, Far = 20000f, Current = true };
		AddChild(Camera);

		BuildTool = new BuildTool { Camera = Camera, Body = this };
		AddChild(BuildTool);
		Drill = new HandDrill { Camera = Camera, Body = this, Inventory = Inventory };
		AddChild(Drill);

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
		else if (e.IsActionPressed("use"))
			Use();
		else if (e.IsActionPressed("toggle_grid_static") && PilotedGrid is null)
			BuildTool.AimedGrid?.ToggleStatic();
		else
		{
			for (int slot = 0; slot <= 9; slot++)
			{
				if (e.IsActionPressed($"slot_{slot}"))
					Equip(slot >= 1 && slot <= Toolbar.Slots.Count ? Toolbar.Slots[slot - 1] : null);
			}
		}
	}

	private void Equip(ToolbarItem? item)
	{
		Equipped = item;
		BuildTool.Select(item?.Block);
		Drill.Equipped = item?.IsDrill == true;
	}

	/// <summary>Hand tools only work on foot.</summary>
	private void SetHandToolsActive(bool active)
	{
		foreach (Node3D tool in new Node3D[] { BuildTool, Drill })
		{
			tool.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
			tool.Visible = active;
		}
	}

	private void Use()
	{
		if (PilotedGrid is not null)
			ExitCockpit();
		else if (BuildTool.AimedGrid is { } grid && grid.TryGet(BuildTool.AimedCell, out var block) && block.Definition.Kind == BlockKind.Cockpit)
			EnterCockpit(grid, BuildTool.AimedCell);
	}

	private void EnterCockpit(BlockGrid grid, Vector3I cell)
	{
		PilotedGrid = grid;
		_cockpitCell = cell;
		grid.BlockRemoved += OnPilotedBlockRemoved;
		grid.ControlFrame = grid.BlockTransform(cell).Basis;
		grid.CanSleep = false;
		grid.Sleeping = false;

		// Park the body: no collisions, no simulation. The camera rides along on a seat node in the grid.
		Freeze = true;
		CollisionLayer = 0;
		CollisionMask = 0;
		SetHandToolsActive(false);

		_seat = new Node3D { Name = "Seat", Transform = grid.BlockTransform(cell) };
		grid.AddChild(_seat);
		Camera.Reparent(_seat, keepGlobalTransform: false);
		Camera.Transform = new Transform3D(Basis.Identity, new Vector3(0, 0.3f, -0.4f));
		Camera.ResetPhysicsInterpolation();
		_pendingMouse = Vector2.Zero;
	}

	private void OnPilotedBlockRemoved(Vector3I cell)
	{
		if (cell == _cockpitCell)
			ExitCockpit();
	}

	private void ExitCockpit()
	{
		var grid = PilotedGrid!;
		grid.BlockRemoved -= OnPilotedBlockRemoved;
		grid.Controls = grid.Controls with { Move = Vector3.Zero, Rotate = Vector3.Zero };
		grid.CanSleep = true;

		Transform3D seat = grid.GlobalTransform * new Transform3D(grid.ControlFrame, BlockGrid.CellCenter(_cockpitCell));
		Transform3D exit = FindExit(seat);

		Camera.Reparent(this, keepGlobalTransform: false);
		Camera.Transform = new Transform3D(Basis.Identity, new Vector3(0, 0.6f, 0));
		_seat!.QueueFree();
		_seat = null;
		PilotedGrid = null;

		GlobalTransform = exit;
		LinearVelocity = grid.LinearVelocity;
		Freeze = false;
		CollisionLayer = PlayerCollisionLayer;
		CollisionMask = PlayerCollisionLayer;
		SetHandToolsActive(true);
		ResetPhysicsInterpolation();
		Camera.ResetPhysicsInterpolation();
	}

	/// <summary>First free spot next to the cockpit: above, behind, the sides, in front, below.</summary>
	private Transform3D FindExit(Transform3D seat)
	{
		var space = GetWorld3D().DirectSpaceState;
		var probe = new PhysicsShapeQueryParameters3D { Shape = new CapsuleShape3D { Radius = 0.45f, Height = 1.9f } };
		Vector3[] directions = [seat.Basis.Y, seat.Basis.Z, -seat.Basis.X, seat.Basis.X, -seat.Basis.Z, -seat.Basis.Y];
		foreach (Vector3 dir in directions)
		{
			var candidate = new Transform3D(seat.Basis, seat.Origin + dir * BlockGrid.CellSize);
			probe.Transform = candidate;
			if (space.IntersectShape(probe, 1).Count == 0)
				return candidate;
		}
		// Fully enclosed cockpit: pop out further above it.
		return new Transform3D(seat.Basis, seat.Origin + seat.Basis.Y * BlockGrid.CellSize * 3f);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (PilotedGrid is not { } grid)
			return;

		Vector2 mouse = _pendingMouse;
		_pendingMouse = Vector2.Zero;
		grid.Controls = new ShipControls
		{
			Move = new Vector3(
				Input.GetAxis("move_left", "move_right"),
				Input.GetAxis("move_down", "move_up"),
				Input.GetAxis("move_forward", "move_back")),
			Rotate = new Vector3(
				Mathf.Clamp(-mouse.Y * ShipMouseRate, -ShipMaxTurnRate, ShipMaxTurnRate),
				Mathf.Clamp(-mouse.X * ShipMouseRate, -ShipMaxTurnRate, ShipMaxTurnRate),
				-Input.GetAxis("roll_left", "roll_right") * RollSpeed),
			Dampeners = DampenersOn,
		};
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
