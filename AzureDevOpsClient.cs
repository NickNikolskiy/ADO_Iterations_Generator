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
        private readonly HttpClient _http;
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

            _http = new HttpClient
            {
                BaseAddress = new Uri(url)
            };
            Console.WriteLine($"PAT = {pat}");
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

            var obj = new System.Text.Json.Nodes.JsonObject();
            if (iterationNode.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
            {
                obj["id"] = idProp.GetInt32();
            }
            if (iterationNode.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            {
                obj["name"] = nameProp.GetString();
            }
            if (iterationNode.TryGetProperty("attributes", out var attrs))
            {
                // Convert JsonElement -> JsonNode safely
                obj["attributes"] = System.Text.Json.Nodes.JsonNode.Parse(attrs.GetRawText());
            }

            using var content = new StringContent(obj.ToJsonString(), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(url, content);

            var respBody = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"POST {url} failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\nResponse body:\n{respBody}\nRequest body:\n{content}");
            }

            return resp;
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
    }
}