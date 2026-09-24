import { McpServer } from '@modelcontextprotocol/server';
import { serveStdio } from '@modelcontextprotocol/server/stdio';
import * as z from 'zod/v4';
import { normalize, echo } from './operations.mjs';
serveStdio(() => {
    const server = new McpServer({ name: 'document-functions-probe', version: '1.0.0' });
    for (const [name, operation] of Object.entries({ normalize, echo })) {
        server.registerTool(
            name,
            {
                description: `Pure ${name} operation; no AI`,
                inputSchema: z.object({ text: z.string() }),
            },
            async (args) => ({ content: [], structuredContent: operation(args) }),
        );
    }
    let counter = 0;
    server.registerTool(
        'diagnostics',
        {
            description: 'Probe-only process identity and private counter',
            inputSchema: z.object({}),
        },
        async () => ({
            content: [],
            structuredContent: {
                pid: process.pid,
                counter: ++counter,
                rss: process.memoryUsage().rss,
            },
        }),
    );
    return server;
});
