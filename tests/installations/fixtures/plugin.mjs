import { pathToFileURL } from 'node:url';
const { PluginApplication } = await import(pathToFileURL(process.argv[2]).href);
const app = new PluginApplication('1', { concurrentCalls: true });
app.function('who', async (input, context) => ({ tenant: context.tenant, language: 'node' }));
await app.run();
