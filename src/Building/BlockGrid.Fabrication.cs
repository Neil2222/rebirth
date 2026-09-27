using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Persistence;

namespace Rebirth.Building;

/// <summary>One queued print at a fabricator.</summary>
public sealed class FabricatorJob
{
	public required Blueprint Design { get; init; }
	/// <summary>0..1 once the ingots have been taken.</summary>
	public float Progress { get; set; }
	public bool Paid { get; set; }
}

// Fabricators print blueprints from ingots delivered into their input by the logistics network. The
// ingots are taken when a job starts; printing then takes time and power, while a hologram of the design fills in from the
// bottom in front of the fabricator's -Z face. The finished grid appears once that space is clear.
public partial class BlockGrid
{
	/// <summary>Seconds of printing per tonne of design (with full power).</summary>
	public const float PrintSecondsPerTonne = 2f;
	public const float MinPrintSeconds = 5f;

	private readonly Dictionary<Vector3I, List<FabricatorJob>> _fabricatorQueues = new();
	private readonly Dictionary<Vector3I, string> _fabricatorStatus = new();
	private readonly Dictionary<Vector3I, (BlueprintModel Model, ShaderMaterial Material)> _holograms = new();
	private float _fabricatorDraw;   // MW requested last tick by fabricators that were printing

	public IReadOnlyList<FabricatorJob> FabricatorQueue(Vector3I cell) =>
		_fabricatorQueues.TryGetValue(cell, out var queue) ? queue : [];

	public string FabricatorStatus(Vector3I cell) => _fabricatorStatus.GetValueOrDefault(cell, "Idle");

	public void EnqueuePrint(Vector3I cell, Blueprint design) =>
		QueueFor(cell).Add(new FabricatorJob { Design = design });

	/// <summary>Restores a queue from a save.</summary>
	public void RestoreFabricator(Vector3I cell, IReadOnlyCollection<FabricatorJob> jobs)
	{
		if (jobs.Count > 0)
			QueueFor(cell).AddRange(jobs);
	}

	/// <summary>Drops the last queued job. Returns false when only the running job (or nothing) is left.</summary>
	public bool CancelLastPrint(Vector3I cell)
	{
		var queue = QueueFor(cell);
		if (queue.Count == 0 || (queue.Count == 1 && queue[0].Paid))
			return false;
		queue.RemoveAt(queue.Count - 1);
		return true;
	}

	public IEnumerable<(Vector3I Cell, IReadOnlyList<FabricatorJob> Queue)> FabricatorQueues =>
		_fabricatorQueues.Where(kv => kv.Value.Count > 0).Select(kv => (kv.Key, (IReadOnlyList<FabricatorJob>)kv.Value));

	private List<FabricatorJob> QueueFor(Vector3I cell)
	{
		if (!_fabricatorQueues.TryGetValue(cell, out var queue))
			_fabricatorQueues[cell] = queue = new List<FabricatorJob>();
		return queue;
	}

	/// <summary>Forget a fabricator's work when its block goes (removed, destroyed, or moved to another grid).</summary>
	private void DropFabricator(Vector3I cell)
	{
		_fabricatorQueues.Remove(cell);
		_fabricatorStatus.Remove(cell);
		RemoveHologram(cell);
	}

	/// <summary>Grid-local placement of a design printed by the fabricator at <paramref name="cell"/>.</summary>
	private Transform3D PrintSpot(Vector3I cell, Blueprint design)
	{
		Aabb bounds = design.Bounds();
		Vector3 front = _blocks[cell].Orientation * Vector3.Forward;
		float reach = CellSize * 0.5f + bounds.Size.Length() * 0.5f + 1.5f;
		Vector3 center = CellCenter(cell) + front * reach;
		// The design's own origin sits off its bounding-box center.
		return new Transform3D(Basis.Identity, center - bounds.GetCenter());
	}

	private void UpdateFabrication(float dt)
	{
		float draw = 0f;
		foreach (var (cell, queue) in _fabricatorQueues)
		{
			if (_quarantined.Contains(cell))
			{
				_fabricatorStatus[cell] = QuarantineStatus;
				continue;
			}
			if (queue.Count == 0)
			{
				_fabricatorStatus[cell] = "Idle";
				RemoveHologram(cell);
				continue;
			}

			var job = queue[0];
			if (!job.Paid)
			{
				// Ingots arrive by parcel from storage and refineries on the same network.
				var cost = job.Design.TotalCost();
				var input = _state[cell].Input!;
				// Parcels are only sent for shortfalls of half a kilo or more, so accept that much missing.
				if (!input.Has(cost, slack: 0.5f))
				{
					_fabricatorStatus[cell] = "Waiting for materials";
					continue;
				}
				foreach (var (item, amount) in cost)
				{
					float used = Mathf.Min(amount, input.Get(item));
					input.TryRemove(item, used);
					Items.ProductionStats.Consumed(item, used);
				}
				job.Paid = true;
			}

			if (job.Progress < 1f)
			{
				float seconds = Mathf.Max(MinPrintSeconds, job.Design.TotalMass() / 1000f * PrintSecondsPerTonne);
				job.Progress = Mathf.Min(1f, job.Progress + dt / seconds * PowerSatisfaction);
				draw += _blocks[cell].Definition.PowerDraw;
				_fabricatorStatus[cell] = PowerSatisfaction < 0.999f ? $"Printing {job.Progress:P0} (low power)" : $"Printing {job.Progress:P0}";
				ShowHologram(cell, job);
				continue;
			}

			if (!TryDeliver(cell, job.Design))
			{
				_fabricatorStatus[cell] = "Output blocked - clear the space in front";
				ShowHologram(cell, job);
				continue;
			}
			queue.RemoveAt(0);
			RemoveHologram(cell);
		}
		_fabricatorDraw = draw;
	}

	private bool TryDeliver(Vector3I cell, Blueprint design)
	{
		Transform3D spot = GlobalTransform * PrintSpot(cell, design);
		Aabb bounds = design.Bounds();
		var probe = new PhysicsShapeQueryParameters3D
		{
			Shape = new BoxShape3D { Size = bounds.Size * 0.95f },
			Transform = spot * new Transform3D(Basis.Identity, bounds.GetCenter()),
			Exclude = [GetRid()],
		};
		if (GetWorld3D().DirectSpaceState.IntersectShape(probe, 1).Count > 0)
			return false;

		var printed = Create(GetParent(), spot, isStatic: design.Kind == DesignKind.Station);
		design.BuildInto(printed, charge: 0.25f);
		printed.LinearVelocity = LinearVelocity;
		return true;
	}

	private void ShowHologram(Vector3I cell, FabricatorJob job)
	{
		if (!_holograms.TryGetValue(cell, out var hologram))
		{
			var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hologram.gdshader") };
			material.SetShaderParameter("tint", _blocks[cell].Paint);
			var model = BlueprintModel.Create(job.Design, targetHeight: null, material);
			Transform3D spot = PrintSpot(cell, job.Design);
			model.Transform = new Transform3D(spot.Basis, spot * job.Design.Bounds().GetCenter());
			AddChild(model);
			_holograms[cell] = hologram = (model, material);
		}

		float height = job.Design.Bounds().Size.Y;
		Transform3D world = hologram.Model.GlobalTransform;
		hologram.Material.SetShaderParameter("progress", job.Progress);
		hologram.Material.SetShaderParameter("base_point", world.Origin - world.Basis.Y * (height * 0.5f));
		hologram.Material.SetShaderParameter("up_dir", world.Basis.Y.Normalized());
		hologram.Material.SetShaderParameter("height", height);
	}

	private void RemoveHologram(Vector3I cell)
	{
		if (_holograms.Remove(cell, out var hologram))
			hologram.Model.QueueFree();
	}
}
