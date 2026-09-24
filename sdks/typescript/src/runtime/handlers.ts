import type { PluginContext } from '../session.js';

export type Handler = (input: any, context: PluginContext, signal: AbortSignal) => Promise<unknown>;
export type Generator = (
    input: any,
    context: PluginContext,
    signal: AbortSignal,
) => AsyncIterable<unknown>;
