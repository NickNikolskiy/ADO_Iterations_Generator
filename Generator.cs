using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace IterationGenerator
{
    public class Generator
    {
        private readonly AzureDevOpsClient _client;
        private readonly CliConfig _cfg;
        private readonly List<JsonElement> _created = new List<JsonElement>();

        public Generator(AzureDevOpsClient client, CliConfig cfg)
        {
            _client = client;
            _cfg = cfg;
        }

        public async Task GenerateAsync()
        {
            Console.WriteLine($"Creating iterations in org='{_cfg.Organization}', project='{_cfg.Project}'");
            await CreateLevelAsync(parentPath: _cfg.Project, currentDepth: 1, startDate: _cfg.StartDate, prefix: _cfg.BaseName);
            Console.WriteLine("Created iterations:");
            foreach (var e in _created)
            {
                Console.WriteLine($" - {GetElementPath(e)} (id={(e.TryGetProperty("id", out var idp) ? idp.ToString() : "n/a")})");
            }

            if (!string.IsNullOrEmpty(_cfg.Team))
            {
                Console.WriteLine($"Attempting to add created iterations to team '{_cfg.Team}'");
                foreach (var node in _created)
                {
                    var resp = await _client.AddTeamIterationAsync(_cfg.Project, _cfg.Team, node);
                    if (resp.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"  -> Added team iteration for {GetElementPath(node)}");
                    }
                    else
                    {
                        var txt = await resp.Content.ReadAsStringAsync();
                        Console.WriteLine($"  -> Failed to add team iteration for {GetElementPath(node)}: {resp.StatusCode} {txt}");
                    }
                }
            }
        }

        private async Task CreateLevelAsync(string parentPath, int currentDepth, DateTime startDate, string prefix)
        {
            if (currentDepth > _cfg.Depth) return;

            for (int i = 1; i <= _cfg.ChildrenPerLevel; i++)
            {
                var name = $"{prefix}{currentDepth}.{i}";
                var finish = startDate.AddDays(_cfg.DaysPerIteration - 1);
                Console.WriteLine($"Creating node '{name}' under '{parentPath}' start={startDate:yyyy-MM-dd} finish={finish:yyyy-MM-dd}");
                var created = await _client.CreateIterationNodeAsync(_cfg.Project, parentPath, name, startDate, finish);
                _created.Add(created);

                // childParentPath — classification node path is returned as 'path' or 'name'; prefer 'path'
                var childPath = "";
                if (created.TryGetProperty("path", out var pathProp))
                {
                    childPath = pathProp.GetString();
                }
                else if (created.TryGetProperty("name", out var nm))
                {
                    // fallback — this will create sibling nodes under root if not correct
                    childPath = nm.GetString();
                }

                // Recurse to children
                await CreateLevelAsync(childPath, currentDepth + 1, finish.AddDays(1), $"{prefix}_{i}");
            }
        }

        private string GetElementPath(JsonElement e)
        {
            if (e.TryGetProperty("path", out JsonElement path)) return path.GetString();
            if (e.TryGetProperty("name", out JsonElement name)) return name.GetString();
            return "(unknown)";
        }
    }
}