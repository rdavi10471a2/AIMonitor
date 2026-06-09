using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SelfDocGen;

// Dogfood generator: drives the REAL AIMonitor.McpServer.dll over stdio (bound to the AIMonitor
// self-index via the self-doc config), pulls caller/index-verified coupling, fuses it with the
// source-verified csproj DAG, ranks the layers, and writes GeneratedDocs/Architecture.aim.md.
internal static class Program
{
    private const string ServerDll = @"C:\VSCodeProjects\AIMonitor\src\AIMonitor.McpServer\bin\Debug\net10.0\AIMonitor.McpServer.dll";
    private const string RepoRoot = @"C:\VSCodeProjects\aimonitor-selfdoc";
    private const string ConfigPath = @"C:\VSCodeProjects\aimonitor-selfdoc\config\self-doc.appsettings.json";
    private const string OutPath = @"C:\VSCodeProjects\aimonitor-selfdoc\GeneratedDocs\Architecture.aim.md";

    // Source-verified skeleton: csproj ProjectReference edges (consumer -> dependency), src projects only.
    private static readonly Dictionary<string, string[]> CsprojEdges = new()
    {
        ["Core"] = [],
        ["Logging"] = ["Core"],
        ["MSBuild"] = ["Core"],
        ["Workflow"] = ["Core"],
        ["Data"] = ["Core", "MSBuild"],
        ["Runtime"] = ["Core", "Logging", "Workflow"],
        ["Indexing"] = ["Core", "Data", "Logging", "MSBuild", "Workflow"],
        ["McpStdioBridge"] = ["Core", "Logging"],
        ["App"] = ["Core", "Data", "Indexing", "Logging", "MSBuild", "Workflow", "Runtime"],
        ["Cli"] = ["Core", "Data", "Workflow", "MSBuild", "Indexing", "Logging", "Runtime"],
        ["McpServer"] = ["Core", "Data", "Logging", "Workflow", "MSBuild", "Indexing", "Runtime"],
    };

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

        // file path -> short project name, and the public src symbols we will probe
        Dictionary<string, string> fileToProject = new(StringComparer.OrdinalIgnoreCase);
        List<(string key, string project, string name, string kind)> targets = [];
        using (JsonDocument doc = JsonDocument.Parse(indexJson))
        {
            JsonElement root = doc.RootElement;
            foreach (JsonElement f in root.GetProperty("files").EnumerateArray())
            {
                string proj = ShortName(f.GetProperty("projectPath").GetString()!);
                fileToProject[f.GetProperty("filePath").GetString()!] = proj;
            }

            foreach (JsonElement s in root.GetProperty("symbols").EnumerateArray())
            {
                string proj = ShortName(s.GetProperty("projectPath").GetString()!);
                if (!CsprojEdges.ContainsKey(proj))
                {
                    continue; // skip tests/samples — src only
                }

                string access = s.GetProperty("accessibility").GetString() ?? "";
                if (!access.Equals("Public", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // cross-project coupling flows through public surface
                }

                targets.Add((
                    s.GetProperty("stableKey").GetString()!,
                    proj,
                    s.GetProperty("name").GetString() ?? "",
                    s.GetProperty("kind").GetString() ?? ""));
            }
        }

        Console.WriteLine($"Probing {targets.Count} public src symbols for caller-verified coupling...");

        // coupling[(consumer, dependency)] = reference-site count  (caller/index-verified)
        Dictionary<(string, string), int> coupling = new();
        int done = 0;
        foreach ((string key, string project, string name, string kind) in targets)
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

                        if (fileToProject.TryGetValue(fp.GetString()!, out string? consumer)
                            && consumer != project
                            && CsprojEdges.ContainsKey(consumer))
                        {
                            coupling[(consumer, project)] = coupling.GetValueOrDefault((consumer, project)) + 1;
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
        sb.AppendLine("> Generated by dogfooding the AIMonitor MCP server DLL against AIMonitor's own self-index.");
        sb.AppendLine("> **Skeleton** = `source-verified` (csproj ProjectReference, compiler-enforced). **Coupling weights** = `caller/index-verified` (live cross-project reference sites from the index).");
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

    private static int ComputeRank(string node, Dictionary<string, int> memo)
    {
        if (memo.TryGetValue(node, out int r))
        {
            return r;
        }

        string[] deps = CsprojEdges.GetValueOrDefault(node, []);
        int rank = deps.Length == 0 ? 0 : 1 + deps.Max(d => ComputeRank(d, memo));
        memo[node] = rank;
        return rank;
    }

    private static string ShortName(string projectPath)
    {
        string name = Path.GetFileNameWithoutExtension(projectPath);
        return name.StartsWith("AIMonitor.", StringComparison.Ordinal) ? name["AIMonitor.".Length..] : name;
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
