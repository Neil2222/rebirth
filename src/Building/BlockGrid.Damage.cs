using System.Collections.Generic;
using Godot;

namespace Driftworks.Building;

// Block integrity, collision damage, and splitting into separate grids when connectivity breaks.
public partial class BlockGrid
{
	/// <summary>Collision impulse (N·s) per contact that a grid shrugs off without damage.</summary>
	public const float ImpactThreshold = 15_000f;
	/// <summary>Integrity lost per N·s of impulse above the threshold.</summary>
	public const float DamagePerImpulse = 0.004f;

	private static readonly Color DamagedColor = new(0.12f, 0.09f, 0.07f);
	private static readonly Vector3I[] Neighbours =
		[Vector3I.Right, Vector3I.Left, Vector3I.Up, Vector3I.Down, Vector3I.Back, Vector3I.Forward];

	private readonly Dictionary<Vector3I, float> _integrity = new();
	private readonly List<(Vector3I Cell, float Amount)> _pendingDamage = new();

	public float Integrity(Vector3I cell) => _integrity[cell];

	/// <summary>Damage is applied on the next physics tick, so it is safe to call from physics callbacks.</summary>
	public void QueueDamage(Vector3I cell, float amount) => _pendingDamage.Add((cell, amount));

	/// <summary>Existing cell whose center is nearest to a grid-local point (searches the 3×3×3 neighbourhood).</summary>
	public bool TryFindCell(Vector3 local, out Vector3I cell)
	{
		Vector3I guess = LocalToCell(local);
		float best = float.MaxValue;
		cell = default;
		for (int x = -1; x <= 1; x++)
		for (int y = -1; y <= 1; y++)
		for (int z = -1; z <= 1; z++)
		{
			var candidate = guess + new Vector3I(x, y, z);
			float d = CellCenter(candidate).DistanceSquaredTo(local);
			if (d < best && _blocks.ContainsKey(candidate))
			{
				best = d;
				cell = candidate;
			}
		}
		return best < float.MaxValue;
	}

	/// <summary>
	/// Turns this tick's hard contacts into damage, on this grid and on any station it hit. The solver
	/// spreads one impact over several contact points, so the threshold applies to their sum and the
	/// resulting damage is shared out in proportion to each contact's impulse.
	/// </summary>
	private void CollectImpacts(PhysicsDirectBodyState3D state)
	{
		int count = state.GetContactCount();
		float total = 0f;
		for (int i = 0; i < count; i++)
			total += state.GetContactImpulse(i).Length();
		if (total <= ImpactThreshold)
			return;

		float damagePerImpulse = (total - ImpactThreshold) * DamagePerImpulse / total;
		Transform3D toLocal = state.Transform.AffineInverse();
		for (int i = 0; i < count; i++)
		{
			float damage = state.GetContactImpulse(i).Length() * damagePerImpulse;
			if (TryFindCell(toLocal * state.GetContactLocalPosition(i), out var cell))
				QueueDamage(cell, damage);
			// Frozen grids don't simulate, so they never see the contact themselves.
			if (state.GetContactColliderObject(i) is BlockGrid { IsStatic: true } other
				&& other.TryFindCell(other.ToLocal(state.GetContactColliderPosition(i)), out var otherCell))
				other.QueueDamage(otherCell, damage);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_pendingDamage.Count == 0)
			return;

		var destroyed = new List<Vector3I>();
		foreach (var (cell, amount) in _pendingDamage)
		{
			if (!_integrity.TryGetValue(cell, out float integrity) || integrity <= 0f)
				continue;
			_integrity[cell] = integrity - amount;
			if (integrity - amount <= 0f)
				destroyed.Add(cell);
		}
		_pendingDamage.Clear();

		foreach (var cell in destroyed)
		{
			Color color = _blocks[cell].Definition.Color;
			RemoveInternal(cell);
			BlockVisuals.SpawnDebris(GetParent(), GlobalTransform * CellCenter(cell), color);
		}

		if (_blocks.Count == 0)
		{
			QueueFree();
			return;
		}
		if (destroyed.Count > 0)
		{
			SplitDisconnected();
			OnBlocksChanged();
		}
		else
		{
			RebuildMesh(); // integrity changed the colours
		}
	}

	/// <summary>
	/// Keeps the heaviest connected component in this grid and moves every other component into a
	/// new dynamic grid that inherits this grid's motion at its own center of mass.
	/// </summary>
	private void SplitDisconnected()
	{
		var components = ConnectedComponents();
		if (components.Count <= 1)
			return;

		int keep = 0;
		float keepMass = 0f;
		for (int i = 0; i < components.Count; i++)
		{
			float mass = 0f;
			foreach (var cell in components[i])
				mass += _blocks[cell].Definition.Mass;
			if (mass > keepMass)
			{
				keepMass = mass;
				keep = i;
			}
		}

		Vector3 oldCenterOfMass = GlobalTransform * CenterOfMass;
		for (int i = 0; i < components.Count; i++)
		{
			if (i == keep)
				continue;

			var piece = Create(GetParent(), GlobalTransform, isStatic: false);
			foreach (var cell in components[i])
			{
				var block = _blocks[cell];
				float integrity = _integrity[cell];
				RemoveInternal(cell);
				piece.AddInternal(cell, block, integrity);
			}
			piece.OnBlocksChanged();
			piece.LinearVelocity = LinearVelocity + AngularVelocity.Cross(piece.GlobalTransform * piece.CenterOfMass - oldCenterOfMass);
			piece.AngularVelocity = AngularVelocity;
		}
	}

	private List<List<Vector3I>> ConnectedComponents()
	{
		var components = new List<List<Vector3I>>();
		var visited = new HashSet<Vector3I>();
		var stack = new Stack<Vector3I>();
		foreach (var start in _blocks.Keys)
		{
			if (!visited.Add(start))
				continue;
			var component = new List<Vector3I>();
			stack.Push(start);
			while (stack.Count > 0)
			{
				var cell = stack.Pop();
				component.Add(cell);
				foreach (var dir in Neighbours)
				{
					var next = cell + dir;
					if (_blocks.ContainsKey(next) && visited.Add(next))
						stack.Push(next);
				}
			}
			components.Add(component);
		}
		return components;
	}
}
