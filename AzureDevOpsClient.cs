using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IterationGenerator
{
    public class AzureDevOpsClient
    {
        // Thrown when the target team already has the maximum allowed subscribed iterations
        public class TeamIterationLimitReachedException : Exception
        {
            public TeamIterationLimitReachedException(string message) : base(message) { }
        }

    internal readonly HttpClient _http;
        private readonly string _organization;
        private readonly string _adourl;
        public string ApiVersion { get; }

        public AzureDevOpsClient(string adourl, string organization, string pat, string apiVersion = "7.1")
        {
            _organization = organization?.Trim() ?? throw new ArgumentNullException(nameof(organization));
            _adourl = adourl?.Trim() ?? throw new ArgumentNullException(nameof(adourl));
            ApiVersion = apiVersion ?? "7.1";
            Console.WriteLine(_adourl);
            Console.WriteLine(_organization);
            var url = $"{_adourl}/{_organization}/";
            Console.WriteLine(url);

            var handler = new HttpClientHandler
            {
                // Do not automatically follow redirects so we can detect sign-in redirects
                AllowAutoRedirect = false
            };
            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri(url)
            };
            var token = ":" + pat; // leading colon
            var base64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(token));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // Create classification node (iteration) under project. If parentPath is null/empty, creates at root.
        // Returns dynamic parsed response (id, name, path ...)
        public async Task<JsonElement> CreateIterationNodeAsync(string project, string parentPath, string name, DateTime? startDate = null, DateTime? finishDate = null)
        {
            // Retrieve full iteration tree for duplicate detection and path matching
            string allUrl = $"{project}/_apis/wit/classificationnodes/iterations?api-version={ApiVersion}&$depth=10";
            using var getResp = await _http.GetAsync(allUrl);
            var getRespBody = await getResp.Content.ReadAsStringAsync();

            // Compute desired full path (as returned by GET) to check for an existing node
            // and compute a project-relative postParentPath for POST (omit the project name)
            string desiredFullPath;
            string? postParentPath = null;

            // Normalize incoming parentPath (may be null, project name, or a GET "path" like "\MyProject\Iteration\Parent")
            var parentNorm = string.IsNullOrWhiteSpace(parentPath) ? null : parentPath.Trim();
            bool parentIsProject = parentNorm != null && string.Equals(parentNorm.Trim('\\'), project, StringComparison.OrdinalIgnoreCase);

            if (parentNorm == null || parentIsProject)
            {
                // Creating directly under project root (no path query)
                desiredFullPath = "\\" + project + "\\" + name;
                postParentPath = null;
            }
            else
            {
                // parentNorm may or may not start with a leading backslash and may or may not include the project name
                var p = parentNorm.StartsWith("\\") ? parentNorm.TrimStart('\\') : parentNorm; // remove leading backslash

                // If the provided parent includes the project at the start, strip it to compute project-relative path
                if (p.StartsWith(project + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    var after = p.Substring(project.Length).TrimStart('\\');
                    // POST path must be project-relative WITHOUT a leading backslash
                    postParentPath = after;
                    desiredFullPath = "\\" + project + "\\" + after + "\\" + name;
                }
                else
                {
                    // parent did not include project; assume it is already project-relative (e.g. "Iteration\Parent")
                    postParentPath = p;
                    desiredFullPath = "\\" + project + "\\" + p + "\\" + name;
                }
            }

            // If GET succeeded, search for an existing node with the exact desired path
            if (getResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(getRespBody);

                bool found = false;
                JsonElement foundNode = default;

                void Traverse(JsonElement node)
                {
                    if (found) return;
                    if (node.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() == desiredFullPath)
                    {
                        found = true;
                        foundNode = node.Clone();
                        return;
                    }
                    if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var child in children.EnumerateArray())
                        {
                            Traverse(child);
                            if (found) return;
                        }
                    }
                }

                Traverse(doc.RootElement);
                if (found)
                {
                    Console.WriteLine($"Iteration '{name}' already exists at path '{foundNode.GetProperty("path").GetString()}'. Skipping creation.");
                    return foundNode.Clone();
                }
            }

            // Build POST URL — include project-relative path when creating under a parent
            string url = $"{project}/_apis/wit/classificationnodes/iterations?api-version={ApiVersion}";
            if (!string.IsNullOrEmpty(postParentPath))
            {
                // Use forward-slash separators in the query path (server may expect slash-separated segments)
                var queryPath = postParentPath.Replace('\\', '/');
                url += "&path=" + Uri.EscapeDataString(queryPath);
            }

            var body = new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = name
            };
            if (startDate.HasValue || finishDate.HasValue)
            {
                var attr = new System.Text.Json.Nodes.JsonObject();
                if (startDate.HasValue) attr["startDate"] = startDate.Value.ToString("o");
                if (finishDate.HasValue) attr["finishDate"] = finishDate.Value.ToString("o");
                body["attributes"] = attr;
            }

            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(url, content);

            var respBody = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                // Throw with full context so you can see HTML/error page
                throw new HttpRequestException($"POST {url} failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\nResponse body:\n{respBody}\nRequest body:\n{body.ToJsonString()}");
            }

            // Success: ensure response is JSON before parsing
            try
            {
                using var doc = JsonDocument.Parse(respBody);
                return doc.RootElement.Clone();
            }
            catch (JsonException je)
            {
                throw new InvalidOperationException($"POST {url} returned non-JSON success response. Response body:\n{respBody}\nRequest body:\n{body.ToJsonString()}", je);
            }
        }
        // Optional: add team iteration (attempt). If API shape differs the call may fail and user must adjust.
        public async Task<HttpResponseMessage> AddTeamIterationAsync(string project, string team, JsonElement iterationNode)
        {
            var url = $"{project}/{Uri.EscapeDataString(team)}/_apis/work/teamsettings/iterations?api-version={ApiVersion}";

            // Collect candidate payloads to try, in order. We'll try identifier (GUID), numeric id, and iterationId field.
            var candidates = new System.Collections.Generic.List<System.Text.Json.Nodes.JsonObject>();

            // Candidate: identifier GUID
            if (iterationNode.TryGetProperty("identifier", out var identProp) && identProp.ValueKind == JsonValueKind.String)
            {
                var o = new System.Text.Json.Nodes.JsonObject();
                o["id"] = identProp.GetString();
                if (iterationNode.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String) o["name"] = nameProp.GetString();
                candidates.Add(o);
            }

            // Candidate: numeric id (if present)
            if (iterationNode.TryGetProperty("id", out var idProp))
            {
                var o = new System.Text.Json.Nodes.JsonObject();
                if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt32(out var intId))
                {
                    o["id"] = intId;
                }
                else if (idProp.ValueKind == JsonValueKind.String)
                {
                    o["id"] = idProp.GetString();
                }
                else
                {
                    o["id"] = idProp.GetRawText();
                }
                if (iterationNode.TryGetProperty("name", out var nameProp2) && nameProp2.ValueKind == JsonValueKind.String) o["name"] = nameProp2.GetString();
                candidates.Add(o);
            }

            // Candidate: use 'iterationId' property name (some APIs expect this)
            if (iterationNode.TryGetProperty("identifier", out var identProp2) && identProp2.ValueKind == JsonValueKind.String)
            {
                var o = new System.Text.Json.Nodes.JsonObject();
                o["iterationId"] = identProp2.GetString();
                if (iterationNode.TryGetProperty("name", out var nameProp3) && nameProp3.ValueKind == JsonValueKind.String) o["name"] = nameProp3.GetString();
                candidates.Add(o);
            }

            // If we have no candidates, at least attempt to send the whole node as body
            if (candidates.Count == 0)
            {
                var raw = System.Text.Json.Nodes.JsonNode.Parse(iterationNode.GetRawText()) as System.Text.Json.Nodes.JsonObject;
                if (raw != null) candidates.Add(raw);
            }

            System.Text.StringBuilder aggErrors = new System.Text.StringBuilder();
            foreach (var obj in candidates)
            {
                using var content = new StringContent(obj.ToJsonString(), Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(url, content);
                var respBody = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Added team iteration using payload: {obj.ToJsonString()}");
                    return resp;
                }
                else
                {
                    // Detect the specific error where the team has >300 iterations subscribed
                    if ((respBody != null && respBody.IndexOf("subscribed to 301 iterations", StringComparison.OrdinalIgnoreCase) >= 0)
                        || (respBody != null && respBody.IndexOf("This team is subscribed to", StringComparison.OrdinalIgnoreCase) >= 0)
                        || (respBody != null && respBody.IndexOf("VS403228", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        throw new TeamIterationLimitReachedException("Target team has reached the maximum allowed subscribed iterations (300). Please reduce team subscriptions before retrying.");
                    }

                    aggErrors.AppendLine($"Attempt with payload {obj.ToJsonString()} failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\nResponse: {respBody}");
                }
            }

            // All attempts failed — throw with aggregated diagnostics
            throw new HttpRequestException($"POST {url} failed for all candidate payloads. Details:\n" + aggErrors.ToString());
        }

        // Create an Area classification node. Works similarly to CreateIterationNodeAsync but targets the 'areas' classification nodes.
        public async Task<JsonElement> CreateAreaNodeAsync(string project, string parentPath, string name)
        {
            // Retrieve full area tree for duplicate detection
            string allUrl = $"{project}/_apis/wit/classificationnodes/areas?api-version={ApiVersion}&$depth=10";
            using var getResp = await _http.GetAsync(allUrl);
            var getRespBody = await getResp.Content.ReadAsStringAsync();

            string desiredFullPath;
            string? postParentPath = null;

            var parentNorm = string.IsNullOrWhiteSpace(parentPath) ? null : parentPath.Trim();
            bool parentIsProject = parentNorm != null && string.Equals(parentNorm.Trim('\\'), project, StringComparison.OrdinalIgnoreCase);

            if (parentNorm == null || parentIsProject)
            {
                desiredFullPath = "\\" + project + "\\" + name;
                postParentPath = null;
            }
            else
            {
                var p = parentNorm.StartsWith("\\") ? parentNorm.TrimStart('\\') : parentNorm;
                if (p.StartsWith(project + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    var after = p.Substring(project.Length).TrimStart('\\');
                    postParentPath = after; // project-relative without leading slash
                    desiredFullPath = "\\" + project + "\\" + after + "\\" + name;
                }
                else
                {
                    postParentPath = p;
                    desiredFullPath = "\\" + project + "\\" + p + "\\" + name;
                }
            }

            if (getResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(getRespBody);
                bool found = false;
                JsonElement foundNode = default;

                void Traverse(JsonElement node)
                {
                    if (found) return;
                    if (node.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() == desiredFullPath)
                    {
                        found = true;
                        foundNode = node.Clone();
                        return;
                    }
                    if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var child in children.EnumerateArray())
                        {
                            Traverse(child);
                            if (found) return;
                        }
                    }
                }

                Traverse(doc.RootElement);
                if (found)
                {
                    Console.WriteLine($"Area '{name}' already exists at path '{foundNode.GetProperty("path").GetString()}'. Skipping creation.");
                    return foundNode.Clone();
                }
            }

            string url = $"{project}/_apis/wit/classificationnodes/areas?api-version={ApiVersion}";
            if (!string.IsNullOrEmpty(postParentPath))
            {
                var queryPath = postParentPath.Replace('\\', '/');
                url += "&path=" + Uri.EscapeDataString(queryPath);
            }

            var body = new System.Text.Json.Nodes.JsonObject { ["name"] = name };
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var resp = await _http.PostAsync(url, content);
            var respBody = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"POST {url} failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\nResponse body:\n{respBody}\nRequest body:\n{body.ToJsonString()}");
            }

            using var docResp = JsonDocument.Parse(respBody);
            return docResp.RootElement.Clone();
        }

        
        // Simple GET request to verify authentication and connectivity
        public async Task<bool> TestConnectionAsync()
        {
            var url = $"_apis/projects?api-version={ApiVersion}";
            using var resp = await _http.GetAsync(url);
            var respBody = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode && respBody.Contains("value"))
            {
                Console.WriteLine("Connection to Azure DevOps REST API succeeded.");
                return true;
            }
            else
            {
                Console.WriteLine($"Connection failed. Status: {(int)resp.StatusCode} {resp.ReasonPhrase}\nResponse body:\n{respBody}");
                return false;
            }
        }

        // Test GET to iterations endpoint for project
        public async Task<bool> TestProjectIterationsGetAsync(string project)
        {
            string url = $"{project}/_apis/wit/classificationnodes/iterations?api-version={ApiVersion}";
            using var resp = await _http.GetAsync(url);
            var respBody = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode && respBody.Contains("value"))
            {
                Console.WriteLine("GET to iterations endpoint succeeded.");
                return true;
            }
            else
            {
                Console.WriteLine($"GET to iterations endpoint failed. Status: {(int)resp.StatusCode} {resp.ReasonPhrase}\nResponse body:\n{respBody}");
                return false;
            }
        }

        // Get iterations JSON for a project (recursive depth). Throws HttpRequestException with body when non-success
        public async Task<JsonElement> GetProjectIterationsJsonAsync(string project, int depth = 10)
        {
            string url = $"{project}/_apis/wit/classificationnodes/iterations?api-version={ApiVersion}&$depth={depth}";
            using var resp = await _http.GetAsync(url);
            var respBody = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"GET {url} failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\nResponse body:\n{respBody}");
            }

            // Ensure response is JSON
            // Quick heuristic: if Content-Type is HTML or body starts with '<', treat as auth/HTML redirect
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (contentType.Contains("html") || respBody.TrimStart().StartsWith("<"))
            {
                var snippet = respBody.Length > 2048 ? respBody.Substring(0, 2048) + "...[truncated]" : respBody;
                throw new HttpRequestException($"GET {url} returned HTML (likely a sign-in page or redirect). This usually means authentication failed or the URL is incorrect. Response snippet:\n{snippet}");
            }

            try
            {
                using var doc = JsonDocument.Parse(respBody);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new HttpRequestException($"GET {url} returned non-JSON response. Response body (truncated):\n{(respBody.Length > 2048 ? respBody.Substring(0, 2048) + "...[truncated]" : respBody)}");
            }
        }
    }
}