using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

namespace WeavePort.CapacityTests;

internal sealed record TenantRequests(int Tenant, string Language, double[] LatenciesMs, Dictionary<string, int> StatusCounts, RequestObservation[]? Observations = null);
