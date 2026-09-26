using Godot;
using Godot.Collections;

namespace Driftworks.Building;

/// <summary>
/// Block placement/removal from the player's view. Aiming at a grid snaps the ghost to the
/// face under the crosshair; aiming at nothing places a new station grid in front of the player.
/// </summary>
public partial class BuildTool : Node3D
{
	[Export] public float Reach = 12f;
	[Export] public float FreePlacementDistance = 6f;

	public Camera3D Camera { get; set; } = null!;
	public CollisionObject3D Body { get; set; } = null!;
	public Node GridParent { get; set; } = null!;

	public BlockDefinition? Selected { get; private set; }

	/// <summary>Grid block under the crosshair, if any. Updated every physics tick, also without a selected block.</summary>
	public BlockGrid? AimedGrid { get; private set; }
	public Vector3I AimedCell { get; private set; }

	private MeshInstance3D _ghost = null!;
	private Node3D? _ghostDecoration;
	private StandardMaterial3D _ghostMaterial = null!;
	private readonly BoxShape3D _probe = new() { Size = Vector3.One * BlockGrid.CellSize * 0.9f };

	// Block rotation relative to the target grid (or to the camera when starting a new grid).
	private Basis _orientation = Basis.Identity;

	// Recomputed every physics tick.
	private BlockGrid? _placeGrid;
	private Vector3I _placeCell;
	private Transform3D _placeTransform;
	private bool _placeValid;

	public override void _Ready()
	{
		_ghostMaterial = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		_ghost = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = Vector3.One * BlockGrid.CellSize * 1.002f, Material = _ghostMaterial },
			TopLevel = true,
			Visible = false,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_ghost);
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (Selected is null || Input.MouseMode != Input.MouseModeEnum.Captured)
			return;
		if (e.IsActionPressed("primary_action"))
			Place();
		else if (e.IsActionPressed("secondary_action"))
			AimedGrid?.Remove(AimedCell);
		else if (e.IsActionPressed("rotate_block_yaw"))
			Rotate(Camera.GlobalBasis.Y);
		else if (e.IsActionPressed("rotate_block_pitch"))
			Rotate(Camera.GlobalBasis.X);
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdateTarget();
		bool wasVisible = _ghost.Visible;
		_ghost.Visible = Selected is not null;
		if (!_ghost.Visible)
			return;

		_ghost.GlobalTransform = _placeTransform;
		_ghostMaterial.AlbedoColor = _placeValid ? new Color(0.3f, 1f, 0.4f, 0.35f) : new Color(1f, 0.25f, 0.2f, 0.35f);
		if (!wasVisible)
			_ghost.ResetPhysicsInterpolation();
	}

	public void Select(BlockDefinition? block)
	{
		Selected = block;
		_ghostDecoration?.QueueFree();
		_ghostDecoration = block is null ? null : BlockVisuals.CreateDecoration(block);
		if (_ghostDecoration is null)
			return;
		_ghost.AddChild(_ghostDecoration);
		foreach (var node in _ghostDecoration.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false))
			((MeshInstance3D)node).MaterialOverride = _ghostMaterial;
		// Show a short static flame so the exhaust side of a thruster is obvious while placing.
		if (_ghostDecoration.GetNodeOrNull<Node3D>(BlockVisuals.FlameName) is { } flame)
			flame.Scale = new Vector3(1, 1, 0.8f);
	}

	/// <summary>Rotates the block 90° around the grid axis closest to <paramref name="worldAxis"/>.</summary>
	private void Rotate(Vector3 worldAxis)
	{
		Basis gridBasis = _placeGrid?.GlobalBasis ?? Camera.GlobalBasis;
		Vector3 axis = BlockGrid.DominantAxis(gridBasis.Inverse() * worldAxis);
		_orientation = Snap(new Basis(axis, Mathf.Pi / 2f) * _orientation);
	}

	private static Basis Snap(Basis b) => new(
		new Vector3(Mathf.Round(b.X.X), Mathf.Round(b.X.Y), Mathf.Round(b.X.Z)),
		new Vector3(Mathf.Round(b.Y.X), Mathf.Round(b.Y.Y), Mathf.Round(b.Y.Z)),
		new Vector3(Mathf.Round(b.Z.X), Mathf.Round(b.Z.Y), Mathf.Round(b.Z.Z)));

	private void Place()
	{
		if (!_placeValid || Selected is null)
			return;
		if (_placeGrid is null)
			BlockGrid.Create(GridParent, _placeTransform * new Transform3D(_orientation.Inverse(), Vector3.Zero), isStatic: true)
				.TryAdd(Vector3I.Zero, Selected, _orientation);
		else
			_placeGrid.TryAdd(_placeCell, Selected, _orientation);
	}

	private void UpdateTarget()
	{
		var space = GetWorld3D().DirectSpaceState;
		Vector3 from = Camera.GlobalPosition;
		Vector3 forward = -Camera.GlobalBasis.Z;
		var ray = PhysicsRayQueryParameters3D.Create(from, from + forward * Reach, exclude: [Body.GetRid()]);
		var hit = space.IntersectRay(ray);

		AimedGrid = null;
		_placeGrid = null;
		if (hit.Count > 0 && hit["collider"].AsGodotObject() is BlockGrid grid)
		{
			Vector3I normal = BlockGrid.DominantAxis(grid.GlobalBasis.Inverse() * hit["normal"].AsVector3());
			Vector3 local = grid.ToLocal(hit["position"].AsVector3());
			AimedGrid = grid;
			AimedCell = BlockGrid.LocalToCell(local - (Vector3)normal * (BlockGrid.CellSize * 0.5f));
			_placeGrid = grid;
			_placeCell = AimedCell + normal;
			_placeTransform = grid.GlobalTransform * new Transform3D(_orientation, BlockGrid.CellCenter(_placeCell));
		}
		else
		{
			_placeTransform = new Transform3D(Camera.GlobalBasis.Orthonormalized() * _orientation, from + forward * FreePlacementDistance);
		}

		_placeValid = (_placeGrid is null || !_placeGrid.Has(_placeCell)) && !Overlaps(space, _placeTransform, _placeGrid);
	}

	private bool Overlaps(PhysicsDirectSpaceState3D space, Transform3D transform, BlockGrid? ignore)
	{
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = _probe,
			Transform = transform,
			Exclude = ignore is null ? new Array<Rid>() : [ignore.GetRid()],
		};
		return space.IntersectShape(query, 1).Count > 0;
	}
}
