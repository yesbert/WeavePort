import { FileErrors, ProtocolLimits, SdkOperations } from './protocol.js';

/** A host-named input of the current invocation. */
export type FileInfo = { readonly name: string; readonly length: number; readonly mediaType: string };

/** A refused file operation; the invocation continues unless the handler rethrows. */
export class PluginFileError extends Error {
    constructor(readonly code: string) {
        super(`Function file operation refused: ${code}`);
    }
}

/** A host input readable at any position. Recently read blocks are cached. */
export interface PluginInput {
    readonly name: string;
    readonly size: number;
    readonly mediaType: string;
    /** Returns up to `length` bytes from `position`; fewer only at the end of the input. */
    read(position: number, length: number): Promise<Uint8Array>;
}

/** A named output. `close()` completes it; the host commits it only if the call succeeds. */
export interface PluginOutput {
    readonly name: string;
    readonly written: number;
    write(chunk: Uint8Array): Promise<void>;
    close(): Promise<void>;
    /** A WritableStream whose close completes the output. */
    toWritableStream(): WritableStream<Uint8Array>;
}

export type FileExchange = (
    operation: string,
    payload: unknown,
) => Promise<{ value?: unknown; error?: string }>;

const NAME = /^[A-Za-z0-9_-][A-Za-z0-9._-]{0,127}$/;
const MEDIA_TYPE = /^[!-~]{1,255}$/;

export function validateBlockBytes(value: number): number {
    if (
        !Number.isInteger(value) ||
        value < ProtocolLimits.FileBlockMinimumBytes ||
        value > ProtocolLimits.FileBlockMaximumBytes
    ) {
        throw new RangeError('File block size must be 4096-262144 bytes');
    }
    return value;
}

/** Invocation-owned descriptors and the reserved file operations. */
export class FileSession {
    readonly inputs: readonly FileInfo[];
    readonly outputsPermitted: boolean;
    private readonly outputs = new Set<string>();
    private closed = false;
    constructor(
        files: unknown,
        private exchange: FileExchange | undefined,
        readonly blockBytes: number,
    ) {
        const descriptor = (files ?? {}) as { inputs?: unknown[]; outputs?: unknown };
        this.inputs = (descriptor.inputs ?? []).map((item) => {
            const input = item as { name: string; length: number; mediaType: string };
            return { name: input.name, length: input.length, mediaType: input.mediaType };
        });
        this.outputsPermitted = typeof descriptor.outputs === 'object' && descriptor.outputs !== null;
    }
    input(name: string): FileInfo {
        const info = this.inputs.find((input) => input.name === name);
        if (!info) {
            throw new PluginFileError(FileErrors.InputNotFound);
        }
        return info;
    }
    reserveOutput(name: string, mediaType: string): void {
        if (typeof name !== 'string' || !NAME.test(name)) {
            throw new TypeError('Invalid output name');
        }
        if (typeof mediaType !== 'string' || !MEDIA_TYPE.test(mediaType)) {
            throw new TypeError('Invalid media type');
        }
        this.check();
        if (!this.outputsPermitted) {
            throw new PluginFileError(FileErrors.OutputNotPermitted);
        }
        if (this.outputs.has(name)) {
            throw new PluginFileError(FileErrors.OutputExists);
        }
        this.outputs.add(name);
    }
    async read(name: string, offset: number): Promise<Uint8Array> {
        const value = (await this.send(SdkOperations.InputRead, {
            name,
            offset,
            length: this.blockBytes,
        })) as { data: string };
        return new Uint8Array(Buffer.from(value.data, 'base64'));
    }
    async create(name: string, mediaType: string): Promise<void> {
        await this.send(SdkOperations.OutputCreate, { name, mediaType });
    }
    async write(name: string, data: Uint8Array): Promise<void> {
        await this.send(SdkOperations.OutputWrite, {
            name,
            data: Buffer.from(data.buffer, data.byteOffset, data.byteLength).toString('base64'),
        });
    }
    async complete(name: string): Promise<void> {
        await this.send(SdkOperations.OutputComplete, { name });
    }
    close(): void {
        this.closed = true;
        this.exchange = undefined;
    }
    ensureOpen(): void {
        this.check();
    }
    private check(): void {
        if (this.closed) {
            throw new Error('Session completed');
        }
    }
    private async send(operation: string, payload: unknown): Promise<unknown> {
        this.check();
        const reply = await this.exchange!(operation, payload);
        if (reply.error !== undefined) {
            throw new PluginFileError(reply.error);
        }
        return reply.value;
    }
}

export class InputFile implements PluginInput {
    private readonly cache = new Map<number, Uint8Array>();
    constructor(
        private files: FileSession,
        private info: FileInfo,
    ) {}
    get name(): string {
        return this.info.name;
    }
    get size(): number {
        return this.info.length;
    }
    get mediaType(): string {
        return this.info.mediaType;
    }
    async read(position: number, length: number): Promise<Uint8Array> {
        if (!Number.isSafeInteger(position) || !Number.isSafeInteger(length) || position < 0 || length < 0) {
            throw new RangeError('Invalid range');
        }
        // Checked on every read, including cache hits, so a retained input cannot outlive its call.
        this.files.ensureOpen();
        const end = Math.min(position + length, this.size);
        const result = new Uint8Array(Math.max(end - position, 0));
        let offset = position;
        while (offset < end) {
            const block = await this.block(Math.floor(offset / this.files.blockBytes));
            const start = offset % this.files.blockBytes;
            const chunk = block.subarray(start, start + end - offset);
            if (chunk.length === 0) {
                break;
            }
            result.set(chunk, offset - position);
            offset += chunk.length;
        }
        return result.subarray(0, offset - position);
    }
    private async block(index: number): Promise<Uint8Array> {
        const cached = this.cache.get(index);
        if (cached) {
            this.cache.delete(index);
            this.cache.set(index, cached);
            return cached;
        }
        const data = await this.files.read(this.name, index * this.files.blockBytes);
        this.cache.set(index, data);
        if (this.cache.size > ProtocolLimits.FileBlockCacheBlocks) {
            this.cache.delete(this.cache.keys().next().value!);
        }
        return data;
    }
}

export class OutputFile implements PluginOutput {
    private buffer: Uint8Array;
    private count = 0;
    private total = 0;
    private completed = false;
    constructor(
        private files: FileSession,
        readonly name: string,
    ) {
        this.buffer = new Uint8Array(files.blockBytes);
    }
    get written(): number {
        return this.total;
    }
    async write(chunk: Uint8Array): Promise<void> {
        if (this.completed) {
            throw new PluginFileError(FileErrors.OutputClosed);
        }
        let remaining = chunk;
        this.total += chunk.length;
        while (remaining.length > 0) {
            const count = Math.min(this.buffer.length - this.count, remaining.length);
            this.buffer.set(remaining.subarray(0, count), this.count);
            this.count += count;
            remaining = remaining.subarray(count);
            await this.sendFullBlock();
        }
    }
    async close(): Promise<void> {
        if (this.completed) {
            return;
        }
        this.completed = true;
        if (this.count > 0) {
            await this.files.write(this.name, this.buffer.subarray(0, this.count));
            this.count = 0;
        }
        await this.files.complete(this.name);
    }
    toWritableStream(): WritableStream<Uint8Array> {
        return new WritableStream<Uint8Array>({
            write: (chunk) => this.write(chunk),
            close: () => this.close(),
        });
    }
    private async sendFullBlock(): Promise<void> {
        if (this.count < this.buffer.length) {
            return;
        }
        await this.files.write(this.name, this.buffer);
        this.count = 0;
    }
}
