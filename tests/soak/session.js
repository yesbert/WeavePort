import { Stream } from 'k6/net/grpc';
import { decode, require, ok } from './protocol.js';

export class Session {
    constructor(client, credential) {
        this.pending = null;
        this.failure = null;
        this.stream = new Stream(client, 'weaveport.sdk.WorkerGateway/Session', {
            metadata: { 'x-weaveport-binding': credential },
            timeout: '30s',
        });
        this.ended = new Promise((resolve) => {
            this.endResolve = resolve;
        });
        this.stream.on('data', (reply) => {
            const active = this.pending;
            if (!active) {
                this.failure = new Error('Unsolicited response');
                return;
            }
            try {
                if (reply.complete) {
                    active.terminal(reply);
                    this.pending = null;
                    active.resolve();
                } else {
                    active.row(decode(reply.json));
                }
            } catch (error) {
                this.pending = null;
                this.failure = error;
                active.reject(error);
            }
        });
        this.stream.on('error', () => this.fail(new Error('gRPC transport error')));
        this.stream.on('end', () => {
            if (this.pending) {
                this.fail(new Error('Missing terminal response'));
            }
            this.endResolve();
        });
    }
    fail(error) {
        this.failure = error;
        if (this.pending) {
            this.pending.reject(error);
            this.pending = null;
        }
        this.endResolve();
    }
    exchange(
        request,
        row = () => {
            throw new Error('Unexpected row');
        },
        terminal = ok,
    ) {
        if (this.failure) {
            return Promise.reject(this.failure);
        }
        require(!this.pending, 'Concurrent exchange on one session');
        return new Promise((resolve, reject) => {
            this.pending = { row, terminal, resolve, reject };
            this.stream.write(request);
        });
    }
    async close() {
        this.stream.end();
        await this.ended;
        if (this.failure) {
            throw this.failure;
        }
    }
}
