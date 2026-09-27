using System.Collections.Generic;
using Godot;
using Rebirth.Building;
using Rebirth.Items;

namespace Rebirth.UI;

/// <summary>
/// A square item slot: a little drawn picture of the item (ore lump, ingot bar, wafer, ice crystal)
/// in its own colour, with the amount in the corner. The name shows as a tooltip.
/// </summary>
public partial class ItemSlot : Control
{
	private string _item = "";
	private float _amount;
	private bool _warn;

	public string Item
	{
		get => _item;
		set { _item = value; TooltipText = ItemCatalog.DisplayName(value); QueueRedraw(); }
	}

	public float Amount
	{
		get => _amount;
		set { _amount = value; QueueRedraw(); }
	}

	/// <summary>Draws the amount in red (e.g. a cost you can't pay).</summary>
	public bool Warn
	{
		get => _warn;
		set { _warn = value; QueueRedraw(); }
	}

	public bool Background { get; set; } = true;

	public static ItemSlot Create(string item, float amount, float size = 52f, bool background = true) => new()
	{
		Item = item,
		Amount = amount,
		Background = background,
		CustomMinimumSize = new Vector2(size, size),
		MouseFilter = MouseFilterEnum.Pass,
	};

	public override void _Draw()
	{
		var size = Size;
		float s = Mathf.Min(size.X, size.Y);
		if (Background)
			DrawStyleBox(UiTheme.Box(new Color(1f, 0.97f, 0.9f, 0.92f), UiTheme.PanelEdge, 2, 10), new Rect2(Vector2.Zero, size));
		var center = new Vector2(size.X * 0.5f, size.Y * 0.44f);
		Icons.DrawItem(this, _item, center, s * 0.34f);
		if (_amount > 0f)
		{
			var font = ThemeDB.FallbackFont;
			int fontSize = Mathf.RoundToInt(s * 0.26f);
			string text = Icons.Short(_amount);
			var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X;
			var at = new Vector2(size.X - width - s * 0.08f, size.Y - s * 0.08f);
			DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, fontSize, 4, new Color(1f, 0.97f, 0.9f));
			DrawString(font, at, text, HorizontalAlignment.Left, -1, fontSize, _warn ? UiTheme.Warning : UiTheme.Text);
		}
	}
}

public static class Icons
{
	private static readonly Color Ink = new(0.24f, 0.17f, 0.13f);

	/// <summary>1234 → "1.2k", 56 → "56".</summary>
	public static string Short(float kg) => kg >= 10000f ? $"{kg / 1000f:0}k" : kg >= 1000f ? $"{kg / 1000f:0.0}k" : $"{kg:0}";

	/// <summary>Draws a picture of <paramref name="item"/> centred on <paramref name="c"/>, about 2·r across.</summary>
	public static void DrawItem(CanvasItem canvas, string item, Vector2 c, float r)
	{
		if (item.Length == 0)
			return;
		var color = ItemCatalog.Get(item).Color;
		if (item == "ice")
			Crystal(canvas, c, r, color);
		else if (item == "silicon_wafer")
			Wafer(canvas, c, r, color);
		else if (ItemCatalog.Get(item).Category == ItemCategory.Ingot)
			Ingot(canvas, c, r, color);
		else
			Lump(canvas, c, r, color, item.GetHashCode(), speckled: item != "stone");
	}

	private static void Outline(CanvasItem canvas, Vector2[] points, float width = 2f)
	{
		var loop = new Vector2[points.Length + 1];
		points.CopyTo(loop, 0);
		loop[^1] = points[0];
		canvas.DrawPolyline(loop, Ink, width, antialiased: true);
	}

	/// <summary>A lumpy pebble; ores have darker speckles.</summary>
	private static void Lump(CanvasItem canvas, Vector2 c, float r, Color color, int seed, bool speckled)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(uint)seed };
		var points = new Vector2[11];
		for (int i = 0; i < points.Length; i++)
		{
			float angle = i * Mathf.Tau / points.Length;
			points[i] = c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.82f) * r * rng.RandfRange(0.82f, 1.05f);
		}
		canvas.DrawColoredPolygon(points, color);
		canvas.DrawCircle(c + new Vector2(-r * 0.3f, -r * 0.3f), r * 0.22f, color.Lightened(0.35f));
		if (speckled)
			for (int i = 0; i < 4; i++)
				canvas.DrawCircle(c + new Vector2(rng.RandfRange(-0.5f, 0.5f), rng.RandfRange(-0.3f, 0.5f)) * r, r * 0.11f, color.Darkened(0.4f));
		Outline(canvas, points);
	}

	/// <summary>A trapezoid bar seen slightly from above.</summary>
	private static void Ingot(CanvasItem canvas, Vector2 c, float r, Color color)
	{
		Vector2[] top = [c + new Vector2(-0.62f, -0.42f) * r, c + new Vector2(0.62f, -0.42f) * r, c + new Vector2(0.95f, 0.05f) * r, c + new Vector2(-0.95f, 0.05f) * r];
		Vector2[] front = [c + new Vector2(-0.95f, 0.05f) * r, c + new Vector2(0.95f, 0.05f) * r, c + new Vector2(0.95f, 0.55f) * r, c + new Vector2(-0.95f, 0.55f) * r];
		canvas.DrawColoredPolygon(front, color.Darkened(0.2f));
		canvas.DrawColoredPolygon(top, color.Lightened(0.2f));
		canvas.DrawLine(c + new Vector2(-0.45f, -0.25f) * r, c + new Vector2(0.2f, -0.25f) * r, Colors.White with { A = 0.7f }, 2f, true);
		Outline(canvas, [top[0], top[1], top[2], front[2], front[3], top[3]]);
		canvas.DrawLine(top[3], top[2], Ink, 1.5f, true);
	}

	/// <summary>A round silicon wafer with a little chip grid.</summary>
	private static void Wafer(CanvasItem canvas, Vector2 c, float r, Color color)
	{
		canvas.DrawCircle(c, r * 0.9f, color);
		for (int i = -1; i <= 1; i++)
		{
			canvas.DrawLine(c + new Vector2(i * 0.3f, -0.6f) * r, c + new Vector2(i * 0.3f, 0.6f) * r, color.Lightened(0.35f), 1.5f, true);
			canvas.DrawLine(c + new Vector2(-0.6f, i * 0.3f) * r, c + new Vector2(0.6f, i * 0.3f) * r, color.Lightened(0.35f), 1.5f, true);
		}
		canvas.DrawArc(c, r * 0.9f, 0f, Mathf.Tau, 32, Ink, 2f, true);
	}

	/// <summary>A pale hexagonal crystal.</summary>
	private static void Crystal(CanvasItem canvas, Vector2 c, float r, Color color)
	{
		Vector2[] points = [c + new Vector2(0f, -1f) * r, c + new Vector2(0.6f, -0.45f) * r, c + new Vector2(0.6f, 0.5f) * r,
			c + new Vector2(0f, 0.95f) * r, c + new Vector2(-0.6f, 0.5f) * r, c + new Vector2(-0.6f, -0.45f) * r];
		canvas.DrawColoredPolygon(points, color);
		canvas.DrawColoredPolygon([points[0], points[1], c, points[5]], color.Lightened(0.4f));
		canvas.DrawLine(c, points[3], color.Darkened(0.2f), 1.5f, true);
		Outline(canvas, points);
	}

	/// <summary>The hand drill: a chunky yellow body with a grey bit.</summary>
	public static void DrawDrill(CanvasItem canvas, Vector2 c, float r)
	{
		Vector2[] body = [c + new Vector2(-0.8f, -0.35f) * r, c + new Vector2(0.2f, -0.35f) * r, c + new Vector2(0.2f, 0.25f) * r, c + new Vector2(-0.8f, 0.25f) * r];
		Vector2[] grip = [c + new Vector2(-0.6f, 0.25f) * r, c + new Vector2(-0.25f, 0.25f) * r, c + new Vector2(-0.35f, 0.85f) * r, c + new Vector2(-0.7f, 0.85f) * r];
		Vector2[] bit = [c + new Vector2(0.2f, -0.22f) * r, c + new Vector2(0.95f, -0.05f) * r, c + new Vector2(0.2f, 0.12f) * r];
		canvas.DrawColoredPolygon(grip, Palette.Orange);
		canvas.DrawColoredPolygon(body, Palette.Lemon);
		canvas.DrawColoredPolygon(bit, new Color(0.75f, 0.75f, 0.78f));
		Outline(canvas, grip);
		Outline(canvas, body);
		Outline(canvas, bit);
	}
}

/// <summary>
/// Little 3D portraits of every block type, rendered once each in an off-screen viewport with the same
/// look as in the world, for the hotbar and menus.
/// </summary>
public static class BlockIcons
{
	private const int Resolution = 128;
	private static readonly Dictionary<string, Texture2D> Cache = new();
	private static Node? _host;

	/// <summary>A node the render viewports can live under (they must stay alive for their textures).</summary>
	public static void Attach(Node host)
	{
		_host = host;
		Cache.Clear();
	}

	public static Texture2D? For(BlockDefinition block)
	{
		if (Cache.TryGetValue(block.Id, out var texture))
			return texture;
		if (_host is null || !GodotObject.IsInstanceValid(_host))
			return null;

		var viewport = new SubViewport
		{
			Size = new Vector2I(Resolution, Resolution),
			TransparentBg = true,
			OwnWorld3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
			Msaa3D = Viewport.Msaa.Msaa4X,
		};
		_host.AddChild(viewport);
		viewport.AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.ClearColor,
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color(0.9f, 0.85f, 1f),
				AmbientLightEnergy = 0.7f,
			},
		});
		var sun = new DirectionalLight3D { LightEnergy = 1.3f, LightColor = new Color(1f, 0.93f, 0.8f) };
		viewport.AddChild(sun);
		sun.LookAtFromPosition(new Vector3(2, 4, 3), Vector3.Zero, Vector3.Up);

		var model = new Node3D();
		viewport.AddChild(model);
		if (block.Kind == BlockKind.Tube)
			model.AddChild(BlockVisuals.CreateTube(block.Paint, [Vector3I.Left, Vector3I.Right]));
		else if (BlockMesher.Build(new Dictionary<Vector3I, PlacedBlock> { [Vector3I.Zero] = new(block, Basis.Identity, block.Paint) }, _ => 1f) is { } mesh)
			model.AddChild(new MeshInstance3D { Mesh = mesh });
		if (block.Kind != BlockKind.Tube && BlockVisuals.CreateDecoration(block, block.Paint) is { } decoration)
		{
			// Turn machines so their working face (-Z) shows.
			decoration.Rotation = new Vector3(0, block.Kind is BlockKind.AutoDrill or BlockKind.Thruster or BlockKind.BotCore ? Mathf.Pi : 0f, 0);
			model.AddChild(decoration);
		}
		if (block.Kind is BlockKind.AutoDrill or BlockKind.Thruster or BlockKind.BotCore)
			model.Rotation = new Vector3(0, Mathf.Pi, 0);

		bool tall = block.Kind == BlockKind.BreachLance;
		var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = tall ? 10.5f : 4.6f, Current = true };
		viewport.AddChild(camera);
		Vector3 target = tall ? new Vector3(0, 3.2f, 0) : new Vector3(0, 0.2f, 0);
		camera.LookAtFromPosition(target + new Vector3(4f, 3.2f, 5f), target, Vector3.Up);

		texture = viewport.GetTexture();
		Cache[block.Id] = texture;
		return texture;
	}
}
