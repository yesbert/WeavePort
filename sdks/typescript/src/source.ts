import { randomUUID } from 'node:crypto';
import type { PluginContext } from './session.js';

/** A binary source owns its position and returns at most the requested bytes. */
export interface BinarySource {
    read(maxBytes: number): Uint8Array | Promise<Uint8Array>;
    close(): void | Promise<void>;
}
export type SourceHandler = (input: any, context: PluginContext, signal: AbortSignal) => BinarySource | Promise<BinarySource>;
export class SourceSession {
    private source?: BinarySource;
    identity?: string;
    async open(handler: SourceHandler, value: any, context: PluginContext, signal: AbortSignal): Promise<unknown> {
        const source = await handler(value, context, signal);
        if (typeof source?.read !== 'function' || typeof source?.close !== 'function') throw new TypeError('Invalid binary source');
        this.source = context.own(source);
        this.identity = randomUUID();
        return { source: this.identity };
    }
    async read(payload: any): Promise<{ data: string; done: boolean }> {
        const size = payload.chunkBytes;
        if (!Number.isInteger(size) || size < 4096 || size > 262144) throw new Error('Source chunk limit');
        const data = await this.source!.read(size);
        if (!(data instanceof Uint8Array) || data.length > size) throw new Error('Source must return bounded bytes');
        return { data: Buffer.from(data).toString('base64'), done: data.length === 0 };
    }
    release(): void { this.source = undefined; this.identity = undefined; }
}
