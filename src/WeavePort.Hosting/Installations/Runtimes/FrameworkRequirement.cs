using System.Text.Json;

namespace WeavePort.Hosting;
internal sealed record FrameworkRequirement(string Name, string Version, string RollForward);
