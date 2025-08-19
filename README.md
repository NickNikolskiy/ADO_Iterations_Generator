## ADO Iterations Generator

This repository contains a small .NET tool to generate and assign Azure DevOps iterations.

Registry tweak (PowerShell)

If you need to raise the service limits for team iterations/area paths on the server, run the following PowerShell commands in the appropriate lightrail for tfs:

```powershell
Set-ServiceRegistryValue -RegistryPath "/Service/Agile/Settings/MaxAllowedTeamIterations" -Value "10000"
Set-ServiceRegistryValue -RegistryPath "/Service/Agile/Settings/MaxAllowedTeamAreaPaths" -Value "10000"
```

Run these only with proper administrative permissions and understanding of server implications.
# Azure DevOps Iterations Generator

A command-line tool for batch-creating iterations (sprints) in Azure DevOps projects using the REST API.

## Features
- Create iterations in bulk for any Azure DevOps project
- Skips creation if an iteration name already exists anywhere in the project
- Supports custom start/finish dates
- Can assign iterations to teams (optional)
- Works with Azure DevOps test environments (e.g., codedev.ms)

## Prerequisites
- .NET 9.0 SDK or later
- Azure DevOps Personal Access Token (PAT) with Work Item Tracking permissions

## Setup
1. Clone the repository:
   ```pwsh
   git clone https://your-repo-url.git
   cd ADO_Iterations_Generator
   ```
2. Restore dependencies:
   ```pwsh
   dotnet restore
   ```
3. Build the project:
   ```pwsh
   dotnet build
   ```

## Usage
Run the generator with required arguments:

### Default Mode: Autogenerate Iterations
```pwsh
# Example usage
# Replace values as needed

dotnet run -- \
  --adourl https://codedev.ms \
  --organization org \
  --project prj01 \
  --pat <your-pat> \
  --prefix "Sprint" \
  --startdate 2024-08-01 \
  --count 5 \
  --length 7
```

### Assign-All-To-Team Mode
Assign all existing iterations in the project to a team:
```pwsh
dotnet run -- --adourl https://codedev.ms --organization org --project prj01 --pat <your-pat> --mode assign-all-to-team --team "My Team"
```

### Arguments
- `--adourl`         : Base Azure DevOps URL (e.g., https://codedev.ms)
- `--organization`   : Organization name
- `--project`        : Project name
- `--pat`            : Personal Access Token
- `--prefix`         : Iteration name prefix (e.g., "Sprint")
- `--startdate`      : Start date for first iteration (YYYY-MM-DD)
- `--count`          : Number of iterations to create
- `--length`         : Number of days per iteration
- `--mode`           : Operation mode (`autogenerate` or `assign-all-to-team`)
- `--team`           : Team name (required for `assign-all-to-team` mode)

### Example
Create 5 sprints, each 7 days long, starting August 1, 2024:
```pwsh
dotnet run -- --adourl https://codedev.ms --organization org --project prj01 --pat <your-pat> --prefix "Sprint" --startdate 2024-08-01 --count 5 --length 7
```

## Output
- In `autogenerate` mode, iterations are created in Azure DevOps under the specified project.
- In `assign-all-to-team` mode, all existing iterations are assigned to the specified team.
- If an iteration name already exists anywhere in the project, it is skipped.
- Console output shows progress and any errors.

## Troubleshooting
- Ensure your PAT has sufficient permissions.
- If you see 409 errors, the tool will now skip duplicate names automatically.
- For connectivity issues, check your organization/project names and PAT.

## License
MIT
