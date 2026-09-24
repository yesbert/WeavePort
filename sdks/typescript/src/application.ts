import { Runtime } from './runtime/runtime.js';
import type { Handler, Generator } from './runtime/handlers.js';
import type { SourceHandler, BinarySource } from './source.js';
import type { PluginContext } from './session.js';

/** Register business handlers; this SDK supplies the executable protocol runtime. */
export class PluginApplication {
    /** Author-declared artifact version, verified against the host binding at startup. */
    constructor(
        private readonly pluginVersion: string = '1',
        private readonly options: { concurrentCalls?: boolean } = {},
    ) {
        if (typeof pluginVersion !== 'string' || !pluginVersion.trim()) {
            throw new Error('Invalid plugin version');
        }
    }
    private functions = new Map<string, Handler>();
    private streams = new Map<string, Generator>();
    private sources = new Map<string, SourceHandler>();
    function<T, R>(
        name: string,
        handler: (input: T, context: PluginContext, signal: AbortSignal) => Promise<R>,
    ): this {
        this.validate(name);
        this.functions.set(name, handler);
        return this;
    }
    stream<T, R>(
        name: string,
        handler: (input: T, context: PluginContext, signal: AbortSignal) => AsyncIterable<R>,
    ): this {
        this.validate(name);
        this.streams.set(name, handler);
        return this;
    }
    source<T>(
        name: string,
        handler: (
            input: T,
            context: PluginContext,
            signal: AbortSignal,
        ) => BinarySource | Promise<BinarySource>,
    ): this {
        this.validate(name);
        this.sources.set(name, handler);
        return this;
    }
    private validate(name: string) {
        if (
            !name ||
            name.startsWith('$') ||
            this.functions.has(name) ||
            this.streams.has(name) ||
            this.sources.has(name)
        ) {
            throw new Error('Invalid or duplicate operation');
        }
    }
    async run(): Promise<void> {
        await new Runtime(
            this.functions,
            this.streams,
            this.sources,
            this.pluginVersion,
            this.options.concurrentCalls === true,
        ).run();
    }
}
