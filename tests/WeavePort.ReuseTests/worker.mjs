import { open, unlink, access } from 'node:fs/promises';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
const { PluginApplication } = await import(process.argv[2]);
const app = new PluginApplication();
let previous, oldFile, hidden;
const cache = new Map();
app.function('session', async (_, context) => {
    let expired = !previous;
    if (previous) { try { await previous.callHost('who', {}); } catch { expired = true; } }
    let fileExists = false;
    if (oldFile) { try { await access(oldFile); fileExists = true; } catch {} }
    if (cache.size || fileExists) throw new Error('Registered residue');
    const secret = context.configuration.secret;
    cache.set('secret', secret);
    context.onClose(() => cache.clear());
    const path = oldFile = join(process.env.WEAVEPORT_WORKSPACE || '/tmp', randomUUID() + '.session');
    context.onClose(() => unlink(path));
    const file = context.own(await open(path, 'wx'));
    await file.write(secret);
    previous = context;
    const owner = await context.callHost('who', { tenant: 'forged' });
    return { tenant: context.tenant, secret, expired, callback: owner };
});
app.function('stash', async (_, context) => { hidden = context.tenant; return {stored:true}; });
app.function('probe', async () => ({ hidden: hidden ?? null }));
app.function('cleanup-fail', async (_, context) => { context.onClose(() => { throw new Error('Deliberate cleanup failure'); }); return {}; });
app.function('cleanup-hang', async (_, context) => { context.onClose(() => new Promise(resolve => setTimeout(resolve, 30000))); return {}; });
app.function('delay', async (value, context) => { await new Promise(resolve => setTimeout(resolve,value.ms)); return {tenant:context.tenant}; });
app.stream('rows', async function* (_,context) { previous=context; for(let i=0;i<20;i++) yield {tenant:context.tenant,i}; });
await app.run();
