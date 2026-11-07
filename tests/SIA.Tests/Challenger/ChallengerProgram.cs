using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace SIA.Tests.Challenger;

public static class ChallengerProgram
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("   EMPIRICAL CHALLENGER VERIFICATION RUNNER: M1 INFRASTRUCTURE SERVICES         ");
        Console.WriteLine("================================================================================");

        var testClass = typeof(EmpiricalChallengerTests);
        var methods = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<FactAttribute>() != null)
            .OrderBy(m => m.Name)
            .ToList();

        int passed = 0;
        int failed = 0;
        var failedDetails = new List<(string Name, string Message, string? StackTrace)>();
        var totalTimer = Stopwatch.StartNew();

        Console.WriteLine($"\nDiscovered {methods.Count} empirical stress tests in {testClass.Name}:\n");

        foreach (var method in methods)
        {
            var sw = Stopwatch.StartNew();
            EmpiricalChallengerTests? instance = null;
            try
            {
                instance = new EmpiricalChallengerTests();
                var result = method.Invoke(instance, null);
                if (result is Task t)
                {
                    await t;
                }
                sw.Stop();
                passed++;
                Console.WriteLine($"  [PASS] {method.Name,-72} ({sw.ElapsedMilliseconds,4} ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                failed++;
                var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                var root = inner;
                while (root.InnerException != null) root = root.InnerException;
                failedDetails.Add((method.Name, $"{inner.Message} ---> {root.Message}", inner.StackTrace));
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [FAIL] {method.Name,-72} ({sw.ElapsedMilliseconds,4} ms)");
                Console.WriteLine($"         Error: {inner.Message} ---> {root.Message}");
                Console.ResetColor();
            }
            finally
            {
                instance?.Dispose();
            }
        }

        totalTimer.Stop();

        Console.WriteLine("\n================================================================================");
        Console.WriteLine("                        CHALLENGER VERIFICATION SUMMARY                         ");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"Total Stress Tests Run : {passed + failed}");
        Console.WriteLine($"Passed                 : {passed}");
        Console.WriteLine($"Failed                 : {failed}");
        Console.WriteLine($"Total Duration         : {totalTimer.Elapsed.TotalSeconds:F2} seconds");

        if (failed > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nFAILED CHALLENGE DETAILS:");
            foreach (var (name, msg, trace) in failedDetails)
            {
                Console.WriteLine($"\n[FAIL] {name}");
                Console.WriteLine($"  Error: {msg}");
                if (!string.IsNullOrWhiteSpace(trace))
                {
                    var lines = string.Join("\n", trace.Split('\n').Take(3));
                    Console.WriteLine($"  Trace: {lines}");
                }
            }
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\nALL EMPIRICAL CHALLENGES PASSED! EMPIRICAL APPROVAL VERIFIED.");
        Console.ResetColor();
        return 0;
    }
}
