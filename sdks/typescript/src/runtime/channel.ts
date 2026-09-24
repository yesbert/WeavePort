import { Console } from 'node:console';
import { createConnection, Socket } from 'node:net';
import { createInterface } from 'node:readline';
import type { Readable, Writable } from 'node:stream';
import { ProtocolLimits } from '../protocol.js';
import { decodeFrame, encodeJson } from './messages.js';
import type { InboundFrame } from './messages.js';

/** Own the launcher-selected transport; author stdout is redirected to stderr. */
export class Channel {
    private reader!: AsyncIterator<string>;
    private output!: Writable;
    private socket?: Socket;

    async read(): Promise<InboundFrame | undefined> {
        const line = await this.reader.next();
        if (line.done) {
            return undefined;
        }
        if (Buffer.byteLength(line.value) > ProtocolLimits.FrameBytes) {
            throw new Error('Frame limit');
        }
        return decodeFrame(JSON.parse(line.value));
    }
    async send(value: unknown): Promise<void> {
        const line = encodeJson(value);
        if (Buffer.byteLength(line) > ProtocolLimits.FrameBytes) {
            throw new Error('Frame limit');
        }
        await new Promise<void>((resolve, reject) =>
            this.output.write(line + '\n', (error) => (error ? reject(error) : resolve())),
        );
    }

    async connect(): Promise<void> {
        let input: Readable = process.stdin;
        this.output = process.stdout;
        if (process.env.WEAVEPORT_SOCKET) {
            const socket = createConnection(process.env.WEAVEPORT_SOCKET);
            await new Promise<void>((resolve, reject) => {
                socket.once('connect', resolve);
                socket.once('error', reject);
            });
            this.socket = socket;
            input = socket;
            this.output = socket;
        }
        globalThis.console = new Console({
            stdout: process.stderr,
            stderr: process.stderr,
        });
        this.reader = createInterface({ input, crlfDelay: Infinity })[Symbol.asyncIterator]();
    }

    close(): void {
        this.socket?.destroy();
    }
}
