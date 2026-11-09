using Axitor.Trading.Gateway.Benchmarks.Streams;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.NativeAot;

namespace Axitor.Trading.Gateway.Benchmarks;

/// <summary>
/// Runs the benchmarks picked on the command line, e.g. --filter *Klines*, or one stream run: streams client stream. Start the exchange stub first.
/// Add --aot to run the picked benchmarks compiled with Native AOT instead of the JIT, e.g. --filter *.Rest.*Axitor* --aot.
/// </summary>
internal static class Program
{
    private const string StreamsMode = "streams";
    private const string AotOption = "--aot";

    private static async Task<int> Main(string[] args)
    {
        if (0 < args.Length && args[0] == StreamsMode)
        {
            return await StreamHarness.RunAsync(args[1..]).ConfigureAwait(false);
        }

        // The default config already exports GitHub markdown.
        IConfig config = DefaultConfig.Instance
            .AddDiagnoser(MemoryDiagnoser.Default)
            .WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend));

        if (Array.IndexOf(args, AotOption) >= 0)
        {
            config = config.AddJob(CreateNativeAotJob());
            args = Array.FindAll(args, static arg => arg != AotOption);
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return 0;
    }

    /// <summary>A Native AOT job on the ILCompiler of the installed SDK.</summary>
    private static Job CreateNativeAotJob()
    {
        // No ILCompiler version: the generated project takes the SDK's own, matching the installed runtime.
        // The generated project skips Directory.Build.props and does not treat warnings as errors, so the trim warnings of other libraries pass.
        IToolchain toolchain = NativeAotToolchain.CreateBuilder()
            .UseNuGet()
            .TargetFrameworkMoniker("net10.0")
            .DisplayName("NativeAOT 10.0")
            .ToToolchain();

        return Job.Default
            .WithRuntime(NativeAotRuntime.Net10_0)
            .WithToolchain(toolchain)
            .WithId("NativeAOT");
    }
}
