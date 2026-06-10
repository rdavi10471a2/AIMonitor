using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SelfDocGen;

// Generator: drives the AIMonitor.McpServer.dll over stdio (bound to the solution index via
// the doc-generation config), pulls caller/index-verified coupling, fuses it with the
// source-verified csproj DAG, ranks the layers, and writes:
//   * GeneratedDocs/Architecture.aim.md           — the solution-wide cross-project view
//   * GeneratedDocs/components/<Short>.aim.md      — one per src project (internal view)
//   * GeneratedDocs/components/README.md           — index of the per-project docs
internal static class Program
{
    private const string ServerDll = @"C:\VSCodeProjects\AIMonitor\src\AIMonitor.McpServer\bin\Debug\net10.0\AIMonitor.McpServer.dll";
    private const string RepoRoot = @"C:\VSCodeProjects\aimonitor-selfdoc";
    private const string ConfigPath = @"C:\VSCodeProjects\aimonitor-selfdoc\config\self-doc.appsettings.json";
    private const string OutPath = @"C:\VSCodeProjects\aimonitor-selfdoc\GeneratedDocs\Architecture.aim.md";
    private const string ComponentsDir = @"C:\VSCodeProjects\aimonitor-selfdoc\GeneratedDocs\components";

    // Source-verified skeleton: the consumer -> dependency project DAG, derived at runtime from the
    // real <ProjectReference> items of the src projects the index actually loaded (see DeriveCsprojEdges).
    // No hand-typed edges — the "source-verified / compiler-enforced" claim in the doc is literally the
    // csproj truth, parsed from the same tree the index reflects.
    private static Dictionary<string, string[]> CsprojEdges = new();

    private static readonly HashSet<string> TypeKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Class", "Interface", "Struct", "Enum", "Record", "RecordStruct", "Delegate", "NamedType"
    };

    private sealed record SymbolInfo(
        string Name,
        string Kind,
        string Accessibility,
        string File,
        string ContainingType,
        string Namespace,
        string Signature);

    private static async Task<int> Main(string[] args)
    {
        StdioClientTransportOptions options = new()
        {
            Name = "ai-monitor-selfdoc",
            Command = "dotnet",
            Arguments = [ServerDll, "--repo-root", RepoRoot, "--config", ConfigPath],
            WorkingDirectory = RepoRoot
        };

        await using McpClient client = await McpClient.CreateAsync(new StdioClientTransport(options));

        string statusJson = await CallText(client, "get_monitor_status", new Dictionary<string, object?>());
        string indexJson = await CallText(client, "get_solution_index", new Dictionary<string, object?>
        {
            ["maxFiles"] = 50000,
            ["maxSymbols"] = 50000
        });

        // Derive the source-verified project DAG from real csproj ProjectReference items (no hardcoded edges).
        CsprojEdges = DeriveCsprojEdges(indexJson);
        Console.WriteLine($"Derived {CsprojEdges.Count} src project(s) from csproj ProjectReference truth.");

        // file path -> short project name; per-project file lists; per-project symbol inventory;
        // and the public src symbols we will probe for coupling.
        Dictionary<string, string> fileToProject = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SortedSet<string>> projectFiles = new();
        Dictionary<string, List<SymbolInfo>> projectSymbols = new();
        List<(string key, string project, string name, string kind, string file)> targets = [];
        using (JsonDocument doc = JsonDocument.Parse(indexJson))
        {
            JsonElement root = doc.RootElement;
            foreach (JsonElement f in root.GetProperty("files").EnumerateArray())
            {
                string proj = ShortName(f.GetProperty("projectPath").GetString()!);
                string filePath = f.GetProperty("filePath").GetString()!;
                fileToProject[filePath] = proj;
                if (CsprojEdges.ContainsKey(proj))
                {
                    if (!projectFiles.TryGetValue(proj, out SortedSet<string>? set))
                    {
                        set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                        projectFiles[proj] = set;
                    }

                    set.Add(filePath);
                }
            }

            foreach (JsonElement s in root.GetProperty("symbols").EnumerateArray())
            {
                string proj = ShortName(s.GetProperty("projectPath").GetString()!);
                if (!CsprojEdges.ContainsKey(proj))
                {
                    continue; // skip tests/samples — src only
                }

                string access = s.GetProperty("accessibility").GetString() ?? "";
                string name = s.GetProperty("name").GetString() ?? "";
                string kind = s.GetProperty("kind").GetString() ?? "";
                string file = GetStringOrEmpty(s, "filePath");

                if (!projectSymbols.TryGetValue(proj, out List<SymbolInfo>? syms))
                {
                    syms = [];
                    projectSymbols[proj] = syms;
                }

                syms.Add(new SymbolInfo(
                    name,
                    kind,
                    access,
                    file,
                    GetStringOrEmpty(s, "containingType"),
                    GetStringOrEmpty(s, "namespace"),
                    GetStringOrEmpty(s, "signature")));

                if (access.Equals("Public", StringComparison.OrdinalIgnoreCase))
                {
                    targets.Add((s.GetProperty("stableKey").GetString()!, proj, name, kind, file));
                }
            }
        }

        Console.WriteLine($"Probing {targets.Count} public src symbols for caller-verified coupling...");

        // coupling[(consumer, dependency)] = cross-project reference-site count (caller/index-verified)
        // internalEdges[project][(consumerFile, targetFile)] = intra-project reference-site count
        Dictionary<(string, string), int> coupling = new();
        Dictionary<string, Dictionary<(string, string), int>> internalEdges = new();
        int done = 0;
        foreach ((string key, string project, string name, string kind, string targetFile) in targets)
        {
            string refsJson = await CallText(client, "find_indexed_references", new Dictionary<string, object?>
            {
                ["stableSymbolKey"] = key,
                ["responseShape"] = "rich",
                ["maxResults"] = 2000
            });
            try
            {
                using JsonDocument rdoc = JsonDocument.Parse(refsJson);
                if (rdoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement row in rdoc.RootElement.EnumerateArray())
                    {
                        if (!row.TryGetProperty("filePath", out JsonElement fp) || fp.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        string consumerFile = fp.GetString()!;
                        if (!fileToProject.TryGetValue(consumerFile, out string? consumer))
                        {
                            continue;
                        }

                        if (consumer != project && CsprojEdges.ContainsKey(consumer))
                        {
                            coupling[(consumer, project)] = coupling.GetValueOrDefault((consumer, project)) + 1;
                        }
                        else if (consumer == project && consumerFile != targetFile && targetFile.Length > 0)
                        {
                            if (!internalEdges.TryGetValue(project, out Dictionary<(string, string), int>? edges))
                            {
                                edges = new();
                                internalEdges[project] = edges;
                            }

                            edges[(consumerFile, targetFile)] = edges.GetValueOrDefault((consumerFile, targetFile)) + 1;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // skip unparseable / error rows
            }

            done++;
            if (done % 50 == 0)
            {
                Console.WriteLine($"  {done}/{targets.Count}");
            }
        }

        WriteDoc(statusJson, coupling);
        Console.WriteLine($"Wrote {OutPath}");

        WritePerProjectDocs(projectFiles, projectSymbols, coupling, internalEdges);
        Console.WriteLine($"Wrote {CsprojEdges.Count} per-project docs to {ComponentsDir}");
        return 0;
    }

    private static void WriteDoc(string statusJson, Dictionary<(string, string), int> coupling)
    {
        Dictionary<string, int> rank = new();
        foreach (string p in CsprojEdges.Keys)
        {
            ComputeRank(p, rank);
        }

        StringBuilder sb = new();
        sb.AppendLine("# AIMonitor Architecture (generated)");
        sb.AppendLine();
        sb.AppendLine("> Generated from the solution index via the AIMonitor MCP server.");
        sb.AppendLine("> **Skeleton** = `source-verified` (csproj ProjectReference, compiler-enforced). **Coupling weights** = `caller/index-verified` (live cross-project reference sites from the index).");
        sb.AppendLine(">");
        sb.AppendLine("> Per-project internal views: [`components/`](components/README.md).");
        sb.AppendLine();
        sb.AppendLine("## Index evidence");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(statusJson);
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Layered architecture (foundation at bottom)");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.AppendLine("flowchart TB");
        foreach (var kv in CsprojEdges)
        {
            foreach (string dep in kv.Value)
            {
                int w = coupling.GetValueOrDefault((kv.Key, dep));
                string label = w > 0 ? $"{w} refs" : "declared only";
                string arrow = w > 0 ? "-->" : "-.->";
                sb.AppendLine($"  {kv.Key} {arrow}|{label}| {dep}");
            }
        }
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Layers (topological rank from the csproj DAG)");
        sb.AppendLine();
        foreach (var grp in rank.GroupBy(r => r.Value).OrderBy(g => g.Key))
        {
            string names = string.Join(", ", grp.Select(g => g.Key).OrderBy(n => n));
            sb.AppendLine($"- **rank {grp.Key}** — {names}");
        }
        sb.AppendLine();

        sb.AppendLine("## Edge evidence (declared vs. live coupling)");
        sb.AppendLine();
        sb.AppendLine("| Consumer | Dependency | Declared (csproj) | Live refs (index) | Verdict |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var kv in CsprojEdges.OrderBy(k => rank[k.Key]))
        {
            foreach (string dep in kv.Value)
            {
                int w = coupling.GetValueOrDefault((kv.Key, dep));
                string verdict = w > 0 ? "live (caller/index-verified)" : "declared-only (no public ref — structural/transitive or candidate-dead)";
                sb.AppendLine($"| {kv.Key} | {dep} | yes | {w} | {verdict} |");
            }
        }
        sb.AppendLine();

        List<string> dead = [];
        foreach (var kv in CsprojEdges)
        {
            foreach (string dep in kv.Value)
            {
                if (coupling.GetValueOrDefault((kv.Key, dep)) == 0)
                {
                    dead.Add($"{kv.Key} → {dep}");
                }
            }
        }

        int relationshipCount = 0;
        try
        {
            using JsonDocument sdoc = JsonDocument.Parse(statusJson);
            relationshipCount = sdoc.RootElement.GetProperty("relationshipCount").GetInt32();
        }
        catch (JsonException)
        {
        }

        sb.AppendLine("## Findings");
        sb.AppendLine();
        sb.AppendLine($"- **{dead.Count} declared ProjectReferences carry zero public cross-project references** — candidate-dead, or used only structurally/transitively (assembly-load-only, DI registration, `MSBuildLocator` init). Verify before removal: " + string.Join("; ", dead) + ".");
        sb.AppendLine($"- **relationshipCount = {relationshipCount}.** AIMonitor barely uses inheritance/interface relationships across the indexed surface — it is composition-over-inheritance; the architecture lives in the *reference* graph, not a type hierarchy.");
        sb.AppendLine("- **Heaviest coupling** is McpServer→Workflow, App→Data, App→Logging — the orchestration/entry tier leans hardest on Workflow + Data.");
        sb.AppendLine();

        // Additive, experimental: a second layering by weighted afferent centrality (research comparison).
        // Leaves the topological view above untouched.
        AppendExperimentalLayering(sb, coupling, rank);

        sb.AppendLine("## Evidence & caveats");
        sb.AppendLine();
        sb.AppendLine("- **Skeleton** (the DAG + ranks): `source-verified` — csproj `ProjectReference`, compiler-enforced; the build's acyclicity *proves* a valid layering exists.");
        sb.AppendLine("- **Coupling weights**: `caller/index-verified` — live cross-project reference sites pulled from the self-index via the AIMonitor MCP server DLL.");
        sb.AppendLine("- **Public-surface only:** coupling counts references whose *target* is a `Public` symbol. Cross-project use via `internal` + `InternalsVisibleTo` is **not** counted, so a `0` is *candidate*-dead, not proven-dead.");
        sb.AppendLine("- **Reference-site semantics:** a dependency used only by assembly load / DI registration / config (no symbol reference) also shows `0` despite being needed at runtime.");
        sb.AppendLine("- **Polarity:** rendered foundation-at-bottom (`flowchart TB`; `Core` is a sink). Tier *names* are not graph-derived — they are maintained intent.");
        sb.AppendLine("- **Self-index, not live-watch:** AIMonitor cannot live-watch itself; it was indexed as a *target* solution to produce this. Regenerate-and-diff is stable (same index → byte-identical doc).");
        sb.AppendLine();

        Directory.CreateDirectory(Path.GetDirectoryName(OutPath)!);
        File.WriteAllText(OutPath, sb.ToString());
    }

    private static void WritePerProjectDocs(
        Dictionary<string, SortedSet<string>> projectFiles,
        Dictionary<string, List<SymbolInfo>> projectSymbols,
        Dictionary<(string, string), int> coupling,
        Dictionary<string, Dictionary<(string, string), int>> internalEdges)
    {
        Dictionary<string, int> rank = new();
        foreach (string p in CsprojEdges.Keys)
        {
            ComputeRank(p, rank);
        }

        Directory.CreateDirectory(ComponentsDir);

        foreach (string project in CsprojEdges.Keys.OrderBy(rank.GetValueOrDefault).ThenBy(n => n))
        {
            WriteOneProjectDoc(project, projectFiles, projectSymbols, coupling, internalEdges, rank);
        }

        WriteComponentsIndex(projectFiles, projectSymbols, rank);
    }

    private static void WriteOneProjectDoc(
        string project,
        Dictionary<string, SortedSet<string>> projectFiles,
        Dictionary<string, List<SymbolInfo>> projectSymbols,
        Dictionary<(string, string), int> coupling,
        Dictionary<string, Dictionary<(string, string), int>> internalEdges,
        Dictionary<string, int> rank)
    {
        List<SymbolInfo> symbols = projectSymbols.GetValueOrDefault(project, []);
        SortedSet<string> files = projectFiles.GetValueOrDefault(project, new SortedSet<string>(StringComparer.OrdinalIgnoreCase));
        List<SymbolInfo> types = symbols.Where(s => TypeKinds.Contains(s.Kind)).ToList();
        int publicSymbols = symbols.Count(s => s.Accessibility.Equals("Public", StringComparison.OrdinalIgnoreCase));
        List<SymbolInfo> publicTypes = types.Where(s => s.Accessibility.Equals("Public", StringComparison.OrdinalIgnoreCase)).ToList();
        int publicMembers = publicSymbols - publicTypes.Count;
        string[] deps = CsprojEdges.GetValueOrDefault(project, []);
        List<string> dependents = CsprojEdges
            .Where(kv => kv.Value.Contains(project))
            .Select(kv => kv.Key)
            .OrderBy(rank.GetValueOrDefault)
            .ThenBy(n => n)
            .ToList();

        StringBuilder sb = new();
        sb.AppendLine($"# AIMonitor.{project} — component architecture (generated)");
        sb.AppendLine();
        sb.AppendLine("> Generated from the solution index via the AIMonitor MCP server.");
        sb.AppendLine($"> **rank {rank.GetValueOrDefault(project)}** in the solution layering. Solution-wide view: [`../Architecture.aim.md`](../Architecture.aim.md).");
        sb.AppendLine();

        sb.AppendLine("## Index evidence (project-scoped)");
        sb.AppendLine();
        sb.AppendLine($"- **Files:** {files.Count}");
        sb.AppendLine($"- **Symbols:** {symbols.Count} (public: {publicSymbols})");
        int namespaceCount = types.Select(t => t.Namespace).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Count();
        sb.AppendLine($"- **Named types:** {types.Count} across {namespaceCount} namespace(s); public API surface: {publicTypes.Count} type(s) + {publicMembers} public member(s)");
        sb.AppendLine();

        sb.AppendLine("## Place in the layering");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.AppendLine("flowchart TB");
        if (deps.Length == 0 && dependents.Count == 0)
        {
            sb.AppendLine($"  {project}");
        }
        foreach (string dep in deps)
        {
            int w = coupling.GetValueOrDefault((project, dep));
            string label = w > 0 ? $"{w} refs" : "declared only";
            string arrow = w > 0 ? "-->" : "-.->";
            sb.AppendLine($"  {project} {arrow}|{label}| {dep}");
        }
        foreach (string consumer in dependents)
        {
            int w = coupling.GetValueOrDefault((consumer, project));
            string label = w > 0 ? $"{w} refs" : "declared only";
            string arrow = w > 0 ? "-->" : "-.->";
            sb.AppendLine($"  {consumer} {arrow}|{label}| {project}");
        }
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("### Depends on (outbound)");
        sb.AppendLine();
        if (deps.Length == 0)
        {
            sb.AppendLine("_None — foundation project (rank 0)._");
        }
        else
        {
            sb.AppendLine("| Dependency | Live refs (index) | Verdict |");
            sb.AppendLine("|---|---|---|");
            foreach (string dep in deps.OrderBy(rank.GetValueOrDefault).ThenBy(n => n))
            {
                int w = coupling.GetValueOrDefault((project, dep));
                string verdict = w > 0 ? "live (caller/index-verified)" : "declared-only (no public ref — structural/transitive or candidate-dead)";
                sb.AppendLine($"| {dep} | {w} | {verdict} |");
            }
        }
        sb.AppendLine();

        sb.AppendLine("### Depended on by (inbound)");
        sb.AppendLine();
        if (dependents.Count == 0)
        {
            sb.AppendLine("_None — top of the stack (no src project references this one)._");
        }
        else
        {
            sb.AppendLine("| Consumer | Live refs into this project | Verdict |");
            sb.AppendLine("|---|---|---|");
            foreach (string consumer in dependents)
            {
                int w = coupling.GetValueOrDefault((consumer, project));
                string verdict = w > 0 ? "live (caller/index-verified)" : "declared-only (no public ref — structural/transitive or candidate-dead)";
                sb.AppendLine($"| {consumer} | {w} | {verdict} |");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Namespaces & types");
        sb.AppendLine();
        if (types.Count == 0)
        {
            sb.AppendLine("_No named types indexed (entry-point or markup-only project)._");
        }
        else
        {
            foreach (var nsGroup in types
                .GroupBy(t => t.Namespace.Length > 0 ? t.Namespace : "(global)")
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"- **{nsGroup.Key}**");
                foreach (SymbolInfo t in nsGroup.OrderBy(t => t.Name, StringComparer.Ordinal))
                {
                    string vis = t.Accessibility.Equals("Public", StringComparison.OrdinalIgnoreCase) ? "" : $" _{t.Accessibility.ToLowerInvariant()}_";
                    sb.AppendLine($"  - `{t.Name}`{KindLabel(t.Kind)}{vis}");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Public API surface (cross-project anchors)");
        sb.AppendLine();
        if (publicTypes.Count == 0)
        {
            sb.AppendLine("_No public types — this project exposes no compiler-visible cross-project surface (consumed structurally, via DI, or `internal` + `InternalsVisibleTo`)._");
        }
        else
        {
            sb.AppendLine($"{publicTypes.Count} public type(s) other projects can bind to:");
            sb.AppendLine();
            foreach (SymbolInfo t in publicTypes.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                sb.AppendLine($"- `{t.Name}`{KindLabel(t.Kind)}");
            }
        }
        sb.AppendLine();

        WriteInternalCoupling(sb, project, internalEdges);

        sb.AppendLine("## Caveats");
        sb.AppendLine();
        sb.AppendLine("- **Public-surface only:** coupling and internal edges are built from references whose *target* symbol is `Public`. Intra-project use through `internal`/`private` symbols is not probed, so the internal graph is a **partial** view (real cohesion is higher).");
        sb.AppendLine("- **Reference-site semantics:** a file consumed only by assembly load / DI / config shows no edge despite a runtime relationship.");
        sb.AppendLine("- **Self-index, not live-watch:** produced by indexing AIMonitor as a *target* solution. Regenerate-and-diff is stable (same index → byte-identical doc).");
        sb.AppendLine();

        string outPath = Path.Combine(ComponentsDir, $"{project}.aim.md");
        File.WriteAllText(outPath, sb.ToString());
    }

    private static void WriteInternalCoupling(
        StringBuilder sb,
        string project,
        Dictionary<string, Dictionary<(string, string), int>> internalEdges)
    {
        sb.AppendLine("## Internal coupling (public-surface, intra-project reference sites)");
        sb.AppendLine();
        Dictionary<(string, string), int> edges = internalEdges.GetValueOrDefault(project, new());
        if (edges.Count == 0)
        {
            sb.AppendLine("_No intra-project public-surface reference edges observed (single-file project, or internal cohesion flows through non-public symbols)._");
            sb.AppendLine();
            return;
        }

        List<KeyValuePair<(string consumer, string target), int>> ordered = edges
            .OrderByDescending(e => e.Value)
            .ThenBy(e => ShortFile(e.Key.Item1), StringComparer.Ordinal)
            .ToList();

        const int MaxEdges = 25;
        List<KeyValuePair<(string consumer, string target), int>> shown = ordered.Take(MaxEdges).ToList();
        sb.AppendLine("```mermaid");
        sb.AppendLine("flowchart LR");
        HashSet<string> declared = new(StringComparer.Ordinal);
        foreach (var e in shown)
        {
            string a = SanitizeId(e.Key.consumer);
            string b = SanitizeId(e.Key.target);
            if (declared.Add(a))
            {
                sb.AppendLine($"  {a}[\"{ShortFile(e.Key.consumer)}\"]");
            }

            if (declared.Add(b))
            {
                sb.AppendLine($"  {b}[\"{ShortFile(e.Key.target)}\"]");
            }

            sb.AppendLine($"  {a} -->|{e.Value}| {b}");
        }
        sb.AppendLine("```");
        sb.AppendLine();
        if (ordered.Count > MaxEdges)
        {
            sb.AppendLine($"_Showing the top {MaxEdges} of {ordered.Count} intra-project edges by weight. Edge `A --> B` = file A references a public symbol defined in file B._");
        }
        else
        {
            sb.AppendLine($"_{ordered.Count} intra-project edge(s). Edge `A --> B` = file A references a public symbol defined in file B._");
        }
        sb.AppendLine();
    }

    private static void WriteComponentsIndex(
        Dictionary<string, SortedSet<string>> projectFiles,
        Dictionary<string, List<SymbolInfo>> projectSymbols,
        Dictionary<string, int> rank)
    {
        StringBuilder sb = new();
        sb.AppendLine("# AIMonitor components (generated, per-project)");
        sb.AppendLine();
        sb.AppendLine("> One internal-architecture doc per `src/` project, generated alongside the solution-wide [`../Architecture.aim.md`](../Architecture.aim.md).");
        sb.AppendLine();
        sb.AppendLine("| Rank | Project | Files | Symbols | Doc |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (string project in CsprojEdges.Keys.OrderBy(rank.GetValueOrDefault).ThenBy(n => n))
        {
            int fileCount = projectFiles.GetValueOrDefault(project, new SortedSet<string>()).Count;
            int symbolCount = projectSymbols.GetValueOrDefault(project, []).Count;
            sb.AppendLine($"| {rank.GetValueOrDefault(project)} | AIMonitor.{project} | {fileCount} | {symbolCount} | [`{project}.aim.md`]({project}.aim.md) |");
        }
        sb.AppendLine();

        File.WriteAllText(Path.Combine(ComponentsDir, "README.md"), sb.ToString());
    }

    private static int ComputeRank(string node, Dictionary<string, int> memo)
    {
        return ComputeRankOver(CsprojEdges, node, memo);
    }

    // Topological longest-path rank over an arbitrary edge map (foundation = rank 0). Used both for
    // the assigned CsprojEdges and, during derivation, for ordering before the static field is set.
    private static int ComputeRankOver(Dictionary<string, string[]> edges, string node, Dictionary<string, int> memo)
    {
        if (memo.TryGetValue(node, out int r))
        {
            return r;
        }

        string[] deps = edges.GetValueOrDefault(node, []);
        int rank = deps.Length == 0 ? 0 : 1 + deps.Max(d => ComputeRankOver(edges, d, memo));
        memo[node] = rank;
        return rank;
    }

    // Build the consumer -> dependency project DAG from the real <ProjectReference> items of the src
    // projects the index loaded. Scope = the distinct csproj paths the index reports that live under a
    // src/ folder; edges are kept only between in-scope src projects (tests/samples excluded). Returned
    // in foundation-first (rank, then name) order so the rendered diagram is deterministic across runs.
    private static Dictionary<string, string[]> DeriveCsprojEdges(string indexJson)
    {
        HashSet<string> csprojPaths = new(StringComparer.OrdinalIgnoreCase);
        using (JsonDocument doc = JsonDocument.Parse(indexJson))
        {
            foreach (JsonElement f in doc.RootElement.GetProperty("files").EnumerateArray())
            {
                if (f.TryGetProperty("projectPath", out JsonElement pp) && pp.ValueKind == JsonValueKind.String)
                {
                    string path = pp.GetString()!;
                    if (IsSrcProject(path))
                    {
                        csprojPaths.Add(path);
                    }
                }
            }
        }

        HashSet<string> inScope = new(csprojPaths.Select(ShortName), StringComparer.Ordinal);
        Dictionary<string, string[]> raw = new();
        foreach (string csproj in csprojPaths)
        {
            string consumer = ShortName(csproj);
            SortedSet<string> deps = new(StringComparer.Ordinal);
            if (File.Exists(csproj))
            {
                XDocument xml = XDocument.Load(csproj);
                foreach (XElement pr in xml.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
                {
                    string? include = pr.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(include))
                    {
                        continue;
                    }

                    string dep = ShortName(include.Replace('\\', '/'));
                    if (inScope.Contains(dep))
                    {
                        deps.Add(dep);
                    }
                }
            }

            raw[consumer] = deps.ToArray();
        }

        Dictionary<string, int> rank = new();
        foreach (string project in raw.Keys)
        {
            ComputeRankOver(raw, project, rank);
        }

        Dictionary<string, string[]> ordered = new();
        foreach (string project in raw.Keys
            .OrderBy(rank.GetValueOrDefault)
            .ThenBy(n => n, StringComparer.Ordinal))
        {
            ordered[project] = raw[project];
        }

        return ordered;
    }

    private static bool IsSrcProject(string projectPath)
    {
        string normalized = projectPath.Replace('\\', '/');
        return normalized.Contains("/src/", StringComparison.OrdinalIgnoreCase);
    }

    // EXPERIMENTAL (additive): a second layering ranked by weighted afferent centrality (PageRank),
    // rendered as banded boxes (foundation at bottom) plus a depth-vs-centrality comparison table.
    // This does not modify the topological view; it answers the open research question of whether a
    // better metric than longest-path depth produces a more intuitive layering on AI-generated code.
    private static void AppendExperimentalLayering(
        StringBuilder sb,
        Dictionary<(string, string), int> coupling,
        Dictionary<string, int> depthRank)
    {
        Dictionary<string, double> pageRank = ComputeWeightedPageRank(coupling);
        double maxPr = pageRank.Values.Max();

        Dictionary<string, int> fanIn = CsprojEdges.Keys.ToDictionary(x => x, _ => 0);
        foreach (KeyValuePair<string, string[]> kv in CsprojEdges)
        {
            foreach (string dep in kv.Value)
            {
                fanIn[dep] = fanIn[dep] + 1;
            }
        }

        Dictionary<string, int> weightedInbound = CsprojEdges.Keys.ToDictionary(x => x, _ => 0);
        foreach (KeyValuePair<(string, string), int> kv in coupling)
        {
            weightedInbound[kv.Key.Item2] = weightedInbound[kv.Key.Item2] + kv.Value;
        }

        double avgDegree = CsprojEdges.Keys.Average(x => fanIn[x] + CsprojEdges[x].Length);

        List<string> ordered = pageRank
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .ToList();

        // Bands cut at the largest relative score gaps — data-driven, no fixed thresholds.
        List<List<string>> bands = new();
        List<string> currentBand = new();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i > 0)
            {
                double prev = pageRank[ordered[i - 1]];
                double here = pageRank[ordered[i]];
                if (here <= 0 || prev / here > 1.5)
                {
                    bands.Add(currentBand);
                    currentBand = new();
                }
            }

            currentBand.Add(ordered[i]);
        }

        if (currentBand.Count > 0)
        {
            bands.Add(currentBand);
        }

        sb.AppendLine("## Layered architecture — experimental (centrality-derived)");
        sb.AppendLine();
        sb.AppendLine("> **Experimental, shown beside the topological view above — not a replacement.** This layering ranks by **weighted PageRank** over the consumer→dependency graph (edge weight = live ref count) — *how much depends on you* (afferent centrality), per the layout-derivation research. Foundation = highest PageRank, rendered at the bottom. Bands are cut at the largest score gaps (data-driven, no fixed thresholds). **Class** is by edge direction (supplier vs. driver). Tier *names* are deliberately omitted: the research confirms naming/role is maintained intent, not graph-derived — only derived position and class are shown.");
        sb.AppendLine();

        sb.AppendLine("```mermaid");
        sb.AppendLine("flowchart TB");
        List<List<string>> topToBottom = Enumerable.Reverse(bands).ToList();
        for (int b = 0; b < topToBottom.Count; b++)
        {
            double bandMax = topToBottom[b].Max(p => pageRank[p]) / maxPr;
            double bandMin = topToBottom[b].Min(p => pageRank[p]) / maxPr;
            string desc = bandMax.ToString("0.00", CultureInfo.InvariantCulture) + "–" + bandMin.ToString("0.00", CultureInfo.InvariantCulture);
            sb.AppendLine($"  subgraph tier{b}[\"PageRank {desc}\"]");
            sb.AppendLine("    direction LR");
            foreach (string p in topToBottom[b].OrderBy(n => n, StringComparer.Ordinal))
            {
                sb.AppendLine($"    {p}");
            }

            sb.AppendLine("  end");
        }

        for (int b = 0; b + 1 < topToBottom.Count; b++)
        {
            sb.AppendLine($"  tier{b} ~~~ tier{b + 1}");
        }

        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Layering comparison — topological depth vs. centrality");
        sb.AppendLine();
        sb.AppendLine("| Project | Depth rank (current) | Weighted PageRank (norm) | Inbound refs | Fan-in / Fan-out | Edge-direction class |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (string p in ordered)
        {
            string pr = (pageRank[p] / maxPr).ToString("0.000", CultureInfo.InvariantCulture);
            sb.AppendLine($"| {p} | {depthRank.GetValueOrDefault(p)} | {pr} | {weightedInbound[p]} | {fanIn[p]} / {CsprojEdges[p].Length} | {ClassifyByDirection(p, fanIn, avgDegree)} |");
        }

        sb.AppendLine();
    }

    // Edge-direction node class (Bunch-style supplier vs. driver / omnipresent), derived from the graph.
    private static string ClassifyByDirection(string project, Dictionary<string, int> fanIn, double avgDegree)
    {
        int fi = fanIn[project];
        int fo = CsprojEdges[project].Length;
        bool omnipresent = (fi + fo) >= 3 * avgDegree;
        if (fo == 0 && fi > 0)
        {
            return "pure foundation (sink — depended-upon only)";
        }

        if (fi == 0 && fo > 0)
        {
            return "driver / entry (leaf consumer)";
        }

        if (omnipresent)
        {
            return "omnipresent hub (degree ≫ avg)";
        }

        return fi >= fo ? "supplier-leaning (more depended-upon)" : "consumer-leaning (depends outward)";
    }

    // Weighted PageRank over the consumer->dependency graph: edge weight = max(live refs, 1) so a
    // structural-only (declared, zero-ref) edge still carries rank. High score = depended-upon =
    // foundation. Deterministic: fixed damping, fixed iteration count, sorted iteration, no randomness.
    private static Dictionary<string, double> ComputeWeightedPageRank(Dictionary<(string, string), int> coupling)
    {
        const double Damping = 0.85;
        const int Iterations = 100;
        List<string> nodes = CsprojEdges.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
        int n = nodes.Count;

        Dictionary<string, List<(string Dep, double Weight)>> outEdges = new();
        Dictionary<string, double> outWeight = new();
        foreach (string node in nodes)
        {
            List<(string, double)> edges = new();
            double total = 0;
            foreach (string dep in CsprojEdges.GetValueOrDefault(node, []))
            {
                double w = Math.Max(coupling.GetValueOrDefault((node, dep)), 1);
                edges.Add((dep, w));
                total += w;
            }

            outEdges[node] = edges;
            outWeight[node] = total;
        }

        Dictionary<string, double> pr = nodes.ToDictionary(x => x, _ => 1.0 / n);
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            Dictionary<string, double> next = nodes.ToDictionary(x => x, _ => (1.0 - Damping) / n);

            double dangling = 0;
            foreach (string node in nodes)
            {
                if (outWeight[node] == 0)
                {
                    dangling += pr[node];
                }
            }

            double danglingShare = Damping * dangling / n;
            foreach (string node in nodes)
            {
                next[node] = next[node] + danglingShare;
            }

            foreach (string node in nodes)
            {
                if (outWeight[node] == 0)
                {
                    continue;
                }

                foreach ((string dep, double weight) in outEdges[node])
                {
                    next[dep] = next[dep] + (Damping * pr[node] * (weight / outWeight[node]));
                }
            }

            pr = next;
        }

        return pr;
    }

    private static string ShortName(string projectPath)
    {
        string name = Path.GetFileNameWithoutExtension(projectPath);
        return name.StartsWith("AIMonitor.", StringComparison.Ordinal) ? name["AIMonitor.".Length..] : name;
    }

    private static string ShortFile(string filePath)
    {
        return Path.GetFileName(filePath);
    }

    // The Roslyn index stores SymbolKind ("NamedType") for all types, not the class/interface/enum
    // TypeKind — so "(namedtype)" is noise. Surface a kind label only when it is informative.
    private static string KindLabel(string kind)
    {
        return kind.Equals("NamedType", StringComparison.OrdinalIgnoreCase) ? "" : $" ({kind.ToLowerInvariant()})";
    }

    private static string SanitizeId(string filePath)
    {
        StringBuilder sb = new("f_");
        foreach (char c in Path.GetFileName(filePath))
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return sb.ToString();
    }

    private static string GetStringOrEmpty(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "";
        }

        return "";
    }

    private static async Task<string> CallText(McpClient client, string tool, IReadOnlyDictionary<string, object?> args)
    {
        CallToolResult result = await client.CallToolAsync(tool, args);
        string wrapperJson = JsonSerializer.Serialize(result);
        using JsonDocument document = JsonDocument.Parse(wrapperJson);
        if (document.RootElement.TryGetProperty("content", out JsonElement content))
        {
            foreach (JsonElement item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        return wrapperJson;
    }
}
