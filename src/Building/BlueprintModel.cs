using System.Collections.Generic;
using Godot;
using Rebirth.Persistence;

namespace Rebirth.Building;

/// <summary>
/// A blueprint drawn as a static model centred on the node's origin. No physics: used for the
/// player's robot body (scaled to player height) and for fabricator holograms (full size).
/// </summary>
public partial class BlueprintModel : Node3D
{
	private readonly List<Node3D> _flames = new();

	/// <param name="targetHeight">Scale uniformly to this height; null keeps real size.</param>
	/// <param name="material">Draw everything, decorations included, with this material instead.</param>
	public static BlueprintModel Create(Blueprint blueprint, float? targetHeight, Material? material = null)
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
		if (BlockMesher.Build(blocks, _ => 1f, material ?? BlockMesher.Material) is { } mesh)
			scaled.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = CastShadows(material) });

		foreach (var (cell, block) in blocks)
		{
			if (BlockVisuals.CreateDecoration(block.Definition, block.Paint) is not { } decoration)
				continue;
			decoration.Transform = new Transform3D(block.Orientation, BlockGrid.CellCenter(cell));
			if (material is not null)
			{
				foreach (var node in decoration.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false))
				{
					((MeshInstance3D)node).MaterialOverride = material;
					((MeshInstance3D)node).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
				}
			}
			scaled.AddChild(decoration);
			if (decoration.GetNodeOrNull<Node3D>(BlockVisuals.FlameName) is { } flame)
				model._flames.Add(flame);
		}

		Aabb bounds = blueprint.Bounds();
		float scale = targetHeight is { } height ? height / bounds.Size.Y : 1f;
		scaled.Scale = Vector3.One * scale;
		scaled.Position = -bounds.GetCenter() * scale;
		model.AddChild(scaled);
		return model;
	}

	private static GeometryInstance3D.ShadowCastingSetting CastShadows(Material? material) =>
		material is null ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;

	/// <summary>Sets every thruster flame on the model to <paramref name="throttle"/> (0..1).</summary>
	public void SetThrust(float throttle)
	{
		foreach (var flame in _flames)
			flame.Scale = new Vector3(1, 1, Mathf.Max(throttle * 1.5f, 0.001f));
	}
}
