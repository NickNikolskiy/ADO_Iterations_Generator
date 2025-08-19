using System;
using System.Globalization;

namespace IterationGenerator
{
    public class CliConfig
    {
        public string ADOUrl { get; set; } = "https://dev.azure.com";
        public string Organization { get; set; } = "";
        public string Project { get; set; } = "";
    // Team may be null if not provided
    // (single definition kept below)
        public string Pat { get; set; } = "";
        public string ApiVersion { get; set; } = "7.1";
    public string Mode { get; set; } = "autogenerate";
        public int Depth { get; set; } = 1;
        public int ChildrenPerLevel { get; set; } = 1;
        public string BaseName { get; set; } = "Sprint";
        public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
        public int DaysPerIteration { get; set; } = 14;
    // Team may be null if not provided
    public string? Team { get; set; } = null;

    public static CliConfig? Parse(string[] args)
        {
            // Very small parser for common flags. Use sensible defaults.
            var cfg = new CliConfig();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                Console.WriteLine(a);
                switch (a.ToLowerInvariant())
                {
                    case "--adourl":
                    case "--ado-url":
                        cfg.ADOUrl = GetArg(args, ++i);
                        break;
                    case "--org":
                    case "--organization":
                        cfg.Organization = GetArg(args, ++i);
                        break;
                    case "--project":
                    case "--proj":
                        cfg.Project = GetArg(args, ++i);
                        break;
                    case "--team":
                    case "--teamname":
                        cfg.Team = GetArg(args, ++i);
                        break;
                    case "--depth":
                        cfg.Depth = int.Parse(GetArg(args, ++i));
                        break;
                    case "--children":
                    case "--count":
                        cfg.ChildrenPerLevel = int.Parse(GetArg(args, ++i));
                        break;
                    case "--basename":
                    case "--prefix":
                        cfg.BaseName = GetArg(args, ++i);
                        break;
                    case "--start":
                    case "--startdate":
                        cfg.StartDate = DateTime.Parse(GetArg(args, ++i), CultureInfo.InvariantCulture);
                        break;
                    case "--days":
                    case "--length":
                        cfg.DaysPerIteration = int.Parse(GetArg(args, ++i));
                        break;
                    case "--pat":
                        cfg.Pat = GetArg(args, ++i);
                        break;
                    case "--apiversion":
                        cfg.ApiVersion = GetArg(args, ++i);
                        break;
                    case "--mode":
                        cfg.Mode = GetArg(args, ++i);
                        break;
                    default:
                        Console.WriteLine($"Unknown arg: {a}");
                        continue;
                }
            }

            if (string.IsNullOrWhiteSpace(cfg.Organization) || string.IsNullOrWhiteSpace(cfg.Project))
            {
                Console.WriteLine("organization and project are required.");
                return null;
            }

            return cfg;
        }

        private static string GetArg(string[] args, int idx)
        {
            if (idx >= args.Length) throw new ArgumentException("Missing argument value");
            return args[idx];
        }

        public static void PrintUsage()
        {
            Console.WriteLine("Usage: IterationGenerator --org <org> --project <project> [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --team <team>             Optional team name to register team iterations");
            Console.WriteLine("  --depth <n>               Depth of iteration tree (default 1)");
            Console.WriteLine("  --children <n>            Number of children per level (default 1)");
            Console.WriteLine("  --basename <name>         Base name for iterations (default 'Sprint')");
            Console.WriteLine("  --start <yyyy-MM-dd>      Start date for first iteration (UTC) (default today)");
            Console.WriteLine("  --days <n>                Days per iteration (default 14)");
            Console.WriteLine("  --pat <pat>               Personal Access Token (or set AZURE_DEVOPS_PAT env var)");
            Console.WriteLine("  --apiversion <ver>        API version (default 7.1)");
                Console.WriteLine("  --mode <autogenerate|assign-all-to-team>  Mode of operation (default autogenerate)");
        }
    }
}