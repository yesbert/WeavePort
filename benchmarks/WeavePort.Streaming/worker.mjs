import {pathToFileURL} from 'node:url';
const {PluginApplication} = await import(pathToFileURL(process.argv[2]).href);
const app = new PluginApplication();
app.stream('records', async function* (query, context) {
    for (let id = 0; id < query.count; id++) {
        if (query.delayMs > 0) await new Promise(resolve => setTimeout(resolve, query.delayMs));
        yield {id, text: 'x'.repeat(query.width), owner: context.tenant};
    }
});
await app.run();
