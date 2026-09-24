export function normalize({ text }) {
    if (typeof text !== 'string') {
        throw new Error('text must be a string');
    }
    return { text: text.trim().replace(/\s+/g, ' ') };
}
export function echo({ text }) {
    if (typeof text !== 'string') {
        throw new Error('text must be a string');
    }
    return { text };
}
