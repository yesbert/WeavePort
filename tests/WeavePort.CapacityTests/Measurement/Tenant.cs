using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

namespace WeavePort.CapacityTests;

internal sealed record Tenant(int Index, string Language, IPluginSession Session, double StartupMs);
