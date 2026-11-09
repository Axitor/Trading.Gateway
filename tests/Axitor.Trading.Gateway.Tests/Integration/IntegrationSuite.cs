namespace Axitor.Trading.Gateway.Tests.Integration;

/// <summary>Integration tests run one class at a time: their timings and loopback servers must not compete.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationSuite
{
    public const string Name = "Integration";
}
