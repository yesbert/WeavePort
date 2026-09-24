import { rmSync } from 'node:fs';

// Removed source modules must not survive into the next packed SDK.
rmSync(new URL('../dist/', import.meta.url), { recursive: true, force: true });
