import { PluginApplication } from '../../sdks/typescript/dist/index.js';
import { normalize, echo } from './operations.mjs';
const app = new PluginApplication('1');
for (const [name, operation] of Object.entries({ normalize, echo })) app.function(name, async args => operation(args));
let counter = 0;
app.function('diagnostics', async () => ({ pid: process.pid, counter: ++counter, rss: process.memoryUsage().rss }));
await app.run();
