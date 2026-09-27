using Godot;

namespace Rebirth.UI;

/// <summary>
/// Warm retro-futuristic look for every Rebirth screen: cream panels with rounded corners,
/// chocolate-brown text and orange accents, like a friendly 70s control panel.
/// </summary>
public static class UiTheme
{
	public static readonly Color Panel = new(0.97f, 0.93f, 0.85f, 0.96f);
	public static readonly Color PanelEdge = new(0.80f, 0.66f, 0.50f);
	public static readonly Color Text = new(0.24f, 0.17f, 0.13f);
	public static readonly Color Accent = new(0.93f, 0.45f, 0.16f);
	public static readonly Color Dim = new(0.50f, 0.42f, 0.36f);
	public static readonly Color Warning = new(0.85f, 0.25f, 0.20f);
	/// <summary>Light text for labels drawn straight over the 3D world (the HUD).</summary>
	public static readonly Color HudText = new(1f, 0.97f, 0.90f);
	public static readonly Color HudOutline = new(0.18f, 0.12f, 0.20f, 0.85f);

	private static readonly Color ButtonFace = new(1f, 0.97f, 0.91f);
	private static readonly Color ButtonHover = new(1f, 0.90f, 0.74f);
	private static readonly Color ButtonPressed = new(0.98f, 0.72f, 0.45f);

	public static Theme Create()
	{
		var theme = new Theme();
		theme.DefaultFontSize = 16;

		theme.SetStylebox("panel", "PanelContainer", Box(Panel, PanelEdge, 2, 14));
		theme.SetStylebox("panel", "PopupPanel", Box(Panel, PanelEdge, 2, 14));

		theme.SetStylebox("normal", "Button", Box(ButtonFace, PanelEdge, 2, 10));
		theme.SetStylebox("hover", "Button", Box(ButtonHover, Accent, 2, 10));
		theme.SetStylebox("pressed", "Button", Box(ButtonPressed, Accent, 2, 10));
		theme.SetStylebox("hover_pressed", "Button", Box(ButtonPressed, Accent, 2, 10));
		theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
		foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
			theme.SetColor(state, "Button", Text);

		theme.SetStylebox("normal", "LineEdit", Box(Colors.White, PanelEdge, 2, 8));
		theme.SetStylebox("focus", "LineEdit", Box(Colors.White, Accent, 2, 8));
		theme.SetColor("font_color", "LineEdit", Text);
		theme.SetColor("caret_color", "LineEdit", Accent);

		theme.SetStylebox("panel", "ItemList", Box(new Color(1f, 0.98f, 0.94f), PanelEdge, 2, 10));
		// Every text state set: Godot's defaults are white, unreadable on the cream rows.
		foreach (string state in new[] { "font_color", "font_selected_color", "font_hovered_color", "font_hovered_selected_color" })
			theme.SetColor(state, "ItemList", Text);
		theme.SetStylebox("selected", "ItemList", Box(ButtonPressed, Accent, 0, 8));
		theme.SetStylebox("selected_focus", "ItemList", Box(ButtonPressed, Accent, 0, 8));
		theme.SetStylebox("hovered", "ItemList", Box(ButtonHover, ButtonHover, 0, 8));
		theme.SetStylebox("hovered_selected", "ItemList", Box(ButtonPressed, Accent, 0, 8));
		theme.SetStylebox("hovered_selected_focus", "ItemList", Box(ButtonPressed, Accent, 0, 8));

		theme.SetStylebox("normal", "OptionButton", Box(ButtonFace, PanelEdge, 2, 10));
		theme.SetStylebox("hover", "OptionButton", Box(ButtonHover, Accent, 2, 10));
		theme.SetStylebox("pressed", "OptionButton", Box(ButtonPressed, Accent, 2, 10));
		foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
			theme.SetColor(state, "OptionButton", Text);

		theme.SetColor("font_color", "Label", Text);
		foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
			theme.SetColor(state, "CheckButton", Text);
		return theme;
	}

	public static StyleBoxFlat Box(Color background, Color border, int borderWidth = 2, int radius = 10) => new()
	{
		BgColor = background,
		BorderColor = border,
		BorderWidthLeft = borderWidth,
		BorderWidthTop = borderWidth,
		BorderWidthRight = borderWidth,
		BorderWidthBottom = borderWidth,
		CornerRadiusTopLeft = radius,
		CornerRadiusTopRight = radius,
		CornerRadiusBottomLeft = radius,
		CornerRadiusBottomRight = radius,
		ContentMarginLeft = 12,
		ContentMarginRight = 12,
		ContentMarginTop = 6,
		ContentMarginBottom = 6,
	};

	public static Label Heading(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeColorOverride("font_color", Accent);
		label.AddThemeFontSizeOverride("font_size", 13);
		return label;
	}
}
