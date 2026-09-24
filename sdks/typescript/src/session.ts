import { FailureCodes } from './protocol.js';
/** Public cooperative session API. It does not erase arbitrary globals or library state. */
export interface PluginContext {
    readonly tenant: string;
    readonly configuration: unknown;
    callHost<T = unknown>(operation: string, input: unknown): Promise<T>;
    /** Cleanup runs in reverse registration order, including after handler failure. */
    onClose(action: () => void | Promise<void>): void;
    /** Transfer exclusive cleanup ownership of a closeable resource. */
    own<T extends { close(): void | Promise<void> }>(resource: T): T;
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
    ) {}
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
