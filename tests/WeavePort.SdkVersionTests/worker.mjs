import { PluginApplication } from '@weaveport/sdk';
const app =
    process.argv[2] === 'default'
        ? new PluginApplication()
        : new PluginApplication(process.argv[2]);
await app.function('echo', async (value) => value).run();
