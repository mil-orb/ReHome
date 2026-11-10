using System.Diagnostics;

namespace Rehome;

/// <summary>Set REHOME_TRACE=1 to see where a scan spends its time. Goes to stderr, never into the JSON.</summary>
internal static class Timing
{
    static readonly bool On = Environment.GetEnvironmentVariable("REHOME_TRACE") == "1";

    public static T Time<T>(string label, Func<T> work)
    {
        if (!On)
            return work();

        var clock = Stopwatch.StartNew();
        var result = work();
        Console.Error.WriteLine($"{clock.Elapsed.TotalMilliseconds,8:0} ms  {label}");
        return result;
    }
}
