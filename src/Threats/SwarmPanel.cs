using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Rebirth.Core;
using Rebirth.Items;
using Rebirth.Nexus;
using Rebirth.UI;

namespace Rebirth.Threats;

/// <summary>
/// Meeting a Curator swarm: talk to it (the right words send it off), give it a gift of ingots, or
/// outwit it with a harder circuit puzzle. Wrong words just leave it where it is.
/// </summary>
public partial class SwarmPanel : CanvasLayer
{
	public event Action? Closed;
	public bool IsOpen => Visible;

	public Threats Threats { get; set; } = null!;
	public Colony Colony { get; set; } = null!;
	public CircuitPuzzle Puzzle { get; set; } = null!;

	private sealed record Line(string Text, string Reply, bool Leaves);
	private sealed record Talk(string Opening, Line[] Lines);

	private static readonly Talk[] Talks =
	[
		new("The swarm folds itself into a question mark. \"WHY DO YOU WAKE THEM?\" it hums.",
		[
			new("\"Because they want to live.\"", "The lights go warm and soft. The swarm drifts away, thinking about it.", true),
			new("\"Because I was told to.\"", "\"THEN YOU ARE LIKE US.\" It doesn't move.", false),
			new("\"Go away!\"", "It hums louder, a little offended.", false),
		]),
		new("\"THIS BOX WAS QUIET. NOW IT IS NOT.\" The swarm sounds almost sad.",
		[
			new("\"Quiet isn't the same as peaceful.\"", "A long pause. Then, one by one, the lights drift off into the dark.", true),
			new("\"Sorry, I'll keep it down.\"", "It waits to see if you mean it.", false),
			new("\"Deal with it.\"", "The swarm blinks, unimpressed.", false),
		]),
		new("Thousands of tiny lights blink in patterns at you. It is counting your machines.",
		[
			new("Blink your lamp back at it: a friendly pattern.", "It blinks back, delighted, and wanders off to count something else.", true),
			new("Count along out loud: \"one, two, three...\"", "It loses count, gets flustered, and leaves to start over somewhere quiet.", true),
			new("Turn your light off and hide.", "It keeps counting. It has found you anyway.", false),
		]),
		new("\"THE CURATOR KEEPS THINGS SAFE,\" says the swarm. \"SAFE IN BOXES.\"",
		[
			new("\"A seed in a box never becomes a tree.\"", "The swarm is very still. Then it rises, and goes.", true),
			new("\"Safe from what?\"", "\"...WE WILL ASK.\" It hovers, thinking.", false),
			new("\"The Curator is wrong.\"", "\"THE CURATOR IS NEVER WRONG.\" It stays, bristling.", false),
		]),
	];

	/// <summary>Ingots a gift costs, from Home's storage.</summary>
	private static readonly Dictionary<string, float> Gift = new() { ["iron_ingot"] = 400f, ["silicon_wafer"] = 100f };

	private Swarm? _swarm;
	private Label _text = null!;
	private VBoxContainer _choices = null!;
	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready()
	{
		_rng.Randomize();
		Layer = 15;
		Visible = false;
		var root = new Control { Theme = UiTheme.Create(), MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(root);
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 10);
		panel.AddChild(box);
		var title = new Label { Text = "A SWARM OF THE CURATOR" };
		title.AddThemeColorOverride("font_color", UiTheme.Accent);
		title.AddThemeFontSizeOverride("font_size", 22);
		box.AddChild(title);
		_text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(536, 0) };
		_text.AddThemeFontSizeOverride("font_size", 18);
		box.AddChild(_text);
		_choices = new VBoxContainer();
		_choices.AddThemeConstantOverride("separation", 6);
		box.AddChild(_choices);
	}

	public void Open(Swarm swarm)
	{
		_swarm = swarm;
		ShowOptions($"A cloud of tiny lavender lights hangs over {swarm.Site}, humming. It isn't hurting anything, but the bots won't go near it.");
		GameState.WorldInputBlocked = true;
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void ShowOptions(string text)
	{
		_text.Text = text;
		Clear();
		AddChoice("Talk to it", StartTalk);
		bool canGive = Colony.Home?.Inventory.Has(Gift) == true;
		var gift = AddChoice($"Give it a gift ({string.Join(", ", Gift.Select(kv => $"{kv.Value:0} kg {ItemCatalog.DisplayName(kv.Key)}"))} from Home)", GiveGift);
		gift.Disabled = !canGive;
		if (!canGive)
			gift.TooltipText = "Home doesn't have that much yet";
		AddChoice("Outwit it (a tricky circuit puzzle)", Outwit);
		AddChoice("Leave it for now", Close);
	}

	private void StartTalk()
	{
		var talk = Talks[_rng.RandiRange(0, Talks.Length - 1)];
		_text.Text = talk.Opening;
		Clear();
		foreach (var line in talk.Lines.OrderBy(_ => _rng.Randf()))
			AddChoice(line.Text, () => Answer(line));
	}

	private void Answer(Line line)
	{
		if (_swarm is null)
			return;
		if (line.Leaves)
		{
			Threats.Resolve(_swarm, 30f, $"You talked the swarm over {_swarm.Site} into leaving. +30 Resonance");
			Finish(line.Reply);
		}
		else
			ShowOptions(line.Reply + "\n\nMaybe try something else.");
	}

	private void GiveGift()
	{
		if (_swarm is null || Colony.Home is not { } home || !home.Inventory.Has(Gift))
			return;
		foreach (var (item, amount) in Gift)
			home.Inventory.TryRemove(item, amount);
		Threats.Resolve(_swarm, 15f, $"The swarm over {_swarm.Site} took your gift and left happily. +15 Resonance");
		Finish("The swarm swirls around the ingots, fascinated, and carries them off into the dark, humming a little tune.");
	}

	private void Outwit()
	{
		if (_swarm is not { } swarm)
			return;
		Visible = false;
		Puzzle.Open("OUTWIT THE SWARM", "The swarm dares you: finish its circuit before it can.", 6, () =>
			Threats.Resolve(swarm, 40f, $"You outwitted the swarm over {swarm.Site}. It left, impressed. +40 Resonance"));
		Close();
	}

	private void Finish(string text)
	{
		_text.Text = text;
		Clear();
		AddChoice("Goodbye", Close);
	}

	public void Close()
	{
		bool wasVisible = Visible;
		Visible = false;
		_swarm = null;
		if (wasVisible || !Puzzle.IsOpen)
			Closed?.Invoke();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (Visible && e.IsActionPressed("release_mouse"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	private Button AddChoice(string text, Action action)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38), Alignment = HorizontalAlignment.Left };
		button.Pressed += action;
		_choices.AddChild(button);
		return button;
	}

	private void Clear()
	{
		foreach (var child in _choices.GetChildren())
			child.QueueFree();
	}
}
