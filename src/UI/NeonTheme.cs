using Godot;

namespace Rebirth.UI;

/// <summary>Dark translucent panels with cyan outlines: the look of every Rebirth screen.</summary>
public static class NeonTheme
{
	public static readonly Color Text = new(0.78f, 0.96f, 1f);
	public static readonly Color Accent = new(0.1f, 0.85f, 1f);
	public static readonly Color Dim = new(0.45f, 0.6f, 0.68f);
	public static readonly Color Warning = new(1f, 0.55f, 0.25f);

	public static Theme Create()
	{
		var theme = new Theme();
		theme.DefaultFontSize = 16;

		theme.SetStylebox("panel", "PanelContainer", Box(new Color(0.01f, 0.03f, 0.05f, 0.82f), Accent * 0.6f));
		theme.SetStylebox("panel", "PopupPanel", Box(new Color(0.01f, 0.03f, 0.05f, 0.95f), Accent));

		theme.SetStylebox("normal", "Button", Box(new Color(0.02f, 0.06f, 0.09f, 0.9f), Accent * 0.35f));
		theme.SetStylebox("hover", "Button", Box(new Color(0.04f, 0.12f, 0.17f, 0.95f), Accent * 0.8f));
		theme.SetStylebox("pressed", "Button", Box(new Color(0.05f, 0.25f, 0.32f, 0.95f), Accent));
		theme.SetStylebox("hover_pressed", "Button", Box(new Color(0.06f, 0.3f, 0.38f, 0.95f), Accent));
		theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
		theme.SetColor("font_color", "Button", Text);
		theme.SetColor("font_hover_color", "Button", Colors.White);
		theme.SetColor("font_pressed_color", "Button", Colors.White);

		theme.SetStylebox("normal", "LineEdit", Box(new Color(0.01f, 0.04f, 0.06f, 0.95f), Accent * 0.5f));
		theme.SetStylebox("focus", "LineEdit", Box(new Color(0.01f, 0.05f, 0.08f, 0.95f), Accent));
		theme.SetColor("font_color", "LineEdit", Colors.White);

		theme.SetStylebox("panel", "ItemList", Box(new Color(0.01f, 0.03f, 0.05f, 0.95f), Accent * 0.4f));
		theme.SetColor("font_color", "ItemList", Text);
		theme.SetColor("font_selected_color", "ItemList", Colors.White);
		theme.SetStylebox("selected", "ItemList", Box(new Color(0.05f, 0.25f, 0.32f, 1f), Accent));
		theme.SetStylebox("selected_focus", "ItemList", Box(new Color(0.05f, 0.25f, 0.32f, 1f), Accent));

		theme.SetColor("font_color", "Label", Text);
		theme.SetColor("font_color", "CheckButton", Text);
		return theme;
	}

	public static StyleBoxFlat Box(Color background, Color border, int borderWidth = 1) => new()
	{
		BgColor = background,
		BorderColor = border,
		BorderWidthLeft = borderWidth,
		BorderWidthTop = borderWidth,
		BorderWidthRight = borderWidth,
		BorderWidthBottom = borderWidth,
		ContentMarginLeft = 10,
		ContentMarginRight = 10,
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
