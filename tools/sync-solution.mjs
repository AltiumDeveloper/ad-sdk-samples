#!/usr/bin/env node
// Regenerates AdSdkSamples.slnx from the folders: every samples/<category>/<Name>/ (shim + Impl).
// Deterministic: run it after adding a sample.
//   node tools/sync-solution.mjs [--check]   (--check: exit 1 if the file is out of date)
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const dirs = (p) => (existsSync(p) ? readdirSync(p).filter((d) => statSync(join(p, d)).isDirectory()).sort() : []);
const rel = (p) => relative(root, p).split('\\').join('/');

function projectsOf(sampleDir, name) {
  return [join(sampleDir, name, `${name}.csproj`), join(sampleDir, `${name}.Impl`, `${name}.Impl.csproj`)].filter(existsSync);
}

const folders = [];
for (const category of dirs(join(root, 'samples'))) {
  const projects = dirs(join(root, 'samples', category)).flatMap((name) => projectsOf(join(root, 'samples', category, name), name));
  if (projects.length) folders.push({ name: `/samples/${category}/`, projects });
}

const text = ['<Solution>',
  ...folders.map((f) => [`  <Folder Name="${f.name}">`, ...f.projects.map((p) => `    <Project Path="${rel(p)}" />`), '  </Folder>'].join('\n')),
  '</Solution>', ''].join('\n');

const file = join(root, 'AdSdkSamples.slnx');
const current = existsSync(file) ? readFileSync(file, 'utf8') : '';
if (args().includes('--check')) {
  if (current !== text) { console.error('AdSdkSamples.slnx is out of date: run node tools/sync-solution.mjs'); process.exit(1); }
  process.exit(0);
}
if (current !== text) writeFileSync(file, text);
console.log(`AdSdkSamples.slnx: ${folders.reduce((n, f) => n + f.projects.length, 0)} projects`);
function args() { return process.argv.slice(2); }
