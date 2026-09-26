using Rebirth.Core;
using Rebirth.Items;
using Godot;
using Godot.Collections;

namespace Rebirth.Building;

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

	/// <summary>Inventory that pays for placed blocks and receives refunds; null means creative (free) building.</summary>
	public Inventory? CostSource { get; set; }

	public BlockDefinition? Selected { get; private set; }

	/// <summary>Grid block under the crosshair, if any. Updated every physics tick, also without a selected block.</summary>
	public BlockGrid? AimedGrid { get; private set; }
	public Vector3I AimedCell { get; private set; }

	private Node3D? _ghost;
	private readonly StandardMaterial3D _ghostMaterial = BlockVisuals.CreateGhostMaterial();
	private readonly BoxShape3D _probe = new() { Size = Vector3.One * BlockGrid.CellSize * 0.9f };

	// Block rotation relative to the target grid (or to the camera when starting a new grid).
	private Basis _orientation = Basis.Identity;

	// Recomputed every physics tick.
	private BlockGrid? _placeGrid;
	private Vector3I _placeCell;
	private Transform3D _placeTransform;
	private bool _placeValid;

	public override void _UnhandledInput(InputEvent e)
	{
		if (GameState.WorldInputBlocked)
			return;
		if (Selected is null || Input.MouseMode != Input.MouseModeEnum.Captured)
			return;
		if (e.IsActionPressed("primary_action"))
			Place();
		else if (e.IsActionPressed("secondary_action"))
			RemoveAimed();
		else if (e.IsActionPressed("rotate_block_yaw"))
			Rotate(Camera.GlobalBasis.Y);
		else if (e.IsActionPressed("rotate_block_pitch"))
			Rotate(Camera.GlobalBasis.X);
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdateTarget();
		if (_ghost is null)
			return;
		bool wasVisible = _ghost.Visible;
		_ghost.Visible = true;
		_ghost.GlobalTransform = _placeTransform;
		_ghostMaterial.AlbedoColor = _placeValid && CanAfford(Selected!) ? BlockVisuals.GhostValid : BlockVisuals.GhostInvalid;
		if (!wasVisible)
			_ghost.ResetPhysicsInterpolation();
	}

	public void Select(BlockDefinition? block)
	{
		Selected = block;
		_ghost?.QueueFree();
		_ghost = null;
		if (block is null)
			return;
		_ghost = BlockVisuals.CreateGhost(block, block.Paint, _ghostMaterial);
		_ghost.TopLevel = true;
		_ghost.Visible = false;
		AddChild(_ghost);
	}

	public bool CanAfford(BlockDefinition block) => CostSource is null || CostSource.Has(block.Cost);

	/// <summary>Removes the aimed block, refunding its cost in survival (whatever fits in the inventory).</summary>
	private void RemoveAimed()
	{
		if (AimedGrid is not { } grid || !grid.TryGet(AimedCell, out var block))
			return;
		grid.Remove(AimedCell);
		if (CostSource is not null)
			foreach (var (item, amount) in block.Definition.Cost)
				CostSource.Add(item, amount);
	}

	/// <summary>Rotates the block 90° around the grid axis closest to <paramref name="worldAxis"/>.</summary>
	private void Rotate(Vector3 worldAxis) =>
		_orientation = BlockGrid.RotateOrientation(_orientation, _placeGrid?.GlobalBasis ?? Camera.GlobalBasis, worldAxis);

	private void Place()
	{
		if (!_placeValid || Selected is null || !CanAfford(Selected))
			return;
		if (CostSource is not null)
			foreach (var (item, amount) in Selected.Cost)
				CostSource.TryRemove(item, amount);
		if (_placeGrid is null)
			BlockGrid.Create(GridParent, _placeTransform * new Transform3D(_orientation.Inverse(), Vector3.Zero), isStatic: true)
				.TryAdd(Vector3I.Zero, Selected, _orientation);
		else
			_placeGrid.TryAdd(_placeCell, Selected, _orientation);
	}

	private void UpdateTarget()
	{
		var space = GetWorld3D().DirectSpaceState;
		// Aim through the crosshair, but measure reach from the head so third person can't build further.
		Vector3 head = Body.GlobalTransform * Characters.Player.HeadOffset;
		Vector3 from = Camera.GlobalPosition;
		Vector3 forward = -Camera.GlobalBasis.Z;
		var ray = PhysicsRayQueryParameters3D.Create(from, from + forward * (Reach + from.DistanceTo(head)), exclude: [Body.GetRid()]);
		var hit = space.IntersectRay(ray);
		if (hit.Count > 0 && hit["position"].AsVector3().DistanceTo(head) > Reach)
			hit.Clear();

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
			_placeTransform = new Transform3D(Camera.GlobalBasis.Orthonormalized() * _orientation, head + forward * FreePlacementDistance);
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
