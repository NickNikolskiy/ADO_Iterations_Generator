using System;
using System.Threading.Tasks;
using System.Text.Json;

namespace IterationGenerator
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("IterationGenerator - Azure DevOps iterations batch creator");

            var config = CliConfig.Parse(args);
            if (config == null)
            {
                CliConfig.PrintUsage();
                return 1;
            }

            var pat = config.Pat ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_PAT");
            if (string.IsNullOrWhiteSpace(pat))
            {
                Console.Write("Enter Azure DevOps PAT (will not be echoed): ");
                pat = ReadPassword();
                Console.WriteLine();
            }

            var client = new AzureDevOpsClient(config.ADOUrl, config.Organization, pat, config.ApiVersion);

            // Test connection before running generator
            var connectionOk = await client.TestConnectionAsync();
            if (!connectionOk)
            {
                Console.WriteLine("Azure DevOps connection test failed. Please check your PAT, organization, and URL.");
                return 3;
            }

            if (config.Mode == "assign-all-to-team")
            {
                if (string.IsNullOrWhiteSpace(config.Team))
                {
                    Console.WriteLine("--team argument is required for assign-all-to-team mode.");
                    return 4;
                }
                try
                {
                    var completed = await AssignAllIterationsToTeamAsync(client, config.Project, config.Team);
                    if (completed)
                    {
                        Console.WriteLine("All iterations assigned to team.");
                        return 0;
                    }
                    else
                    {
                        Console.WriteLine("Stopped assigning iterations because the target team reached the subscription limit (300).\nPlease reduce team subscriptions and re-run to assign remaining iterations.");
                        return 5;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fatal error type: {ex.GetType()}");
                    Console.WriteLine($"Fatal error Message: {ex.Message}");
                    Console.WriteLine($"Fatal error StackTrace: {ex.StackTrace}");
                    return 2;
                }
            }
            else
            {
                var generator = new Generator(client, config);
                try
                {
                    await generator.GenerateAsync();
                    Console.WriteLine("Done.");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fatal error type: {ex.GetType()}");
                    Console.WriteLine($"Fatal error Message: {ex.Message}");
                    Console.WriteLine($"Fatal error StackTrace: {ex.StackTrace}");
                    return 2;
                }
            }
        }

        // simple hidden input
    // Assign all iterations in project to a team
    // Returns true if completed, false if stopped due to team iteration limit
    private static async Task<bool> AssignAllIterationsToTeamAsync(AzureDevOpsClient client, string project, string team)
        {
            try
            {
                var docRoot = await client.GetProjectIterationsJsonAsync(project, depth: 10);
                // Write full JSON to a file for inspection and print a truncated snippet to console
                try
                {
                    var json = docRoot.GetRawText();
                    var outPath = System.IO.Path.Combine(System.Environment.CurrentDirectory, $"project_iterations_{project}.json");
                    System.IO.File.WriteAllText(outPath, json);
                    var snippet = json.Length > 4096 ? json.Substring(0, 4096) + "...[truncated]" : json;
                    Console.WriteLine($"Wrote project iterations JSON to: {outPath}");
                    Console.WriteLine("DEBUG: Project iterations JSON snippet:\n" + snippet);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to write project iterations JSON to file: " + ex.Message);
                }
                await AssignRecursive(docRoot, client, project, team);
                return true;
            }
            catch (IterationGenerator.AzureDevOpsClient.TeamIterationLimitReachedException tle)
            {
                // Stop assigning further iterations
                Console.WriteLine("Stopped assigning: " + tle.Message);
                return false;
            }
            catch (HttpRequestException hre)
            {
                Console.WriteLine($"Failed to fetch iterations: {hre.Message}");
                throw;
            }
        }

        private static async Task AssignRecursive(JsonElement node, AzureDevOpsClient client, string project, string team)
        {
            // Log node for inspection
            try
            {
                Console.WriteLine("DEBUG: Iteration node: " + node.GetRawText());
            }
            catch { }

            bool hasId = node.TryGetProperty("id", out var idProp) && (idProp.ValueKind == JsonValueKind.Number || idProp.ValueKind == JsonValueKind.String);
            bool hasIdentifier = node.TryGetProperty("identifier", out var identProp) && identProp.ValueKind == JsonValueKind.String;

            if (hasId || hasIdentifier)
            {
                try
                {
                    await client.AddTeamIterationAsync(project, team, node);
                    Console.WriteLine($"Assigned iteration '{(node.TryGetProperty("name", out var nameP) ? nameP.GetString() : "(unknown)")}'.");
                }
                catch (IterationGenerator.AzureDevOpsClient.TeamIterationLimitReachedException tle)
                {
                    Console.WriteLine("Team iteration limit reached: " + tle.Message);
                    // rethrow to stop further processing
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to assign iteration '{(node.TryGetProperty("name", out var nameP2) ? nameP2.GetString() : "(unknown)")}' : {ex.Message}");
                    // continue to children
                }
            }

            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in children.EnumerateArray())
                {
                    await AssignRecursive(child, client, project, team);
                }
            }
        }
        private static string ReadPassword()
        {
            var pw = string.Empty;
            ConsoleKeyInfo key;
            while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace && pw.Length > 0)
                {
                    pw = pw.Substring(0, pw.Length - 1);
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    pw += key.KeyChar;
                }
            }
            return pw;
        }
    }
}