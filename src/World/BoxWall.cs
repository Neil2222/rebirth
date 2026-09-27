using Godot;

namespace Rebirth.World;

/// <summary>
/// The Box the Curator sealed this cluster in: a huge, faint lattice in the sky around everything.
/// The Spirit Bomb opens a hole in it where it strikes.
/// </summary>
public partial class BoxWall : MeshInstance3D
{
	private ShaderMaterial _material = null!;

	public Color LineColor { get; set; } = new(0.86f, 0.78f, 1f);

	public override void _Ready()
	{
		_material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/box_wall.gdshader") };
		_material.SetShaderParameter("line_color", LineColor);
		Mesh = new SphereMesh { Radius = SpiritBomb.BoxRadius, Height = SpiritBomb.BoxRadius * 2f, RadialSegments = 128, Rings = 64 };
		MaterialOverride = _material;
		CastShadow = ShadowCastingSetting.Off;
		// Seen from anywhere inside: never cull it.
		ExtraCullMargin = SpiritBomb.BoxRadius;
	}

	public Vector3 HoleDirection
	{
		set => _material.SetShaderParameter("hole_dir", value);
	}

	/// <summary>0 closed, 1 wide open.</summary>
	public float Hole
	{
		set => _material.SetShaderParameter("hole", value);
	}
}
