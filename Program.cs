using System;
using System.Threading.Tasks;

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

        // simple hidden input
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