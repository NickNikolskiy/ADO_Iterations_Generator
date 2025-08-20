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
            Console.WriteLine($"Creating {(_cfg.Kind == "area" ? "areas" : "iterations")} in org='{_cfg.Organization}', project='{_cfg.Project}'");
            await CreateLevelAsync(parentPath: _cfg.Project, currentDepth: 1, startDate: _cfg.StartDate, prefix: _cfg.BaseName);
            Console.WriteLine("Created iterations:");
            foreach (var e in _created)
            {
                Console.WriteLine($" - {GetElementPath(e)} (id={(e.TryGetProperty("id", out var idp) ? idp.ToString() : "n/a")})");
            }

            if (!string.IsNullOrEmpty(_cfg.Team) && _cfg.Kind == "iteration")
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
            ShowStatusOfCreation();
            for (int i = 1; i <= _cfg.ChildrenPerLevel; i++)
            {
                var name = $"{prefix}_{currentDepth}.{i}";
                var finish = startDate.AddDays(_cfg.DaysPerIteration - 1);

                JsonElement created;
                if (_cfg.Kind == "area")
                {
                    created = await _client.CreateAreaNodeAsync(_cfg.Project, parentPath, name);
                }
                else
                {
                    created = await _client.CreateIterationNodeAsync(_cfg.Project, parentPath, name, startDate, finish);
                }
                _created.Add(created);

                // Recurse to children
                await CreateLevelAsync(parentPath + "\\" + name, currentDepth + 1, startDate.AddDays(1), name);
            }
        }

        private string GetElementPath(JsonElement e)
        {
            if (e.TryGetProperty("path", out JsonElement path)) return path.GetString();
            if (e.TryGetProperty("name", out JsonElement name)) return name.GetString();
            return "(unknown)";
        }

        private void ShowStatusOfCreation()
        {
            var total = _cfg.ChildrenPerLevel ^ _cfg.Depth;
            var created = _created.Count;
            var percent = total <= 0 ? 100 : (int)Math.Min(100, created * 100 / total);
            Console.WriteLine($"Status: Created {created} of {total} {(_cfg.Kind == "area" ? "areas" : "iterations")}. Progress: {percent}%");
        }
    }
}