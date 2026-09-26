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

	private MeshInstance3D _ghost = null!;
	private StandardMaterial3D _ghostMaterial = null!;
	private readonly BoxShape3D _probe = new() { Size = Vector3.One * BlockGrid.CellSize * 0.9f };

	// Recomputed every physics tick.
	private BlockGrid? _placeGrid;
	private Vector3I _placeCell;
	private Transform3D _placeTransform;
	private bool _placeValid;
	private BlockGrid? _aimedGrid;
	private Vector3I _aimedCell;

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
		for (int slot = 0; slot <= 9; slot++)
		{
			if (!e.IsActionPressed($"slot_{slot}"))
				continue;
			Selected = slot >= 1 && slot <= BlockCatalog.Toolbar.Count ? BlockCatalog.Toolbar[slot - 1] : null;
			return;
		}

		if (Selected is null || Input.MouseMode != Input.MouseModeEnum.Captured)
			return;
		if (e.IsActionPressed("build_place"))
			Place();
		else if (e.IsActionPressed("build_remove"))
			_aimedGrid?.Remove(_aimedCell);
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

	private void Place()
	{
		if (!_placeValid || Selected is null)
			return;
		var grid = _placeGrid ?? BlockGrid.Create(GridParent, _placeTransform, isStatic: true);
		grid.TryAdd(_placeGrid is null ? Vector3I.Zero : _placeCell, Selected);
	}

	private void UpdateTarget()
	{
		var space = GetWorld3D().DirectSpaceState;
		Vector3 from = Camera.GlobalPosition;
		Vector3 forward = -Camera.GlobalBasis.Z;
		var ray = PhysicsRayQueryParameters3D.Create(from, from + forward * Reach, exclude: [Body.GetRid()]);
		var hit = space.IntersectRay(ray);

		_aimedGrid = null;
		_placeGrid = null;
		if (hit.Count > 0 && hit["collider"].AsGodotObject() is BlockGrid grid)
		{
			Vector3I normal = DominantAxis(grid.GlobalBasis.Inverse() * hit["normal"].AsVector3());
			Vector3 local = grid.ToLocal(hit["position"].AsVector3());
			_aimedGrid = grid;
			_aimedCell = BlockGrid.LocalToCell(local - (Vector3)normal * (BlockGrid.CellSize * 0.5f));
			_placeGrid = grid;
			_placeCell = _aimedCell + normal;
			_placeTransform = grid.GlobalTransform * new Transform3D(Basis.Identity, BlockGrid.CellCenter(_placeCell));
		}
		else
		{
			_placeTransform = new Transform3D(Camera.GlobalBasis.Orthonormalized(), from + forward * FreePlacementDistance);
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

	private static Vector3I DominantAxis(Vector3 v)
	{
		Vector3 a = v.Abs();
		if (a.X >= a.Y && a.X >= a.Z)
			return new Vector3I(Mathf.Sign(v.X), 0, 0);
		if (a.Y >= a.Z)
			return new Vector3I(0, Mathf.Sign(v.Y), 0);
		return new Vector3I(0, 0, Mathf.Sign(v.Z));
	}
}
