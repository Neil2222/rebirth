using System;
using Godot;

namespace Rebirth.UI;

/// <summary>
/// Info cards that appear the moment the mouse is over something and stay put beside it (not following
/// the mouse) for as long as the mouse stays. One card at a time, drawn above every screen.
/// </summary>
public static class HoverCard
{
	private const float Gap = 8f;

	private static CanvasLayer? _layer;
	private static Control? _card;
	private static Control? _anchor;

	/// <summary>Shows a card next to <paramref name="anchor"/> when the mouse enters it, hides it when it leaves.</summary>
	public static void Attach(Control anchor, Func<Control?> build)
	{
		anchor.MouseEntered += () => Show(anchor, build());
		anchor.MouseExited += () => Hide(anchor);
		anchor.TreeExiting += () => Hide(anchor);
		anchor.VisibilityChanged += () =>
		{
			if (!anchor.IsVisibleInTree())
				Hide(anchor);
		};
	}

	private static void Show(Control anchor, Control? card)
	{
		Hide(_anchor);
		if (card is null || !anchor.IsInsideTree())
			return;
		if (_layer is null || !GodotObject.IsInstanceValid(_layer))
		{
			_layer = new CanvasLayer { Layer = 100 };
			((SceneTree)Engine.GetMainLoop()).Root.AddChild(_layer);
		}
		card.MouseFilter = Control.MouseFilterEnum.Ignore;
		foreach (var child in card.FindChildren("*", nameof(Control), true, false))
			((Control)child).MouseFilter = Control.MouseFilterEnum.Ignore;
		card.Theme = UiTheme.Create();
		_layer.AddChild(card);
		_card = card;
		_anchor = anchor;

		// Beside the anchor: to the right if it fits, else to the left; kept on screen.
		var rect = anchor.GetGlobalRect();
		var size = card.GetCombinedMinimumSize();
		var screen = anchor.GetViewportRect().Size;
		float x = rect.End.X + Gap;
		if (x + size.X > screen.X)
			x = rect.Position.X - Gap - size.X;
		float y = Mathf.Clamp(rect.Position.Y, 0f, Mathf.Max(0f, screen.Y - size.Y));
		card.Position = new Vector2(Mathf.Max(0f, x), y);
	}

	private static void Hide(Control? anchor)
	{
		if (anchor is null || anchor != _anchor)
			return;
		if (_card is not null && GodotObject.IsInstanceValid(_card))
			_card.QueueFree();
		_card = null;
		_anchor = null;
	}
}
