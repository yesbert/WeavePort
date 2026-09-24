import { API } from 'typescript/unstable/sync';
import { SyntaxKind as K } from 'typescript/unstable/ast';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import assert from 'node:assert/strict';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const controls = new Set([
    K.IfStatement,
    K.ForStatement,
    K.ForOfStatement,
    K.ForInStatement,
    K.WhileStatement,
    K.DoStatement,
]);
const scopes = new Set([
    K.FunctionDeclaration,
    K.FunctionExpression,
    K.ArrowFunction,
    K.MethodDeclaration,
    K.Constructor,
    K.GetAccessor,
    K.SetAccessor,
]);
const exits = new Set([K.ReturnStatement, K.ThrowStatement, K.BreakStatement, K.ContinueStatement]);

function guard(node) {
    if (node.kind !== K.IfStatement || node.elseStatement) return false;
    const body = node.thenStatement;
    const last = body.kind === K.Block ? body.statements.at(-1) : body;
    let nested = false;
    const visit = (child) => {
        if (controls.has(child.kind)) nested = true;
        child.forEachChild(visit);
    };
    body.forEachChild(visit);
    return !nested && last && exits.has(last.kind);
}

export function violations(source) {
    const failures = [];
    const visit = (node, parent, alternative = false) => {
        if (scopes.has(node.kind)) parent = undefined;
        if (controls.has(node.kind)) {
            if (parent && !alternative && !(parent.kind !== K.IfStatement && guard(node))) {
                failures.push(source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1);
            }
            parent = node;
        }
        node.forEachChild((child) => {
            visit(
                child,
                parent,
                node.kind === K.IfStatement &&
                    node.elseStatement === child &&
                    child.kind === K.IfStatement,
            );
        });
    };
    visit(source);
    return failures;
}

function verifyFixtures() {
    const directory = mkdtempSync(resolve(tmpdir(), 'weaveport-control-flow-'));
    const fixtures = [
        ['if (a) return; if (b) return;', false],
        ['for (const x of xs) { if (!x) continue; use(x); }', false],
        ['if (a) A(); else if (b) B(); else C();', false],
        ['if (a) { if (b) use(); }', true],
        ['for (const x of xs) { for (const y of ys) use(x, y); }', true],
        ['if (a) { for (const x of xs) use(x); }', true],
        ['for (const x of xs) { if (x) use(x); }', true],
    ];
    const api = new API({ cwd: directory });
    try {
        writeFileSync(
            resolve(directory, 'tsconfig.json'),
            JSON.stringify({ files: fixtures.map((_, index) => `${index}.ts`) }),
        );
        fixtures.forEach(([source], index) =>
            writeFileSync(resolve(directory, `${index}.ts`), `function fixture() { ${source} }`),
        );
        const snapshot = api.updateSnapshot({
            openProjects: [resolve(directory, 'tsconfig.json')],
        });
        const project = snapshot.getProject(resolve(directory, 'tsconfig.json'));
        fixtures.forEach(([source, rejected], index) => {
            const file = project.program.getSourceFile(resolve(directory, `${index}.ts`));
            assert.equal(violations(file).length > 0, rejected, source);
        });
        snapshot.dispose();
    } finally {
        api.close();
        rmSync(directory, { recursive: true, force: true });
    }
}

verifyFixtures();

const api = new API({ cwd: root });
try {
    const snapshot = api.updateSnapshot({ openProjects: [resolve(root, 'tsconfig.json')] });
    const project = snapshot.getProject(resolve(root, 'tsconfig.json'));
    if (!project) throw new Error('TypeScript project missing');
    const failures = project.rootFiles.flatMap((path) => {
        const source = project.program.getSourceFile(path);
        if (!source) throw new Error(`Source missing: ${path}`);
        return violations(source).map((line) => `${path}:${line}`);
    });
    snapshot.dispose();
    console.log(failures.length ? failures.join('\n') : 'TypeScript control flow: passed');
    process.exitCode = failures.length ? 1 : 0;
} finally {
    api.close();
}
