// ApiUsage: which SDK types and members does a sample's own code use, resolved by the compiler (not by regex).
//
// For each sample folder (samples/<category>/<Id>), for its Impl and shim projects:
//   1. asks MSBuild for the project's Compile items and resolved references (the project must restore/build),
//   2. compiles them in memory with Roslyn,
//   3. walks the sample's own files (framework/ and obj/ are skipped) and records every symbol that lives in an
//      Altium.SDK* assembly, with file, line and the [HotCommand] it belongs to.
// Extension methods of the generated *Helper classes (GetState_X(this ISch_Wire ...)) are attributed to the
// interface they extend, and GetState_/SetState_ accessors to the property name, as the SDK reference shows them.
//
// Output (stdout or --out): { "samples": { "<Id>": { "types": {...}, "members": {...}, "errors": [...] } } }
// where types/members map "NS.Type[.Member]" -> [{ file, line, command, symbol }].
using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var argv = args.ToList();
string? outFile = null;
var o = argv.IndexOf("--out");
if (o >= 0) { outFile = argv[o + 1]; argv.RemoveRange(o, 2); }
if (argv.Count == 0) { Console.Error.WriteLine("usage: ApiUsage <sampleDir>... [--out file.json]"); return 2; }

var repoRoot = FindRepoRoot(Path.GetFullPath(argv[0]));
var result = new SortedDictionary<string, object>(StringComparer.Ordinal);
var exit = 0;
foreach (var dirArg in argv)
{
    var dir = Path.GetFullPath(dirArg);
    var id = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
    var usage = new SampleUsage();
    foreach (var project in new[] { Path.Combine(dir, $"{id}.Impl", $"{id}.Impl.csproj"), Path.Combine(dir, id, $"{id}.csproj") })
    {
        if (!File.Exists(project)) continue;
        try { Analyze(project, dir, repoRoot, usage); }
        catch (Exception ex) { usage.Errors.Add($"{Path.GetFileName(project)}: {ex.Message}"); exit = 1; }
    }
    result[id] = usage.ToJson();
}

var json = JsonSerializer.Serialize(new { generated = "eng/ApiUsage", samples = result },
    new JsonSerializerOptions { WriteIndented = true });
if (outFile != null) File.WriteAllText(outFile, json + "\n"); else Console.WriteLine(json);
return exit;

static void Analyze(string project, string sampleDir, string repoRoot, SampleUsage usage)
{
    var props = MsBuildItems(project);
    var sources = props.Compile.Select(Path.GetFullPath).Where(File.Exists).ToList();
    var trees = sources.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f),
        new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: props.Defines), path: f)).ToList();
    var refs = props.References.Where(File.Exists).Select(r => MetadataReference.CreateFromFile(r)).ToList();
    var compilation = CSharpCompilation.Create("ApiUsage", trees, refs,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(5).ToList();
    foreach (var e in errors) usage.Errors.Add(e.ToString());

    foreach (var tree in trees)
    {
        var file = tree.FilePath;
        var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
        // Only the sample's own code: not the shared framework, not generated files.
        if (!file.StartsWith(sampleDir, StringComparison.OrdinalIgnoreCase) || rel.Contains("/obj/")) continue;
        var model = compilation.GetSemanticModel(tree);
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            ISymbol? symbol = node switch
            {
                IdentifierNameSyntax or GenericNameSyntax => model.GetSymbolInfo(node).Symbol ?? model.GetSymbolInfo(node).CandidateSymbols.FirstOrDefault(),
                _ => null,
            };
            if (symbol == null) continue;
            // Avoid double counting: for a.B record only the B identifier (the member), types via their own names.
            var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
            usage.Record(symbol, rel, line, EnclosingCommand(node, model));
        }
    }
}

static string? EnclosingCommand(SyntaxNode node, SemanticModel model)
{
    var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
    if (method == null) return null;
    foreach (var attr in method.AttributeLists.SelectMany(l => l.Attributes))
        if (attr.Name.ToString().EndsWith("HotCommand") && attr.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax lit)
            return lit.Token.ValueText;
    return method.Identifier.ValueText; // a helper: the method name, aggregation maps helpers to commands if needed
}

static (List<string> Compile, List<string> References, List<string> Defines) MsBuildItems(string project)
{
    // Restore + resolve references (no compile), then read the items as JSON.
    var psi = new ProcessStartInfo("dotnet", $"msbuild \"{project}\" -nologo -restore -t:ResolveReferences -p:Configuration=Debug -getItem:Compile -getItem:ReferencePath -getProperty:DefineConstants")
    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    using var p = Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEnd();
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0) throw new InvalidOperationException($"msbuild failed: {stderr}{Tail(stdout)}");
    var start = stdout.IndexOf('{');
    using var doc = JsonDocument.Parse(stdout[start..]);
    var items = doc.RootElement.GetProperty("Items");
    var dir = Path.GetDirectoryName(project)!;
    List<string> Paths(string name) => items.TryGetProperty(name, out var arr)
        ? arr.EnumerateArray().Select(i => i.GetProperty("FullPath").GetString()!).ToList() : new();
    var defines = doc.RootElement.GetProperty("Properties").GetProperty("DefineConstants").GetString()!
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    return (Paths("Compile"), Paths("ReferencePath"), defines);
}

static string Tail(string s) => s.Length > 2000 ? s[^2000..] : s;

static string FindRepoRoot(string start)
{
    for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "AdSdkSamples.slnx"))) return d.FullName;
    throw new InvalidOperationException("AdSdkSamples.slnx not found above " + start);
}

sealed class SampleUsage
{
    public readonly SortedDictionary<string, List<object>> Types = new(StringComparer.Ordinal);
    public readonly SortedDictionary<string, List<object>> Members = new(StringComparer.Ordinal);
    public readonly List<string> Errors = new();
    private readonly HashSet<string> _seen = new();

    static bool IsSdk(IAssemblySymbol? a) => a != null && a.Name.StartsWith("Altium.SDK", StringComparison.OrdinalIgnoreCase);

    static string TypeId(ITypeSymbol t)
    {
        var def = t.OriginalDefinition;
        var ns = def.ContainingNamespace?.ToDisplayString() ?? "";
        var name = def.ContainingType != null ? $"{def.ContainingType.Name}.{def.Name}" : def.Name;
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    static readonly string[] AccessorPrefixes = { "GetState_", "SetState_", "GetProperty_", "SetProperty_", "Get_", "Set_" };

    public void Record(ISymbol symbol, string file, int line, string? command)
    {
        switch (symbol)
        {
            case ITypeSymbol type when IsSdk(type.ContainingAssembly):
                Add(Types, TypeId(type), file, line, command, type.Name);
                break;
            case IMethodSymbol method when IsSdk(method.ContainingAssembly):
            {
                var m = method.ReducedFrom ?? method;
                var owner = m.IsExtensionMethod && m.Parameters.Length > 0 ? m.Parameters[0].Type : m.ContainingType;
                var name = m.Name;
                var prefix = AccessorPrefixes.FirstOrDefault(p => name.StartsWith(p, StringComparison.Ordinal));
                var member = prefix != null ? name[prefix.Length..] : name;
                // An accessor pair (GetState_X / SetState_X) is the property X, as the SDK reference names it.
                Add(Members, $"{TypeId(owner)}.{member}", file, line, command, name, prefix != null ? "property" : "method");
                Add(Types, TypeId(owner), file, line, command, owner.Name);
                break;
            }
            case IPropertySymbol or IFieldSymbol or IEventSymbol when IsSdk(symbol.ContainingAssembly):
            {
                var owner = symbol.ContainingType;
                var kind = symbol switch
                {
                    IFieldSymbol when owner.TypeKind == TypeKind.Enum => "enumValue",
                    IFieldSymbol => "field",
                    IEventSymbol => "event",
                    _ => "property",
                };
                Add(Members, $"{TypeId(owner)}.{symbol.Name}", file, line, command, symbol.Name, kind);
                Add(Types, TypeId(owner), file, line, command, owner.Name);
                break;
            }
        }
    }

    void Add(SortedDictionary<string, List<object>> map, string key, string file, int line, string? command, string symbol, string? kind = null)
    {
        if (!_seen.Add($"{key}|{file}|{line}")) return;
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<object>();
        list.Add(kind == null ? new { file, line, command, symbol } : new { file, line, command, symbol, kind });
    }

    public object ToJson() => new { types = Types, members = Members, errors = Errors };
}
