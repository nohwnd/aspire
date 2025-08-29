// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Aspire.Dashboard.McpTools;

[McpServerToolType]
internal sealed class ResourceMcpTools
{
    [McpServerTool, Description("Lists resource names and their current state.")]
    public static async Task<object> ListResources(IDashboardClient dashboardClient)
    {
        var resources = new List<object>();

        try
        {
            var cts = new CancellationTokenSource(millisecondsDelay: 500);

            var subscription = await dashboardClient.SubscribeResourcesAsync(cts.Token).ConfigureAwait(false);
            foreach (var resource in subscription.InitialState)
            {
                resources.Add(new
                {
                    resource.Name,
                    resource.ResourceType,
                    resource.State,
                });
            }

        }
        catch
        {
            // Keep tool robust - return empty list on error.
        }

        return resources;
    }
}
