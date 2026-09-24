using System.Diagnostics;
using System.Text.Json;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;
using WeavePort.SdkFixture;

internal sealed partial class SdkVerification
{
    private async Task CompleteWorkloadsAsync(IPluginClient client, string tenant)
    {
        foreach (string name in Workload.Cases)
        {
            Workload.Check(await Workload.ExecuteAsync(client, name), name, tenant);
            _checks++;
        }
    }

    private async Task TypedResultsAsync(IPluginClient client, string tenant)
    {
        var nullValue = await client.CallAsync("echo", JsonSerializer.SerializeToElement<object?>(null));
        Assert(nullValue.ValueKind == JsonValueKind.Null, "Null contract");
        string text = "ü🚀<>&\u0000";
        var typed = await client.CallAsync<object, Echo>("echo", new
        {
            value = text
        });
        Assert(typed.Value == text, "Typed Unicode contract");
        int typedRows = 0;
        await foreach (var row in client.StreamAsync<object, TypedRow>("records", new
        {
            count = 2,
            width = 3
        }))
        {
            Assert(row.Id == typedRows++ && row.Text == "xxx" && row.Owner == tenant, "Typed stream");
        }

    }

    private async Task CallbackAuthorityAsync(IPluginClient client, string tenant)
    {
        bool denied = false;
        try
        {
            await client.CallAsync("owner", JsonSerializer.SerializeToElement(new
            {
                operation = "forbidden",
                input = new
                {
                    tenant = "tenant-other"
                }
            }));
        }
        catch (PluginCallException e) when (e.Status == "denied") { denied = true; }
        Assert(denied, "Callback grant denied");
        Workload.Check(await Workload.ExecuteAsync(client, "callback"), "callback", tenant);
        _checks++;
    }
}
