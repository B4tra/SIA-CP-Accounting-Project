using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace SIA.Tests.E2E;

public static class E2EProgram
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("    SIA E2E TEST SUITE: GENERAL JOURNAL & FINANCIAL REPORTS (TIERS 1 - 4)       ");
        Console.WriteLine("================================================================================");

        var testClasses = new[]
        {
            typeof(Tier1_FeatureCoverageTests),
            typeof(Tier2_BoundaryAndCornerCasesTests),
            typeof(Tier3_CrossFeatureCombinationsTests),
            typeof(Tier4_RealWorldScenariosTests)
        };

        int totalPassed = 0;
        int totalFailed = 0;
        var failedTests = new List<(string Name, string Error, string? StackTrace)>();
        var totalSw = Stopwatch.StartNew();

        foreach (var testClass in testClasses)
        {
            Console.WriteLine($"\n>>> Running Test Class: {testClass.Name} <<<");

            var methods = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<FactAttribute>() != null)
                .OrderBy(m => m.Name)
                .ToList();

            foreach (var method in methods)
            {
                var testFullName = $"{testClass.Name}.{method.Name}";
                var instance = Activator.CreateInstance(testClass);
                var sw = Stopwatch.StartNew();

                try
                {
                    var result = method.Invoke(instance, null);
                    if (result is Task task)
                    {
                        await task;
                    }

                    sw.Stop();
                    totalPassed++;
                    Console.WriteLine($"  [PASS] {method.Name,-70} ({sw.ElapsedMilliseconds,4} ms)");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    totalFailed++;
                    var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                    failedTests.Add((testFullName, inner.Message, inner.StackTrace));
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [FAIL] {method.Name,-70} ({sw.ElapsedMilliseconds,4} ms)");
                    Console.WriteLine($"         Error: {inner.Message}");
                    Console.ResetColor();
                }
                finally
                {
                    if (instance is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }

        totalSw.Stop();

        Console.WriteLine("\n================================================================================");
        Console.WriteLine("                             E2E TEST RUN SUMMARY                               ");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"Total Tests Executed : {totalPassed + totalFailed}");
        Console.WriteLine($"Passed               : {totalPassed}");
        Console.WriteLine($"Failed               : {totalFailed}");
        Console.WriteLine($"Execution Duration   : {totalSw.Elapsed.TotalSeconds:F2} seconds");

        if (totalFailed > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nFAILED TEST DETAILS:");
            foreach (var (name, error, trace) in failedTests)
            {
                Console.WriteLine($"\n[FAIL] {name}");
                Console.WriteLine($"  Message: {error}");
                if (!string.IsNullOrWhiteSpace(trace))
                {
                    var firstFewLines = string.Join("\n", trace.Split('\n').Take(3));
                    Console.WriteLine($"  Trace: {firstFewLines}");
                }
            }
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\nALL E2E & SCENARIO TESTS PASSED SUCCESSFULLY! (100% PASS RATE)");
        Console.ResetColor();
        return 0;
    }
}
