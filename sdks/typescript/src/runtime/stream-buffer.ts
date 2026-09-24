import { ProtocolLimits } from '../protocol.js';
const BATCH_WAIT_MILLISECONDS = 25;
// Reserve brackets and a conservative comma per accepted item.
const ARRAY_BRACKET_BYTES = 2;
const ITEM_SEPARATOR_BYTES = 1;
/** Bounded batch state; only one iterator advancement may remain in flight. */
export class StreamBuffer {
    private pending?: unknown;
    private hasPending = false;
    private bytes = 0;
    private advance?: Promise<IteratorResult<unknown>>;

    constructor(private readonly encode: (value: unknown) => string) {}

    async read(
        iterator: AsyncIterator<unknown>,
        onItem: () => void,
    ): Promise<{ items: unknown[]; done: boolean }> {
        const items: unknown[] = [];
        let batchBytes = ARRAY_BRACKET_BYTES;
        let done = false;
        while (items.length < ProtocolLimits.StreamBatchItems) {
            const next =
                this.takePending() ??
                (await this.next(iterator, items.length ? 0 : BATCH_WAIT_MILLISECONDS));
            if (next === undefined) {
                break;
            }
            if (next.done) {
                done = true;
                break;
            }
            const item = next.value;
            const size = Buffer.byteLength(this.encode(item));
            if (size > ProtocolLimits.StreamItemBytes) {
                throw new Error('Item limit');
            }
            if (batchBytes + size + ITEM_SEPARATOR_BYTES > ProtocolLimits.StreamBatchBytes) {
                this.pending = item;
                this.hasPending = true;
                break;
            }
            batchBytes += size + ITEM_SEPARATOR_BYTES;
            this.bytes += size;
            if (this.bytes > ProtocolLimits.StreamTotalBytes) {
                throw new Error('Stream limit');
            }
            items.push(item);
            onItem();
        }
        return { items, done };
    }

    private takePending(): IteratorResult<unknown> | undefined {
        if (!this.hasPending) {
            return undefined;
        }
        const value = this.pending;
        this.pending = undefined;
        this.hasPending = false;
        return { value, done: false };
    }

    private async next(
        iterator: AsyncIterator<unknown>,
        waitMs: number,
    ): Promise<IteratorResult<unknown> | undefined> {
        this.advance ??= iterator.next();
        let timer: ReturnType<typeof setTimeout> | undefined;
        const next = await Promise.race([
            this.advance,
            new Promise<undefined>((resolve) => {
                timer = setTimeout(() => resolve(undefined), waitMs);
            }),
        ]);
        clearTimeout(timer);
        if (next !== undefined) {
            this.advance = undefined;
        }
        return next;
    }

    /** Detach state synchronously, then let the owner await any pending advancement. */
    reset(): Promise<IteratorResult<unknown>> | undefined {
        const advance = this.advance;
        this.advance = undefined;
        this.pending = undefined;
        this.hasPending = false;
        this.bytes = 0;
        return advance;
    }
}
