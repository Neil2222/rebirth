using System.Collections.Generic;
using System.Linq;

namespace Rebirth.Items;

/// <summary>
/// How much of every item the world makes and uses, over time: running totals sampled every few
/// seconds so the Nexus can show rates over the last minute, ten minutes or hour, with graphs.
/// Covers the world you are in; it starts fresh when a world loads.
/// </summary>
public static class ProductionStats
{
	public const float SampleSeconds = 5f;
	private const int MaxSamples = 3600 / 5 + 2;   // an hour

	private sealed record Sample(double Time, Dictionary<string, float> Made, Dictionary<string, float> Used);

	private static readonly Dictionary<string, float> Made = new();
	private static readonly Dictionary<string, float> Used = new();
	private static readonly List<Sample> History = new();
	private static double _clock;
	private static float _timer;

	/// <summary>Seconds of history recorded so far.</summary>
	public static double Elapsed => History.Count == 0 ? 0 : _clock - History[0].Time;

	public static void Produced(string item, float kg)
	{
		if (kg > 0f)
			Made[item] = Made.GetValueOrDefault(item) + kg;
	}

	public static void Consumed(string item, float kg)
	{
		if (kg > 0f)
			Used[item] = Used.GetValueOrDefault(item) + kg;
	}

	public static void Reset()
	{
		Made.Clear();
		Used.Clear();
		History.Clear();
		_clock = 0;
		_timer = 0f;
		History.Add(new Sample(0, new(), new()));
	}

	public static void Tick(float dt)
	{
		_clock += dt;
		_timer += dt;
		if (_timer < SampleSeconds)
			return;
		_timer = 0f;
		History.Add(new Sample(_clock, new(Made), new(Used)));
		if (History.Count > MaxSamples)
			History.RemoveAt(0);
	}

	public static IEnumerable<string> Items => Made.Keys.Union(Used.Keys);

	/// <summary>Kilograms per minute made and used over the last <paramref name="window"/> seconds.</summary>
	public static (float Made, float Used) PerMinute(string item, float window)
	{
		if (History.Count == 0)
			return (0f, 0f);
		var start = History.FirstOrDefault(s => s.Time >= _clock - window) ?? History[^1];
		double minutes = (_clock - start.Time) / 60.0;
		if (minutes < 1.0 / 60.0)
			return (0f, 0f);
		return ((float)((Made.GetValueOrDefault(item) - start.Made.GetValueOrDefault(item)) / minutes),
			(float)((Used.GetValueOrDefault(item) - start.Used.GetValueOrDefault(item)) / minutes));
	}

	/// <summary>
	/// Rate per minute in <paramref name="buckets"/> equal slices of the window, oldest first, for graphs.
	/// </summary>
	public static float[] Series(string item, bool made, float window, int buckets)
	{
		var result = new float[buckets];
		if (History.Count < 2)
			return result;
		double from = _clock - window, slice = window / buckets;
		for (int b = 0; b < buckets; b++)
		{
			double t0 = from + b * slice, t1 = t0 + slice;
			var a = History.LastOrDefault(s => s.Time <= t0) ?? History[0];
			var z = History.LastOrDefault(s => s.Time <= t1) ?? History[0];
			if (z.Time <= a.Time)
				continue;
			var sa = made ? a.Made : a.Used;
			var sz = made ? z.Made : z.Used;
			result[b] = (float)((sz.GetValueOrDefault(item) - sa.GetValueOrDefault(item)) / ((z.Time - a.Time) / 60.0));
		}
		return result;
	}
}
