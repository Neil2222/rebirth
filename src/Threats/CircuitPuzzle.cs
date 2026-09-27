using System;
using System.Collections.Generic;
using Godot;
using Rebirth.Core;
using Rebirth.UI;

namespace Rebirth.Threats;

/// <summary>
/// The purge puzzle: a board of circuit tiles, scrambled. Click a tile to turn it; once current flows
/// from the plug on the left to the socket on the right, the virus is out. There is always a way.
/// </summary>
public partial class CircuitPuzzle : CanvasLayer
{
	public event Action? Closed;
	public bool IsOpen => Visible;

	private Label _title = null!;
	private Label _text = null!;
	private Board _board = null!;
	private Button _leave = null!;
	private Action? _solved;
	private bool _done;

	public override void _Ready()
	{
		Layer = 16;
		Visible = false;
		var root = new Control { Theme = UiTheme.Create() };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(root);
		var dim = new ColorRect { Color = new Color(0.12f, 0.06f, 0.16f, 0.55f), MouseFilter = Control.MouseFilterEnum.Stop };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.AddChild(dim);
		var panel = new PanelContainer();
		root.AddChild(panel);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize);
		panel.GrowHorizontal = Control.GrowDirection.Both;
		panel.GrowVertical = Control.GrowDirection.Both;
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 10);
		panel.AddChild(box);
		_title = new Label();
		_title.AddThemeColorOverride("font_color", UiTheme.Accent);
		_title.AddThemeFontSizeOverride("font_size", 22);
		box.AddChild(_title);
		_text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(480, 0) };
		box.AddChild(_text);
		_board = new Board { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
		_board.Solved += OnSolved;
		box.AddChild(_board);
		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		buttons.AddThemeConstantOverride("separation", 8);
		var hint = new Button { Text = "Hint" };
		hint.Pressed += () => _board.Hint();
		buttons.AddChild(hint);
		_leave = new Button { Text = "Leave it for now" };
		_leave.Pressed += Close;
		buttons.AddChild(_leave);
		box.AddChild(buttons);
	}

	/// <param name="size">Tiles across (height is one less).</param>
	public void Open(string title, string text, int size, Action solved)
	{
		_title.Text = title;
		_text.Text = text + "\nClick a tile to turn it (right-click turns it back). Connect the plug on the left to the socket on the right.";
		_solved = solved;
		_done = false;
		_leave.Text = "Leave it for now";
		_board.NewPuzzle(size, size - 1, (int)GD.Randi());
		GameState.WorldInputBlocked = true;
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void OnSolved()
	{
		if (_done)
			return;
		_done = true;
		_text.Text = "Current flows. The virus hops out, waves, and is gone.";
		_leave.Text = "Done";
		_solved?.Invoke();
		GetTree().CreateTimer(1.2).Timeout += () =>
		{
			if (_done)
				Close();
		};
	}

	public void Close()
	{
		if (!Visible)
			return;
		Visible = false;
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

	/// <summary>The tile board itself: generation, turning, current flow, drawing.</summary>
	private partial class Board : Control
	{
		private const int North = 1, East = 2, South = 4, West = 8;
		private const float Tile = 74f;
		private const float Margin = 44f;

		public event Action? Solved;

		private int _w, _h, _source, _sink;
		private int[,] _tiles = new int[1, 1];
		/// <summary>The path's tiles as they should be turned, for hints.</summary>
		private readonly List<(int X, int Y, int Mask)> _solution = new();
		private bool[,] _powered = new bool[1, 1];

		public void NewPuzzle(int w, int h, int seed)
		{
			_w = w;
			_h = h;
			var rng = new RandomNumberGenerator { Seed = (ulong)(uint)seed };
			_tiles = new int[w, h];
			_powered = new bool[w, h];
			_source = rng.RandiRange(0, h - 1);
			_sink = rng.RandiRange(0, h - 1);

			// A winding path from the plug to the socket: a random depth-first walk with backtracking.
			var path = Walk(rng);
			_solution.Clear();
			for (int i = 0; i < path.Count; i++)
			{
				var (x, y) = path[i];
				int mask = 0;
				mask |= i == 0 ? West : Toward(path[i], path[i - 1]);
				mask |= i == path.Count - 1 ? East : Toward(path[i], path[i + 1]);
				_tiles[x, y] = mask;
				_solution.Add((x, y, mask));
			}
			// Everything else: decoy pieces.
			int[] shapes = [North | South, North | East, North | East | South, North | East, North | South];
			for (int x = 0; x < w; x++)
				for (int y = 0; y < h; y++)
					if (_tiles[x, y] == 0)
						_tiles[x, y] = shapes[rng.RandiRange(0, shapes.Length - 1)];
			// Scramble, making sure it doesn't start solved.
			do
			{
				for (int x = 0; x < w; x++)
					for (int y = 0; y < h; y++)
						for (int r = rng.RandiRange(0, 3); r > 0; r--)
							_tiles[x, y] = Turn(_tiles[x, y]);
			}
			while (Flow());
			CustomMinimumSize = new Vector2(w * Tile + Margin * 2f, h * Tile + 20f);
			QueueRedraw();
		}

		private List<(int X, int Y)> Walk(RandomNumberGenerator rng)
		{
			var path = new List<(int, int)> { (0, _source) };
			var seen = new HashSet<(int, int)> { (0, _source) };
			int budget = 20000;
			bool Search((int X, int Y) at)
			{
				if (--budget < 0)
					return false;
				if (at.X == _w - 1 && at.Y == _sink && path.Count >= _w + 1)
					return true;
				var next = new List<(int, int)> { (at.X + 1, at.Y), (at.X - 1, at.Y), (at.X, at.Y + 1), (at.X, at.Y - 1) };
				for (int i = next.Count - 1; i > 0; i--)
				{
					int j = rng.RandiRange(0, i);
					(next[i], next[j]) = (next[j], next[i]);
				}
				foreach (var n in next)
				{
					if (n.Item1 < 0 || n.Item2 < 0 || n.Item1 >= _w || n.Item2 >= _h || !seen.Add(n))
						continue;
					path.Add(n);
					if (Search(n))
						return true;
					path.RemoveAt(path.Count - 1);
					seen.Remove(n);
				}
				return false;
			}
			if (!Search((0, _source)))
			{
				// Fallback (never expected): a straight run along the source row, then to the sink row.
				path.Clear();
				for (int x = 0; x < _w; x++)
					path.Add((x, _source));
				int step = Math.Sign(_sink - _source);
				for (int y = _source + step; step != 0 && y != _sink + step; y += step)
					path.Add((_w - 1, y));
			}
			return path;
		}

		/// <summary>Turns the first path tile that is still wrong into place.</summary>
		public void Hint()
		{
			foreach (var (x, y, mask) in _solution)
			{
				if (_tiles[x, y] == mask)
					continue;
				_tiles[x, y] = mask;
				break;
			}
			bool solved = Flow();
			QueueRedraw();
			if (solved)
				Solved?.Invoke();
		}

		private static int Toward((int X, int Y) from, (int X, int Y) to) =>
			to.X > from.X ? East : to.X < from.X ? West : to.Y > from.Y ? South : North;

		private static int Turn(int mask) => ((mask << 1) | (mask >> 3)) & 15;

		private static int TurnBack(int mask) => ((mask >> 1) | (mask << 3)) & 15;

		/// <summary>Floods current from the plug; true when it reaches the socket.</summary>
		private bool Flow()
		{
			Array.Clear(_powered);
			if ((_tiles[0, _source] & West) == 0)
				return false;
			var queue = new Queue<(int X, int Y)>();
			queue.Enqueue((0, _source));
			_powered[0, _source] = true;
			while (queue.Count > 0)
			{
				var (x, y) = queue.Dequeue();
				int m = _tiles[x, y];
				foreach (var (dir, dx, dy, back) in new[] { (North, 0, -1, South), (East, 1, 0, West), (South, 0, 1, North), (West, -1, 0, East) })
				{
					int nx = x + dx, ny = y + dy;
					if ((m & dir) == 0 || nx < 0 || ny < 0 || nx >= _w || ny >= _h || _powered[nx, ny] || (_tiles[nx, ny] & back) == 0)
						continue;
					_powered[nx, ny] = true;
					queue.Enqueue((nx, ny));
				}
			}
			return _powered[_w - 1, _sink] && (_tiles[_w - 1, _sink] & East) != 0;
		}

		public override void _GuiInput(InputEvent e)
		{
			if (e is not InputEventMouseButton { Pressed: true } click || click.ButtonIndex is not (MouseButton.Left or MouseButton.Right))
				return;
			int x = Mathf.FloorToInt((click.Position.X - Margin) / Tile), y = Mathf.FloorToInt(click.Position.Y / Tile);
			if (x < 0 || y < 0 || x >= _w || y >= _h)
				return;
			_tiles[x, y] = click.ButtonIndex == MouseButton.Left ? Turn(_tiles[x, y]) : TurnBack(_tiles[x, y]);
			bool solved = Flow();
			QueueRedraw();
			AcceptEvent();
			if (solved)
				Solved?.Invoke();
		}

		public override void _Draw()
		{
			Flow();
			var dark = new Color(0.36f, 0.3f, 0.4f);
			var live = new Color(1f, 0.62f, 0.25f);
			for (int x = 0; x < _w; x++)
				for (int y = 0; y < _h; y++)
				{
					var rect = new Rect2(Margin + x * Tile + 3f, y * Tile + 3f, Tile - 6f, Tile - 6f);
					DrawStyleBox(UiTheme.Box(_powered[x, y] ? new Color(1f, 0.93f, 0.8f) : new Color(0.93f, 0.9f, 0.86f), UiTheme.PanelEdge, 2, 10), rect);
					var c = rect.GetCenter();
					var color = _powered[x, y] ? live : dark;
					int m = _tiles[x, y];
					float r = Tile * 0.5f - 3f;
					if ((m & North) != 0) DrawLine(c, c + new Vector2(0, -r), color, 10f, true);
					if ((m & East) != 0) DrawLine(c, c + new Vector2(r, 0), color, 10f, true);
					if ((m & South) != 0) DrawLine(c, c + new Vector2(0, r), color, 10f, true);
					if ((m & West) != 0) DrawLine(c, c + new Vector2(-r, 0), color, 10f, true);
					DrawCircle(c, 8f, color);
				}
			// Plug and socket.
			var plug = new Vector2(Margin * 0.45f, _source * Tile + Tile * 0.5f);
			DrawCircle(plug, 14f, live);
			DrawLine(plug, plug + new Vector2(Margin * 0.55f, 0), live, 10f, true);
			var socket = new Vector2(Margin + _w * Tile + Margin * 0.55f, _sink * Tile + Tile * 0.5f);
			bool on = _powered[_w - 1, _sink] && (_tiles[_w - 1, _sink] & East) != 0;
			DrawCircle(socket, 14f, on ? live : dark);
			DrawCircle(socket, 7f, new Color(0.98f, 0.95f, 0.9f));
		}
	}
}
