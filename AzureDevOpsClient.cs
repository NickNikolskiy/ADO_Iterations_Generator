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
            // Global duplicate check: recursively search all iterations in the project
            string allUrl = $"{project}/_apis/wit/classificationnodes/iterations?api-version={ApiVersion}&$depth=10";
            using var getResp = await _http.GetAsync(allUrl);
            var getRespBody = await getResp.Content.ReadAsStringAsync();
            if (getResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(getRespBody);
                // Recursively search for duplicate name
                bool FoundDuplicate(JsonElement node)
                {
                    if (node.TryGetProperty("name", out var n) && n.GetString() == name)
                        return true;
                    if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var child in children.EnumerateArray())
                        {
                            if (FoundDuplicate(child)) return true;
                        }
                    }
                    return false;
                }
                if (FoundDuplicate(doc.RootElement))
                {
                    Console.WriteLine($"Iteration '{name}' already exists anywhere in project. Skipping creation.");
                    return doc.RootElement.Clone(); // Optionally, return the root or null
                }
            }

            // Restore POST url for creation
            string url = $"{project}/_apis/wit/classificationnodes/iterations?api-version={ApiVersion}";

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
            Console.WriteLine($"DEBUG: GET Iterations JSON URL: {_http.BaseAddress}{url}");
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