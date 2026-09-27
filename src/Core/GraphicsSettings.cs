using System.Text.Json;
using Godot;

namespace Rebirth.Core;

public enum GraphicsPreset { Low, Medium, High, Custom }

/// <summary>
/// How much the game asks of the graphics card: render scale, shadows, contact shadows (SSAO), glow,
/// anti-aliasing, frame cap and window mode. Stored in user://graphics.json, shared by all save slots.
/// </summary>
public sealed class GraphicsSettings
{
	private const string PathOnDisk = "user://graphics.json";

	public GraphicsPreset Preset { get; set; } = GraphicsPreset.High;
	/// <summary>Share of the screen resolution the 3D world is drawn at (upscaled with FSR below 1).</summary>
	public float RenderScale { get; set; } = 1f;
	public bool Shadows { get; set; } = true;
	public bool Ssao { get; set; } = true;
	public bool Glow { get; set; } = true;
	/// <summary>0 off, 1 = 2×, 2 = 4× MSAA.</summary>
	public int AntiAliasing { get; set; } = 2;
	public bool VSync { get; set; } = true;
	/// <summary>0 = no cap.</summary>
	public int FpsCap { get; set; }
	public bool Fullscreen { get; set; }

	public static GraphicsSettings Current { get; private set; } = Load();

	public void UsePreset(GraphicsPreset preset)
	{
		Preset = preset;
		(RenderScale, Shadows, Ssao, Glow, AntiAliasing) = preset switch
		{
			GraphicsPreset.Low => (0.67f, false, false, false, 0),
			GraphicsPreset.Medium => (0.85f, true, false, true, 1),
			_ => (1f, true, true, true, 2),
		};
	}

	/// <summary>Applies everything to the window and to the world's environment and sun (if given).</summary>
	public void Apply(Viewport viewport, Environment? environment, DirectionalLight3D? sun)
	{
		viewport.Scaling3DScale = RenderScale;
		viewport.Scaling3DMode = RenderScale < 0.99f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
		viewport.Msaa3D = AntiAliasing switch { 0 => Viewport.Msaa.Disabled, 1 => Viewport.Msaa.Msaa2X, _ => Viewport.Msaa.Msaa4X };
		DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = FpsCap;
		var mode = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
		if (DisplayServer.WindowGetMode() != mode && !(mode == DisplayServer.WindowMode.Windowed && DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Maximized))
			DisplayServer.WindowSetMode(mode);
		if (environment is not null)
		{
			environment.SsaoEnabled = Ssao;
			environment.GlowEnabled = Glow;
		}
		if (sun is not null)
			sun.ShadowEnabled = Shadows;
	}

	public void Save()
	{
		using var file = FileAccess.Open(PathOnDisk, FileAccess.ModeFlags.Write);
		file?.StoreString(JsonSerializer.Serialize(this));
	}

	private static GraphicsSettings Load()
	{
		if (!FileAccess.FileExists(PathOnDisk))
			return new GraphicsSettings();
		try
		{
			return JsonSerializer.Deserialize<GraphicsSettings>(FileAccess.GetFileAsString(PathOnDisk)) ?? new GraphicsSettings();
		}
		catch (JsonException e)
		{
			GD.PushWarning($"Ignoring unreadable graphics settings: {e.Message}");
			return new GraphicsSettings();
		}
	}
}
