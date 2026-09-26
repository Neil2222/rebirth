using System.Linq;
using Rebirth.Building;
using Rebirth.Core;
using Rebirth.Items;
using Rebirth.Persistence;
using Godot;

namespace Rebirth.Characters;

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
	[Export] public float WalkSpeed = 5f;          // m/s
	[Export] public float SprintSpeed = 9f;        // m/s
	[Export] public float JumpSpeed = 5f;          // m/s

	private const float WalkGravityThreshold = 0.5f;  // m/s²; weaker gravity still means floating
	private const float UprightRate = 3f;            // rad/s
	private const float GroundProbe = 1.1f;          // capsule half-height (0.9) plus slack
	private const float GroundAcceleration = 40f;
	private const float AirAcceleration = 4f;

	private const uint PlayerCollisionLayer = 1;
	public const float CarryCapacity = 1500f;   // kg
	private const double MessageSeconds = 4.0;

	public bool DampenersOn { get; private set; } = true;
	public bool JetpackOn { get; private set; } = true;
	public Camera3D Camera { get; private set; } = null!;
	public SpotLight3D HelmetLight { get; private set; } = null!;
	public BuildTool BuildTool { get; private set; } = null!;
	public HandDrill Drill { get; private set; } = null!;
	public Inventory Inventory { get; } = new() { Capacity = CarryCapacity };

	/// <summary>Gravity acting on the player (m/s²), from the physics state.</summary>
	public Vector3 Gravity { get; private set; }
	/// <summary>Jetpack off in gravity: upright, walking, jumping.</summary>
	public bool Walking { get; private set; }
	public bool Grounded { get; private set; }

	private float _headPitch;   // walking only; flying pitches the whole body

	/// <summary>Creative mode builds for free; survival pays ingots from the inventory.</summary>
	public bool Creative { get; private set; }

	/// <summary>Short feedback line for the HUD, or null when there is nothing recent to say.</summary>
	public string? Message => Time.GetTicksMsec() / 1000.0 - _messageTime < MessageSeconds ? _message : null;

	/// <summary>Item in hand, or null for the empty hand.</summary>
	public ToolbarItem? Equipped { get; private set; }

	/// <summary>Grid being piloted, or null when on foot.</summary>
	public BlockGrid? PilotedGrid { get; private set; }

	private Vector3I _cockpitCell;
	private Node3D? _seat;
	private Vector2 _pendingMouse;
	private string? _message;
	private double _messageTime = double.NegativeInfinity;

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
		HelmetLight = new SpotLight3D { SpotRange = 45f, SpotAngle = 32f, LightEnergy = 3f, Position = new Vector3(0.15f, 0.1f, 0f) };
		Camera.AddChild(HelmetLight);

		BuildTool = new BuildTool { Camera = Camera, Body = this, CostSource = Inventory };
		AddChild(BuildTool);
		Drill = new HandDrill { Camera = Camera, Body = this, Inventory = Inventory };
		AddChild(Drill);

		// Starter kit: enough ingots for a handful of blocks before the first refinery run.
		Inventory.Add("iron_ingot", 800f);
		Inventory.Add("nickel_ingot", 150f);
		Inventory.Add("silicon_wafer", 150f);

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	// Movement input, silenced while a full-screen tool such as the Forge is open.
	private static float Axis(string negative, string positive) => GameState.WorldInputBlocked ? 0f : Input.GetAxis(negative, positive);
	private static bool Held(string action) => !GameState.WorldInputBlocked && Input.IsActionPressed(action);

	public override void _UnhandledInput(InputEvent e)
	{
		if (GameState.WorldInputBlocked)
			return;
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
		else if (e.IsActionPressed("toggle_light"))
			HelmetLight.Visible = !HelmetLight.Visible;
		else if (e.IsActionPressed("use"))
			Use();
		else if (e.IsActionPressed("toggle_grid_static") && PilotedGrid is null)
			BuildTool.AimedGrid?.ToggleStatic();
		else if (e.IsActionPressed("toggle_creative"))
		{
			Creative = !Creative;
			BuildTool.CostSource = Creative ? null : Inventory;
			ShowMessage(Creative ? "Creative mode: building is free" : "Survival mode: blocks cost ingots");
		}
		else
		{
			for (int key = 0; key <= 9; key++)
			{
				if (!e.IsActionPressed($"slot_{key}"))
					continue;
				int index = (key + 9) % 10;   // keys 1..9 then 0
				ToolbarItem? item = index < Toolbar.Slots.Count ? Toolbar.Slots[index] : null;
				Equip(item == Equipped ? null : item);
			}
		}
	}

	public void ShowMessage(string message)
	{
		_message = message;
		_messageTime = Time.GetTicksMsec() / 1000.0;
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
		{
			ExitCockpit();
			return;
		}
		if (BuildTool.AimedGrid is not { } grid || !grid.TryGet(BuildTool.AimedCell, out var block))
			return;
		if (block.Definition.Kind == BlockKind.Cockpit)
			EnterCockpit(grid, BuildTool.AimedCell);
		else if (block.Definition.CargoCapacity > 0f)
			TradeWith(grid.Inventory);
	}

	/// <summary>Unloads carried ore into a grid's inventory and picks up the ingots it holds.</summary>
	private void TradeWith(Inventory grid)
	{
		float deposited = 0f, collected = 0f;
		foreach (var (id, amount) in Inventory.Items.ToArray())
			if (ItemCatalog.Get(id).Category == ItemCategory.Ore)
				deposited += Inventory.TransferTo(grid, id, amount);
		foreach (var (id, amount) in grid.Items.ToArray())
			if (ItemCatalog.Get(id).Category == ItemCategory.Ingot)
				collected += grid.TransferTo(Inventory, id, amount);
		ShowMessage($"Deposited {deposited:0} kg ore, took {collected:0} kg ingots");
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
		_headPitch = 0f;
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

	public PlayerSave ToSave()
	{
		// While piloting, the body is parked somewhere else; save where getting out would put us.
		Transform3D transform = PilotedGrid is { } grid
			? FindExit(grid.GlobalTransform * new Transform3D(grid.ControlFrame, BlockGrid.CellCenter(_cockpitCell)))
			: GlobalTransform;
		var (position, rotation) = SaveMath.ToArrays(transform);
		return new PlayerSave
		{
			Position = position,
			Rotation = rotation,
			Velocity = SaveMath.ToArray(PilotedGrid?.LinearVelocity ?? LinearVelocity),
			Jetpack = JetpackOn,
			Dampeners = DampenersOn,
			Creative = Creative,
			Light = HelmetLight.Visible,
			Inventory = Inventory.Items.ToDictionary(kv => kv.Key, kv => kv.Value),
		};
	}

	public void ApplySave(PlayerSave save)
	{
		GlobalTransform = SaveMath.Transform(save.Position, save.Rotation);
		LinearVelocity = SaveMath.Vector(save.Velocity);
		JetpackOn = save.Jetpack;
		DampenersOn = save.Dampeners;
		Creative = save.Creative;
		BuildTool.CostSource = Creative ? null : Inventory;
		HelmetLight.Visible = save.Light;
		Inventory.Clear();
		foreach (var (item, amount) in save.Inventory)
			Inventory.Add(item, amount);
		ResetPhysicsInterpolation();
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
				Axis("move_left", "move_right"),
				Axis("move_down", "move_up"),
				Axis("move_forward", "move_back")),
			Rotate = new Vector3(
				Mathf.Clamp(-mouse.Y * ShipMouseRate, -ShipMaxTurnRate, ShipMaxTurnRate),
				Mathf.Clamp(-mouse.X * ShipMouseRate, -ShipMaxTurnRate, ShipMaxTurnRate),
				-Axis("roll_left", "roll_right") * RollSpeed),
			Dampeners = DampenersOn,
		};
	}

	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		Gravity = state.TotalGravity;
		bool walk = !JetpackOn && Gravity.LengthSquared() > WalkGravityThreshold * WalkGravityThreshold;
		if (walk != Walking)
		{
			Walking = walk;
			// Switching to free flight folds the head's pitch into the body, so the view doesn't jump.
			if (!walk)
			{
				var folded = state.Transform.Basis * new Basis(Vector3.Right, _headPitch);
				state.Transform = new Transform3D(folded.Orthonormalized(), state.Transform.Origin);
				_headPitch = 0f;
				Camera.Rotation = Vector3.Zero;
			}
		}
		state.AngularVelocity = Vector3.Zero;

		if (walk)
			Walk(state);
		else
			Fly(state);
	}

	private void Fly(PhysicsDirectBodyState3D state)
	{
		float dt = state.Step;
		Basis basis = state.Transform.Basis;
		Grounded = false;

		// Rotation: post-multiply so every axis is relative to the player's current orientation.
		float roll = Axis("roll_left", "roll_right");
		if (_pendingMouse != Vector2.Zero || roll != 0f)
		{
			basis *= new Basis(Vector3.Up, -_pendingMouse.X * MouseSensitivity);
			basis *= new Basis(Vector3.Right, -_pendingMouse.Y * MouseSensitivity);
			basis *= new Basis(Vector3.Back, -roll * RollSpeed * dt);
			_pendingMouse = Vector2.Zero;
			state.Transform = new Transform3D(basis.Orthonormalized(), state.Transform.Origin);
		}

		if (!JetpackOn)
			return;

		// Thrust per local axis; dampeners counter drift on any axis without input. Gravity is
		// integrated after this callback, so dampen the velocity it is about to produce: that hovers.
		var input = new Vector3(
			Axis("move_left", "move_right"),
			Axis("move_down", "move_up"),
			Axis("move_forward", "move_back"));
		Vector3 localVelocity = basis.Inverse() * (state.LinearVelocity + state.TotalGravity * dt);
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

	/// <summary>On foot in gravity: body upright along -gravity, mouse yaws the body and pitches the head.</summary>
	private void Walk(PhysicsDirectBodyState3D state)
	{
		float dt = state.Step;
		Vector3 up = -Gravity.Normalized();
		Basis basis = state.Transform.Basis;

		basis = new Basis(basis.Y, -_pendingMouse.X * MouseSensitivity) * basis;
		_headPitch = Mathf.Clamp(_headPitch - _pendingMouse.Y * MouseSensitivity, -1.5f, 1.5f);
		_pendingMouse = Vector2.Zero;
		Camera.Rotation = new Vector3(_headPitch, 0f, 0f);

		// Swing upright gradually, e.g. after switching the jetpack off upside down.
		float tilt = basis.Y.AngleTo(up);
		if (tilt > 1e-4f)
		{
			Vector3 axis = basis.Y.Cross(up);
			axis = axis.LengthSquared() > 1e-8f ? axis.Normalized() : basis.X;
			basis = new Basis(axis, Mathf.Min(tilt, UprightRate * dt)) * basis;
		}
		Vector3 origin = state.Transform.Origin;
		state.Transform = new Transform3D(basis.Orthonormalized(), origin);

		var ray = PhysicsRayQueryParameters3D.Create(origin, origin - up * GroundProbe, exclude: [GetRid()]);
		Grounded = state.GetSpaceState().IntersectRay(ray).Count > 0;

		Vector3 forward = (-basis.Z).Slide(up).Normalized();
		Vector3 right = basis.X.Slide(up).Normalized();
		Vector3 wish = right * Axis("move_left", "move_right") - forward * Axis("move_forward", "move_back");
		float speed = Held("sprint") ? SprintSpeed : WalkSpeed;
		wish = wish.LimitLength(1f) * speed;

		Vector3 vertical = up * state.LinearVelocity.Dot(up);
		Vector3 horizontal = state.LinearVelocity - vertical;
		horizontal = horizontal.MoveToward(wish, (Grounded ? GroundAcceleration : AirAcceleration) * dt);
		if (Grounded && Held("move_up") && vertical.Dot(up) <= 0.1f)
			vertical = up * JumpSpeed;
		state.LinearVelocity = horizontal + vertical;
	}
}
