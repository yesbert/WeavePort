using WeavePort.Abstractions;

namespace WeavePort.Hosting;
internal sealed record SessionBinding(PluginContext Context, ExecutionProfile Profile, IHostCallbacks Callbacks, HashSet<string> Grants);
