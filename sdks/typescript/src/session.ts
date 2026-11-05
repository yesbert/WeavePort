import { FailureCodes, FileErrors } from './protocol.js';
import { FileSession, InputFile, OutputFile, PluginFileError } from './files.js';
import type { FileInfo, PluginInput, PluginOutput } from './files.js';
/** Public cooperative session API. It does not erase arbitrary globals or library state. */
export interface PluginContext {
    readonly tenant: string;
    readonly configuration: unknown;
    callHost<T = unknown>(operation: string, input: unknown): Promise<T>;
    /** Cleanup runs in reverse registration order, including after handler failure. */
    onClose(action: () => void | Promise<void>): void;
    /** Transfer exclusive cleanup ownership of a closeable resource. */
    own<T extends { close(): void | Promise<void> }>(resource: T): T;
    /** Inputs the host named for this call; empty for streams, sources and calls without files. */
    readonly inputs: readonly FileInfo[];
    /** Open a host-named input; unknown names throw PluginFileError before any host request. */
    openInput(name: string): PluginInput;
    /** Create a named output; close it before returning. The host commits it only if the call succeeds. */
    createOutput(name: string, mediaType: string): Promise<PluginOutput>;
}

export class SessionCleanupError extends AggregateError {
    hasExecutionFailure = false;
    readonly code = FailureCodes.CleanupError;
}

export class SessionContext implements PluginContext {
    private active = true;
    private cleanup: (() => void | Promise<void>)[] = [];
    constructor(
        private owner: string | undefined,
        private config: unknown,
        private callback: (<T>(operation: string, input: unknown) => Promise<T>) | undefined,
        private files?: FileSession,
    ) {}
    get inputs(): readonly FileInfo[] {
        this.check();
        return this.files?.inputs ?? [];
    }
    openInput(name: string): PluginInput {
        this.check();
        if (!this.files) {
            throw new PluginFileError(FileErrors.InputNotFound);
        }
        return new InputFile(this.files, this.files.input(name));
    }
    async createOutput(name: string, mediaType: string): Promise<PluginOutput> {
        this.check();
        if (!this.files) {
            throw new PluginFileError(FileErrors.OutputNotPermitted);
        }
        this.files.reserveOutput(name, mediaType);
        await this.files.create(name, mediaType);
        return new OutputFile(this.files, name);
    }
    private check(): void {
        if (!this.active) {
            throw new Error('Session completed');
        }
    }
    get tenant(): string {
        this.check();
        return this.owner!;
    }
    get configuration(): unknown {
        this.check();
        return this.config;
    }
    onClose(action: () => void | Promise<void>): void {
        this.check();
        if (typeof action !== 'function') {
            throw new TypeError('Cleanup must be callable');
        }
        this.cleanup.push(action);
    }
    own<T extends { close(): void | Promise<void> }>(resource: T): T {
        this.onClose(() => resource.close());
        return resource;
    }
    async callHost<T>(operation: string, input: unknown): Promise<T> {
        this.check();
        return await this.callback!<T>(operation, input);
    }
    async complete(): Promise<void> {
        this.files?.close();
        this.active = false;
        this.owner = undefined;
        this.config = undefined;
        this.callback = undefined;
        const actions = this.cleanup;
        this.cleanup = [];
        const errors: unknown[] = [];
        for (const action of actions.reverse()) {
            try {
                await action();
            } catch (error) {
                errors.push(error);
            }
        }
        if (errors.length) {
            throw new SessionCleanupError(errors, 'Session cleanup failed');
        }
    }
}
