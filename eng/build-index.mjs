#!/usr/bin/env node
// Repository machinery (run by CI, .github/workflows/samples.yml): checks the samples' metadata, keeps the sample
// table in README.md current, and builds the published index.
//
//   node eng/build-index.mjs                   validate every samples/*/*/sample.json (schema/sample.schema.json) and
//                                              rewrite the sample table in README.md
//   node eng/build-index.mjs --check           validate only; fail if the README table is out of date
//                                              (needs neither .NET nor Altium Designer: runs on every pull request)
//   node eng/build-index.mjs --api --out DIR   also resolve the SDK APIs each sample's code uses (eng/ApiUsage, the C#
//                                              compiler; the samples must build: AltiumSystemDir set, restored) and
//                                              write DIR/index.json (schema/index.schema.json) and DIR/llms.txt
//
// The index is a build output (the CI artifact 'samples-index', published as a release); it is never committed.
// "primary": an API the README names under "What it shows" (in backticks, as `NS.Type.Member`, `Type.Member` or
// `Type`), as opposed to plumbing. "digest": a hash of the sample's files (README, sources, installation files), so the
// index changes whenever a sample does. "source": the repository and commit the index was built from (from
// GITHUB_REPOSITORY / GITHUB_SHA in CI, else from git); line references are relative to that commit.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const CHECK = args.includes('--check');
const API = args.includes('--api');
const outArg = args.indexOf('--out');
const OUT = outArg >= 0 ? resolve(args[outArg + 1]) : null;
if (API && !OUT) { console.error('build-index: --api needs --out <dir>'); process.exit(2); }
const problems = [];
const readJson = (p) => JSON.parse(readFileSync(p, 'utf8'));
const posix = (p) => p.split('\\').join('/');

const schema = readJson(join(root, 'schema', 'sample.schema.json'));
const CATEGORY_TITLES = {
  schematic: 'Schematic', pcb: 'PCB', library: 'Library management', '3d-step': '3D and STEP models',
  output: 'Design output', project: 'Project management', 'framework-ui': 'Extension framework and UI',
  integration: 'External integration', licensing: 'Licensing',
};

// ---------------------------------------------------------------- a small JSON Schema validator
// Covers what schema/*.schema.json use: type, const, enum, pattern, min/maxLength, min/maxItems, uniqueItems,
// required, properties, additionalProperties (false or a schema), items, minimum.
function validate(s, v, at, out) {
  const type = Array.isArray(v) ? 'array' : v === null ? 'null' : Number.isInteger(v) ? 'integer' : typeof v;
  if (s.type && !(s.type === type || (s.type === 'number' && type === 'integer'))) { out.push(`${at}: expected ${s.type}, got ${type}`); return out; }
  if ('const' in s && v !== s.const) out.push(`${at}: must be ${JSON.stringify(s.const)}`);
  if (s.enum && !s.enum.includes(v)) out.push(`${at}: '${v}' is not one of ${s.enum.join(', ')}`);
  if (typeof v === 'string') {
    if (s.pattern && !new RegExp(s.pattern).test(v)) out.push(`${at}: '${v}' does not match ${s.pattern}`);
    if (s.minLength && v.length < s.minLength) out.push(`${at}: shorter than ${s.minLength}`);
    if (s.maxLength && v.length > s.maxLength) out.push(`${at}: longer than ${s.maxLength}`);
  }
  if (typeof v === 'number' && 'minimum' in s && v < s.minimum) out.push(`${at}: below ${s.minimum}`);
  if (Array.isArray(v)) {
    if (s.minItems && v.length < s.minItems) out.push(`${at}: fewer than ${s.minItems} items`);
    if (s.maxItems && v.length > s.maxItems) out.push(`${at}: more than ${s.maxItems} items`);
    if (s.uniqueItems && new Set(v.map((x) => JSON.stringify(x))).size !== v.length) out.push(`${at}: duplicate items`);
    if (s.items) v.forEach((x, i) => validate(s.items, x, `${at}[${i}]`, out));
  }
  if (type === 'object') {
    for (const r of s.required ?? []) if (!(r in v)) out.push(`${at}: missing ${r}`);
    for (const [k, x] of Object.entries(v)) {
      if (s.properties?.[k]) validate(s.properties[k], x, `${at}.${k}`, out);
      else if (s.additionalProperties === false) out.push(`${at}: unknown property ${k}`);
      else if (typeof s.additionalProperties === 'object') validate(s.additionalProperties, x, `${at}.${k}`, out);
    }
  }
  return out;
}

// ---------------------------------------------------------------- the samples

const samples = [];
for (const category of readdirSync(join(root, 'samples')).filter((c) => statSync(join(root, 'samples', c)).isDirectory()).sort()) {
  for (const id of readdirSync(join(root, 'samples', category)).sort()) {
    const dir = join(root, 'samples', category, id);
    if (!statSync(dir).isDirectory()) continue;
    if (!existsSync(join(dir, 'sample.json'))) { problems.push(`${posix(relative(root, dir))}: no sample.json`); continue; }
    if (!existsSync(join(dir, 'README.md'))) problems.push(`${posix(relative(root, dir))}: no README.md`);
    samples.push({ dir, path: posix(relative(root, dir)), meta: readJson(join(dir, 'sample.json')) });
  }
}
for (const s of samples) {
  problems.push(...validate(schema, s.meta, `${s.path}/sample.json`, []));
  if (s.meta.id !== s.path.split('/').pop()) problems.push(`${s.path}/sample.json: id '${s.meta.id}' is not the folder name`);
  if (s.meta.category !== s.path.split('/')[1]) problems.push(`${s.path}/sample.json: category '${s.meta.category}' is not the folder`);
}

// ---------------------------------------------------------------- the README table (committed)

const list = samples.map((s) => s.meta);
const groups = Object.entries(CATEGORY_TITLES).map(([c, title]) => [title, samples.filter((s) => s.meta.category === c)]).filter(([, l]) => l.length);
const START = '<!-- samples-index:start (generated by eng/build-index.mjs) -->', END = '<!-- samples-index:end -->';
const table = groups.map(([title, l]) => `### ${title}\n\n| Sample | What it does | Hooks into Altium Designer as |\n|---|---|---|\n` +
  l.map(({ path, meta: m }) => `| [${m.id}](${path}/) | ${String(m.title).replace(/\|/g, '\\|')} | ${m.surface.join(', ')}${m.requiresRestart ? ' (restart)' : ''} |`).join('\n') + '\n').join('\n');
const readmeFile = join(root, 'README.md');
const readme = readFileSync(readmeFile, 'utf8');
const a = readme.indexOf(START), b = readme.indexOf(END);
if (a < 0 || b < a) problems.push('README.md: the samples-index markers are missing');
else {
  // Keep the file's own line endings (a Windows checkout may have CRLF), and compare ignoring them.
  const eol = readme.includes('\r\n') ? '\r\n' : '\n';
  const updated = `${readme.slice(0, a + START.length)}\n\n${table}\n${readme.slice(b)}`.replace(/\r?\n/g, eol);
  const lf = (t) => t.replace(/\r\n/g, '\n');
  if (CHECK) { if (lf(updated) !== lf(readme)) problems.push('README.md: the sample table is out of date: run node eng/build-index.mjs'); }
  else if (updated !== readme) writeFileSync(readmeFile, updated);
}

// ---------------------------------------------------------------- the published index (--api)

if (API) {
  const tmp = mkdtempSync(join(tmpdir(), 'ad-samples-api-'));
  const usageFile = join(tmp, 'usage.json');
  try {
    execFileSync('dotnet', ['run', '--project', join(root, 'eng', 'ApiUsage'), '-c', 'Release', '--', ...samples.map((s) => s.dir), '--out', usageFile],
      { cwd: root, stdio: ['ignore', 'inherit', 'inherit'] });
  } catch (e) { problems.push(`eng/ApiUsage failed: ${e.message.split('\n')[0]}`); }
  const usage = existsSync(usageFile) ? readJson(usageFile).samples : {};
  rmSync(tmp, { recursive: true, force: true });

  const namedApis = (md) => {
    const m = /^## What it shows\s*\n([\s\S]*?)(?=^## |(?![\s\S]))/m.exec(md);
    if (!m) return [];
    return [...m[1].matchAll(/`([^`]+)`/g)].map(([, span]) => span.replace(/\(.*$/, '').trim()
      .split('.').map((seg) => seg.replace(/^(Get|Set)State_/, '')).join('.'))
      .filter((span) => /^[A-Za-z_][A-Za-z0-9_.]*$/.test(span) && (span.includes('.') || /^(I|T)[A-Z]/.test(span)));
  };

  // The sample's files, in path order, with line endings normalized (a Windows checkout may have CRLF).
  const digestOf = (dir) => {
    const files = [];
    const walk = (d) => { for (const n of readdirSync(d).sort()) { const f = join(d, n);
      if (statSync(f).isDirectory()) { if (!['bin', 'obj'].includes(n)) walk(f); } else files.push(f); } };
    walk(dir);
    const h = createHash('sha256');
    for (const f of files) {
      const data = readFileSync(f);
      const text = /\.(cs|csproj|md|json|ins|rcs|out|csv|txt|xml|resx|config|props|targets)$/i.test(f)
        ? Buffer.from(data.toString('utf8').replace(/\r\n/g, '\n')) : data;
      h.update(posix(relative(dir, f))).update('\0').update(text).update('\0');
    }
    return `sha256:${h.digest('hex')}`;
  };
  const git = (...a) => { try { return execFileSync('git', a, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim(); } catch { return ''; } };
  const repoUrl = process.env.GITHUB_REPOSITORY ? `https://github.com/${process.env.GITHUB_REPOSITORY}`
    : git('remote', 'get-url', 'origin').replace(/\.git$/, '');
  const commit = process.env.GITHUB_SHA || git('rev-parse', 'HEAD');

  const apisOf = new Map();
  for (const s of samples) {
    const u = usage[s.meta.id];
    if (!u) { problems.push(`${s.path}: no API usage produced`); apisOf.set(s.meta.id, []); continue; }
    for (const e of u.errors ?? []) problems.push(`${s.path}: ${e}`);
    const named = namedApis(existsSync(join(s.dir, 'README.md')) ? readFileSync(join(s.dir, 'README.md'), 'utf8') : '');
    const apis = [];
    for (const kind of ['types', 'members'])
      for (const [id, uses] of Object.entries(u[kind]).sort(([x], [y]) => x.localeCompare(y))) {
        const primary = named.some((n) => id === n || id.toLowerCase().endsWith(`.${n.toLowerCase()}`));
        apis.push({ id, kind: kind === 'types' ? 'type' : memberKind(uses), ...(primary ? { primary: true } : {}),
          uses: uses.map((x) => ({ file: posix(relative(s.dir, join(root, x.file))), line: x.line, ...(x.command ? { command: x.command } : {}) })) });
      }
    apisOf.set(s.meta.id, apis);
  }

  const byApi = {}, kindOf = {};
  for (const s of samples)
    for (const api of apisOf.get(s.meta.id)) {
      kindOf[api.id] ??= api.kind;
      (byApi[api.id] ??= []).push({ sample: s.meta.id, ...(api.primary ? { primary: true } : {}),
        file: `${s.path}/${api.uses[0].file}`, line: api.uses[0].line, uses: api.uses.length, _focus: apisOf.get(s.meta.id).length });
    }
  for (const id of Object.keys(byApi))
    byApi[id] = byApi[id].sort((x, y) => (y.primary ? 1 : 0) - (x.primary ? 1 : 0) || x._focus - y._focus || x.sample.localeCompare(y.sample))
      .map(({ _focus, ...e }) => e);

  const vocab = (axis) => schema.properties[axis].enum ?? schema.properties[axis].items.enum;
  const index = {
    schemaVersion: 2,
    ...(repoUrl && commit ? { source: { repo: repoUrl, commit } } : {}),
    vocabulary: { domain: vocab('domain'), task: vocab('task'), surface: vocab('surface') },
    samples: samples.map(({ path, meta: m }) => ({
      id: m.id, title: m.title, summary: m.summary, category: m.category, domain: m.domain, task: m.task, surface: m.surface,
      ...(m.interactive ? { interactive: true } : {}), ...(m.requiresRestart ? { requiresRestart: true } : {}),
      path, digest: digestOf(join(root, path)), phrasings: m.phrasings ?? [], commands: m.commands ?? [], apis: apisOf.get(m.id),
    })),
    apis: Object.fromEntries(Object.entries(byApi).sort(([x], [y]) => x.localeCompare(y)).map(([id, l]) => [id, { kind: kindOf[id], samples: l }])),
  };
  problems.push(...validate(readJson(join(root, 'schema', 'index.schema.json')), index, 'index.json', []));

  const llms = [
    '# Altium Designer SDK samples (C#)', '',
    `> ${list.length} complete Altium Designer extensions in C#, each with a shim, an implementation and installation`,
    '> files, compiled against the Altium Designer SDK assemblies (Altium.SDK.dll, Altium.SDK.Interfaces.dll). Copy one',
    '> to start an extension; the repository README says which Altium Designer version they are built for.', '',
    'Facets per sample: domain (where in Altium Designer), task (what it does), surface (how it hooks in). index.json',
    'has the same data, every SDK type and member each sample uses (file and line), and the reverse index.', '',
    ...groups.flatMap(([title, l]) => [`## ${title}`, '', ...l.map(({ path, meta: m }) => {
      const primary = apisOf.get(m.id).filter((x) => x.primary).map((x) => x.id);
      return `- [${m.id}](${path}/README.md): ${m.summary} [${m.domain}; ${m.task.join(', ')}; ${m.surface.join(', ')}]` +
        (primary.length ? ` Shows: ${primary.slice(0, 8).join(', ')}${primary.length > 8 ? `, +${primary.length - 8} more` : ''}` : '');
    }), '']),
  ].join('\n');

  mkdirSync(OUT, { recursive: true });
  writeFileSync(join(OUT, 'index.json'), JSON.stringify(index) + '\n');
  writeFileSync(join(OUT, 'llms.txt'), llms + '\n');
  cpSchema(OUT);
  console.log(`build-index: ${Object.keys(byApi).length} SDK types and members, ` +
    `${[...apisOf.values()].reduce((n, l) => n + l.filter((x) => x.primary).length, 0)} primary uses -> ${OUT}`);
}
// A member's kind, from the uses eng/ApiUsage recorded (method, property, field, enumValue, event). Uses of one id
// normally agree; if they do not, a property wins over a method (an accessor and the property are one entry).
function memberKind(uses) {
  const kinds = new Set(uses.map((u) => u.kind).filter(Boolean));
  for (const k of ['property', 'event', 'enumValue', 'field', 'method']) if (kinds.has(k)) return k;
  return 'method';
}
function cpSchema(out) {
  mkdirSync(join(out, 'schema'), { recursive: true });
  for (const f of ['sample.schema.json', 'index.schema.json']) writeFileSync(join(out, 'schema', f), readFileSync(join(root, 'schema', f)));
}

console.log(`build-index: ${list.length} samples${CHECK ? ' (check only)' : ''}`);
if (problems.length) { console.error(`build-index: ${problems.length} problem(s):\n  ${problems.slice(0, 80).join('\n  ')}`); process.exitCode = 1; }
