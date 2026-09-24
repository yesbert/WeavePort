import { SessionCleanupError } from '../session.js';

export async function cleanupAll(...actions: (() => Promise<unknown>)[]): Promise<void> {
    const errors: unknown[] = [];
    for (const action of actions) {
        try {
            await action();
        } catch (error) {
            errors.push(error);
        }
    }
    if (errors.length) throw new SessionCleanupError(errors, 'Owned resource cleanup failed');
}

export async function executeWithCleanup<T>(
    action: () => Promise<T>,
    cleanup: () => Promise<void>,
): Promise<T> {
    const failures: unknown[] = [];
    try {
        return await action();
    } catch (error) {
        failures.push(error);
        throw error;
    } finally {
        try {
            await cleanup();
        } catch (error) {
            if (!failures.length) throw error;
            const combined = new SessionCleanupError(
                [...failures, error],
                'Execution and cleanup failed',
            );
            combined.hasExecutionFailure = true;
            throw combined;
        }
    }
}
