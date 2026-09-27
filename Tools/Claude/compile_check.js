// Compiles Assembly-CSharp outside the editor with Unity's bundled Roslyn, to catch C# errors
// without waiting for a domain reload. Does not run NGO IL post-processing.
// Usage: node Tools/Claude/compile_check.js [-w]   (-w also prints warnings)
// Override the editor location with UNITY_EDITOR_DATA=<...>\Editor\Data if Unity is not in the default Hub path.
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');

const projectRoot = path.resolve(__dirname, '..', '..');
const version = (/m_EditorVersion:\s*(\S+)/.exec(fs.readFileSync(path.join(projectRoot, 'ProjectSettings', 'ProjectVersion.txt'), 'utf8')) || [])[1];
const unityData = process.env.UNITY_EDITOR_DATA || `C:\\Program Files\\Unity\\Hub\\Editor\\${version}\\Editor\\Data`;
const csprojPath = path.join(projectRoot, 'Assembly-CSharp.csproj');
if (!fs.existsSync(csprojPath)) { console.error('Assembly-CSharp.csproj missing: open the project in Unity once (or Assets > Open C# Project).'); process.exit(2); }
const csproj = fs.readFileSync(csprojPath, 'utf8');
const outDir = path.join(os.tmpdir(), 'defrag-compile-check');
fs.mkdirSync(outDir, { recursive: true });

const xml = s => s.replace(/&amp;/g, '&').replace(/&apos;/g, "'").replace(/&quot;/g, '"').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
const compileFiles = [...csproj.matchAll(/<Compile Include="([^"]+)"/g)].map(m => xml(m[1]));
// Pick up newly added scripts that the (possibly stale) csproj does not list yet.
const listed = new Set(compileFiles.map(f => path.resolve(projectRoot, f).toLowerCase()));
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (/[\\/](Editor|Plugins)$/i.test(p)) continue;
      walk(p);
    } else if (e.name.endsWith('.cs') && !listed.has(p.toLowerCase())) {
      compileFiles.push(path.relative(projectRoot, p));
    }
  }
})(path.join(projectRoot, 'Assets', 'Scripts'));

const refs = [...csproj.matchAll(/<HintPath>([^<]+)<\/HintPath>/g)].map(m => xml(m[1]));
const projectRefs = [...csproj.matchAll(/<ProjectReference Include="([^"]+)"/g)]
  .map(m => path.join(projectRoot, 'Library', 'ScriptAssemblies', path.basename(m[1], '.csproj') + '.dll'))
  .filter(p => fs.existsSync(p));
const defines = (/<DefineConstants>([^<]+)<\/DefineConstants>/.exec(csproj) || [, ''])[1];
const langVersion = (/<LangVersion>([^<]+)<\/LangVersion>/.exec(csproj) || [, '9.0'])[1];

const rsp = [
  '-nologo', '-target:library', '-nostdlib+', '-unsafe+', '-nowarn:0169,0414,0649,0618,0067,0162,0168,0219,0105',
  `-langversion:${langVersion}`, `-define:${defines}`,
  `-out:${path.join(outDir, 'Assembly-CSharp.check.dll')}`,
  ...[...new Set([...refs, ...projectRefs])].map(r => `-r:"${path.isAbsolute(r) ? r : path.join(projectRoot, r)}"`),
  ...compileFiles.map(f => `"${path.join(projectRoot, f)}"`),
].join('\n');
const rspPath = path.join(outDir, 'compile.rsp');
fs.writeFileSync(rspPath, rsp);

const result = spawnSync(path.join(unityData, 'NetCoreRuntime', 'dotnet.exe'),
  [path.join(unityData, 'DotNetSdkRoslyn', 'csc.dll'), `@${rspPath}`], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
if (result.error) { console.error(`Could not run Unity's compiler from ${unityData}: ${result.error.message}`); process.exit(2); }
const out = (result.stdout || '') + (result.stderr || '');
const isError = l => /(^|\s)error CS\d+/.test(l);
const lines = out.split(/\r?\n/).filter(l => isError(l) || (process.argv[2] === '-w' && / warning /.test(l)));
console.log(lines.slice(0, 60).join('\n'));
console.log(`files=${compileFiles.length} refs=${refs.length + projectRefs.length} errors=${out.split(/\r?\n/).filter(isError).length} exit=${result.status}`);
