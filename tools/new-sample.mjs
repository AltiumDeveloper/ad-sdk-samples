#!/usr/bin/env node
// Creates a sample folder from framework/templates (the monorepo form of `dotnet new ad-extension`).
//
//   node tools/new-sample.mjs --category pcb --name BoardUniqueId \
//        --command "GetUniqueId|Show the board's unique ID" --command "SetUniqueId|Set the board's unique ID|UniqueId=<text>" \
//        [--menus pcb,sch,noDocument] [--panel] [--outputer] [--description "..."] [--force]
//
// --command "Name|Description[|Params]"  one menu item per command (Params are appended to Cmd=Name as |Params).
// Writes samples/<category>/<Name>/{<Name>/, <Name>.Impl/, README.md, sample.json}. Never overwrites unless --force.
// Does not touch AdSdkSamples.slnx: run `node tools/sync-solution.mjs` after it.
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const opt = (name) => { const i = args.indexOf(`--${name}`); return i >= 0 ? args[i + 1] : undefined; };
const all = (name) => args.flatMap((a, i) => (a === `--${name}` ? [args[i + 1]] : []));
const flag = (name) => args.includes(`--${name}`);

const category = opt('category');
const name = opt('name');
if (!category || !name) { console.error('usage: --category <slug> --name <Name> [--command "Name|Description[|Params]"]...'); process.exit(2); }
if (!/^[A-Z][A-Za-z0-9]*$/.test(name)) { console.error(`name '${name}': PascalCase letters and digits only (it becomes AD's server, process and menu IDs)`); process.exit(2); }
if (!/^[a-z0-9-]+$/.test(category)) { console.error(`category '${category}': lower-case slug`); process.exit(2); }

const panel = flag('panel');
const outputer = flag('outputer');
const menus = (opt('menus') ?? 'noDocument,sch,pcb').split(',').map((s) => s.trim()).filter(Boolean);
const commands = all('command').map((spec) => {
  const [cmd, description = '', params = ''] = spec.split('|');
  if (!/^[A-Za-z][A-Za-z0-9.]*$/.test(cmd)) { console.error(`command '${cmd}': letters, digits and dots`); process.exit(2); }
  return { cmd, description, params };
});
const description = opt('description') ?? `${name}: Altium Designer SDK sample`;

const dir = join(root, 'samples', category, name);
const force = flag('force');
const written = [];
function write(rel, text) {
  const file = join(dir, rel);
  if (existsSync(file) && !force) { console.log(`  kept    ${rel}`); return; }
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, text.replace(/\r?\n/g, '\r\n'));
  written.push(rel);
  console.log(`  wrote   ${rel}`);
}

// Minimal mustache: {{Key}}, {{#section}}...{{/section}} (boolean or list), {{#menu.x}} for menus.
function render(template, ctx) {
  let out = template.replace(/\{\{#([\w.]+)\}\}\r?\n?([\s\S]*?)\{\{\/\1\}\}\r?\n?/g, (_, key, body) => {
    const value = key.startsWith('menu.') ? menus.includes(key.slice(5)) : ctx[key];
    if (Array.isArray(value)) return value.map((item) => render(body, { ...ctx, ...item })).join('');
    return value ? render(body, ctx) : '';
  });
  return out.replace(/\{\{(\w+)\}\}/g, (_, key) => (ctx[key] ?? ''));
}
const tpl = (f) => readFileSync(join(root, 'framework', 'templates', f), 'utf8');
const esc = (s) => s.replace(/'/g, "''");
const today = new Date();
const ctx = {
  Name: name,
  Description: esc(description),
  Date: `${today.getMonth() + 1}/${today.getDate()}/${today.getFullYear()}`,
  MenuCaption: `&${name}`,
  panel,
  commands: commands.map((c) => ({
    Command: c.cmd,
    Caption: '&' + c.cmd.replace(/([a-z0-9])([A-Z])/g, '$1 $2'),
    CommandDescription: esc(c.description),
    Params: c.params ? `|${c.params}` : '',
  })),
};

console.log(`samples/${category}/${name}`);
write(`${name}/${name}.csproj`, `<Project Sdk="Microsoft.NET.Sdk">
  <!-- The shim: what Altium Designer loads. Shared sources and settings: Directory.Build.targets. -->
  <PropertyGroup>
    <SampleRole>Shim</SampleRole>
    <!-- Identifies the extension in Extensions\\ExtensionsRegistry.xml (tools\\deploy.ps1). Generated once; keep it. -->
    <ExtensionGuid>${randomUUID()}</ExtensionGuid>
  </PropertyGroup>
</Project>
`);
write(`${name}/Installation/${name}.ins`, render(tpl('Sample.ins'), ctx));
write(`${name}/Installation/${name}.rcs`, render(tpl('Sample.rcs'), ctx));
if (outputer) write(`${name}/Installation/${name}.out`, render(tpl('Sample.out'), ctx));

write(`${name}.Impl/${name}.Impl.csproj`, `<Project Sdk="Microsoft.NET.Sdk">
  <!-- The sample's code: [HotCommand] methods. Shared framework sources: Directory.Build.targets. -->
  <PropertyGroup>
    <SampleRole>Impl</SampleRole>
  </PropertyGroup>
</Project>
`);
const stubs = commands.map((c) => `        [HotCommand("${c.cmd}", "${c.description.replace(/"/g, '\\"')}")]
        public static string ${c.cmd.replace(/\./g, '_')}(CommandContext ctx)
        {
            throw new UserException("Not implemented yet.");
        }
`).join('\n');
write(`${name}.Impl/Commands/${name}Commands.cs`, `namespace AdSamples.${name}
{
    internal static class ${name}Commands
    {
${stubs}    }
}
`);
if (panel) write(`${name}.Impl/Ui/PanelContent.cs`, `using System.Windows.Forms;

namespace AdSamples
{
    internal static partial class SamplePanel
    {
        internal static partial Control CreateContent() => new ${name}.Ui.PanelContent();
    }
}

namespace AdSamples.${name}.Ui
{
    /// <summary>The panel's content. Rebuilt on every hot reload; keep state that must survive it outside this control.</summary>
    internal sealed class PanelContent : UserControl
    {
        public PanelContent()
        {
            BackColor = DXP.DxpThemeManager.ColorPanelBody;
            Controls.Add(new Label { Dock = DockStyle.Fill, Text = "${name}", ForeColor = DXP.DxpThemeManager.ColorPanelFont });
        }
    }
}
`);
if (outputer) write(`${name}.Impl/Commands/ReportCommand.cs`, `namespace AdSamples.${name}
{
    internal static class ReportCommand
    {
        /// <summary>Called by the OutJob outputer (framework\\shim\\ReportOutput.cs) with File=&lt;path&gt;; env Context = the project.</summary>
        [HotCommand("Report", "Write the OutJob report")]
        public static string Report(CommandContext ctx)
        {
            throw new UserException("Not implemented yet.");
        }
    }
}
`);

write('README.md', `# ${name}

One paragraph: what the sample does and what a developer learns from it.

## What it shows
- `NS.Type.Member`: what it is for here.

## Run it
1. Open ...
2. **${name} › ...**, or `tools/ad.ps1 -Sample ${name} run <Command> "<Key>=<Value>"`

| Command | Parameters | Result |
|---|---|---|

## How it works

## Notes
`);
if (!existsSync(join(dir, 'sample.json'))) {
  // Fill in title, summary, domain, task, surface and phrasings (schema/sample.schema.json), then run
  // node eng/build-index.mjs: it checks the file and adds the sample to the README table.
  write('sample.json', JSON.stringify({ $schema: '../../../schema/sample.schema.json', schemaVersion: 1, id: name,
    title: '', summary: '', category, domain: '', task: [], surface: ['command'], phrasings: [],
    commands: commands.map((c) => ({ name: c.cmd, description: c.description, ...(c.params ? { params: c.params } : {}) })) }, null, 2) + '\n');
}
console.log(written.length ? `done (${written.length} files)` : 'nothing written');
