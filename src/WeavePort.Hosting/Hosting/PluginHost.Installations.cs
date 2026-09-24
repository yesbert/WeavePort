using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Sdk.Client;

namespace WeavePort.Hosting;
public sealed partial class PluginHost
{
    /// <summary>Creates a ready-to-use client from a verified installation, one operator approval and a tenant binding.</summary>
    public async Task<IBoundPluginClient> BindAsync(InstalledPlugin installation, PluginApproval approval, TenantBinding binding, IHostCallbacks callbacks, IEnumerable<string> grants, CancellationToken cancellationToken = default)
    {
        ProcessProfile profile = ApprovedProfile(installation, approval);
        if (approval.Ownership == WorkerReusePolicy.Shared)
        {
            throw new NotSupportedException("Use ShareAsync for shared installations.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(binding.Tenant);
        var context = new PluginContext(binding.Tenant, installation.Identity.Plugin, installation.Identity.Version, installation.Identity.Digest, binding.Configuration);
        IPluginSession session = await BindAsync(context, profile, callbacks, grants, cancellationToken: cancellationToken);
        return new LocalPluginClient(session, streamOptions: approval.Streams);
    }

    /// <summary>Starts a verified shared installation with instance-wide configuration and tenant-bound client views.</summary>
    public Task<ISharedPlugin> ShareAsync(InstalledPlugin installation, PluginApproval approval, JsonElement configuration, IHostCallbacks callbacks, IEnumerable<string> grants, CancellationToken cancellationToken = default)
    {
        ProcessProfile profile = ApprovedProfile(installation, approval);
        if (approval.Ownership != WorkerReusePolicy.Shared)
        {
            throw new NotSupportedException("ShareAsync requires Shared operator approval.");
        }

        var context = new PluginContext("", installation.Identity.Plugin, installation.Identity.Version, installation.Identity.Digest, configuration);
        var options = new SharedWorkerOptions
        {
            Degree = approval.Degree,
            Workers = approval.Workers,
            MaximumRestarts = approval.MaximumRestarts,
            RestartWindow = approval.RestartWindow,
            MaximumAbandonedCalls = approval.MaximumAbandonedCalls,
            CancellationGrace = approval.CancellationGrace,
            SilenceTimeout = approval.SilenceTimeout
        };
        return ShareAsync(context, profile, options, callbacks, grants, cancellationToken);
    }

    private static ProcessProfile ApprovedProfile(InstalledPlugin installation, PluginApproval approval)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(approval);
        PluginLaunchDeclaration launch = installation.Launch ?? throw new InvalidDataException("Installation has no launch declaration.");
        if (!approval.TrustedCode || !launch.Ownership.Contains(approval.Ownership) || launch.MemoryMiB > approval.MaximumMemoryMiB || approval.Degree < 1 || approval.Degree > launch.MaximumDegree || approval.Workers < 1 || approval.MaximumCallbacks < 1 || approval.Ownership != WorkerReusePolicy.Shared && (approval.Degree != 1 || approval.Workers != 1))
        {
            throw new InvalidOperationException("Operator approval does not authorize this installation's launch requirements.");
        }

        ArgumentNullException.ThrowIfNull(approval.Streams);
        ValidateDeadline(approval.Streams.ExchangeTimeout);
        ValidateDeadline(approval.Streams.TotalTimeout);
        ValidateDeadline(approval.InvocationTimeout);
        ValidateDeadline(approval.StartupTimeout);
        return new ProcessProfile(installation.RuntimeFiles[launch.Runtime], [installation.EntryPoints[launch.Runtime], ..launch.Arguments], true, approval.WorkspaceRoot, launch.MemoryMiB, approval.InvocationTimeout)
        {
            RuntimeValidation = installation.RuntimeValidation,
            ReusePolicy = approval.Ownership,
            WorkClass = approval.WorkClass,
            MaximumCallbacks = approval.MaximumCallbacks,
            StartupTimeout = approval.StartupTimeout,
            Reconstructible = approval.Reconstructible,
            ForwardStandardError = approval.ForwardStandardError
        };
    }

    private static void ValidateDeadline(TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Deadlines must be positive and fit a cancellation timer.");
        }
    }
}
