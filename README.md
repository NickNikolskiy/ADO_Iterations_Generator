## Azure DevOps Iterations / Areas Generator

Command‑line tool for bulk creation of Azure DevOps classification nodes (Iterations or Areas) and optional team assignment (iterations only right now). Designed for seeding demo/test projects or bootstrapping sprint structures.

## Current Feature Set
* Create a hierarchical tree of Iterations OR Areas (choose with `--kind iteration|area`).
* Tree depth and branching factor: `--depth` (levels) and `--children` (siblings per level).
* Date attributes (start / finish) applied to iterations (areas ignore dates).
* Skips duplicates idempotently (409 duplicate responses are resolved to existing nodes).
* Optional team subscription for created iterations (`--team`). (Team area subscriptions are stubbed but may vary by server – adjust if needed.)
* Supports on‑prem / codedev style URLs via `--adourl` and custom API version.
* Graceful handling of authentication / redirect HTML detection.

## IMPORTANT: Growth Warning
The number of nodes created is exponential:

TotalNodes = sum(children^k) for k=1..depth.

Example: depth=4, children=50 => 50 + 2,500 + 125,000 + 6,250,000 = 6,377,550 nodes (NOT recommended).

Use modest values (e.g. depth 3, children 5 => 5 + 25 + 125 = 155).

Add a manual safety review before running very large generations.

## Prerequisites
* .NET 9 SDK
* Azure DevOps PAT with Work Item Tracking (and optionally Project & Team settings) permissions

## Build & Run
```pwsh
dotnet restore
dotnet build

# Show minimal usage line
dotnet run -- --org <org> --project <project>
```

## Core Arguments (current parser)
| Flag | Meaning | Default |
|------|---------|---------|
| `--adourl` / `--ado-url` | Base URL (e.g. https://dev.azure.com or https://codedev.ms) | https://dev.azure.com |
| `--org` / `--organization` | Organization name | (required) |
| `--project` | Project name | (required) |
| `--team` | Team name (only needed if subscribing iterations) | null |
| `--pat` | Personal Access Token | (required unless env var logic added) |
| `--apiversion` | REST API version | 7.1 |
| `--mode` | `autogenerate` or `assign-all-to-team` | autogenerate |
| `--kind` | `iteration` or `area` | iteration |
| `--depth` | Levels in the tree | 1 |
| `--children` / `--count` | Children per node per level | 1 |
| `--basename` / `--prefix` | Base name (nodes become Base_Depth.Index) | Sprint |
| `--start` / `--startdate` | First iteration start date (YYYY-MM-DD) | Today (UTC) |
| `--days` / `--length` | Days per iteration | 14 |

## Naming Pattern
For each level (starting at 1) and each child i (1..children) the name becomes:
```
<BaseName>_<level>.<i>
```
Example with `--basename Sprint --depth 2 --children 2`:
```
Sprint_1.1
Sprint_1.2
Sprint_1.1\Sprint_2.1
Sprint_1.1\Sprint_2.2
Sprint_1.2\Sprint_2.1
Sprint_1.2\Sprint_2.2
```

## Examples

### 1. Simple flat 5 iterations (depth 1, 5 children)
```pwsh
dotnet run -- --adourl https://codedev.ms --org org --project MyProj \
   --pat <PAT> --depth 1 --children 5 --basename Sprint --start 2025-01-06 --days 14
```

### 2. 3-level small tree of areas
```pwsh
dotnet run -- --adourl https://codedev.ms --org org --project MyProj \
   --pat <PAT> --kind area --depth 3 --children 3 --basename AreaRoot
```

### 3. Iterations with team subscription
```pwsh
dotnet run -- --adourl https://codedev.ms --org org --project MyProj \
   --team "MyProj Team" --pat <PAT> --depth 2 --children 4 \
   --basename Sprint --start 2025-02-03 --days 7
```

### 4. Assign ALL existing project iterations to a team
```pwsh
dotnet run -- --adourl https://codedev.ms --org org --project MyProj \
   --team "MyProj Team" --pat <PAT> --mode assign-all-to-team
```

### 5. Large generation (DRY RUN RECOMMENDED FIRST)
If you plan something like `--depth 4 --children 10` (11,110 nodes) reconsider – maybe generate level by level.

## Best Practices & Safety
1. Keep depth * children modest. Exponential growth escalates quickly.
2. Start with a test project before seeding production.
3. Use a PAT with the minimum required scopes; revoke it after bulk operations.
4. Consider a naming prefix that is unique (e.g., `Init2025_`) so reruns won’t clash with human-created nodes.
5. Watch for Azure DevOps service limits (team iteration subscription ~300 unless increased). The client surfaces limit errors.
6. Re-run is idempotent for duplicates: existing nodes are detected (409 handled) and skipped/returned.
7. Avoid embedding PATs in scripts checked into source control (use environment variables or secret managers).
8. If you need extremely large trees, add throttling (sleep) or batching to avoid rate limits (not yet built-in here).

## Error Handling Notes
* 401 / HTML response: likely bad PAT or URL – the client blocks on detecting HTML.
* 404 WorkItemTrackingTreeNodeNotFound: usually means the `?path=` query included the project name or wrong separator.
* 409 ClassificationNodeDuplicateName: tool now treats this as existing and returns the matching node.
* Team subscription 400: payload schema differences across deployments – adapt `AddTeamIterationAsync` / `AddTeamAreaAsync` if needed.

## Extending
Ideas you can add next:
* `--dry-run` flag to print prospective nodes & total count only.
* `--max-nodes` guard that aborts if computed total exceeds threshold.
* Parallel creation with a controlled degree (beware of request bursts).
* Persist a manifest (JSON) of created node IDs for later reference.

## Internal Implementation Highlights
* Uses `HttpClient` with Basic auth (PAT in username-blank format). No redirect following – surfaces sign-in pages.
* Duplicate detection: fetch full tree (depth up to 10) and path compare.
* Path normalization: GET uses `\Project\Iteration\...`; POST expects project‑relative without leading slash.
* 409 handling returns existing node to stay idempotent.

## Registry / Server Limit Tweaks (On-Prem Advanced ONLY)
Only if you operate the server and understand the impact. Example (PowerShell):
```powershell
Set-ServiceRegistryValue -RegistryPath "/Service/Agile/Settings/MaxAllowedTeamIterations" -Value "10000"
Set-ServiceRegistryValue -RegistryPath "/Service/Agile/Settings/MaxAllowedTeamAreaPaths" -Value "10000"
```
Do NOT run on hosted services you don't administrate.

## Security
* Treat PAT like a password; avoid committing or echoing it.
* Clear scrollback if you pasted the PAT in a shared session.
* Revoke PATs used for one-off bulk loads.

## License
MIT
