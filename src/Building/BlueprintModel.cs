using System.Collections.Generic;
using Godot;
using Rebirth.Persistence;

namespace Rebirth.Building;

/// <summary>
/// A blueprint drawn as a static model, uniformly scaled to a target height and centred on the
/// node's origin. No physics: used for the player's robot body.
/// </summary>
public partial class BlueprintModel : Node3D
{
	private readonly List<Node3D> _flames = new();

	public static BlueprintModel Create(Blueprint blueprint, float targetHeight)
	{
		var model = new BlueprintModel { Name = "Model" };
		var blocks = new Dictionary<Vector3I, PlacedBlock>();
		foreach (var entry in blueprint.Blocks)
		{
			var definition = BlockCatalog.Get(entry.Id);
			blocks[entry.CellVector()] = new PlacedBlock(definition, entry.Orientation(), entry.PaintColor(definition));
		}
		if (blocks.Count == 0)
			return model;

		var scaled = new Node3D();
		if (BlockMesher.Build(blocks, _ => 1f, BlockMesher.SmallModelMaterial) is { } mesh)
			scaled.AddChild(new MeshInstance3D { Mesh = mesh });

		Vector3I min = default, max = default;
		bool first = true;
		foreach (var (cell, block) in blocks)
		{
			min = first ? cell : min.Min(cell);
			max = first ? cell : max.Max(cell);
			first = false;
			if (BlockVisuals.CreateDecoration(block.Definition, block.Paint) is not { } decoration)
				continue;
			decoration.Transform = new Transform3D(block.Orientation, BlockGrid.CellCenter(cell));
			scaled.AddChild(decoration);
			if (decoration.GetNodeOrNull<Node3D>(BlockVisuals.FlameName) is { } flame)
				model._flames.Add(flame);
		}

		float height = (max.Y - min.Y + 1) * BlockGrid.CellSize;
		float scale = targetHeight / height;
		Vector3 center = ((Vector3)(min + max)) * 0.5f * BlockGrid.CellSize;
		scaled.Scale = Vector3.One * scale;
		scaled.Position = -center * scale;
		model.AddChild(scaled);
		return model;
	}

	/// <summary>Sets every thruster flame on the model to <paramref name="throttle"/> (0..1).</summary>
	public void SetThrust(float throttle)
	{
		foreach (var flame in _flames)
			flame.Scale = new Vector3(1, 1, Mathf.Max(throttle * 1.5f, 0.001f));
	}
}
