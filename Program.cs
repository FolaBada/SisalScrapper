// Program.cs (SisalScraper)
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SisalScraper
{
    internal static class Program
    {
        // Choose your default rotation order here (same idea as Domus)
        private static readonly string[] DefaultSports = new[]
        {
            "american football",
            "Basket",
            "Calcio",
            "baseball",
            "ice hockey",
            "Tennis",
            // "rugby" // add if needed; keep names matching Sisal site filters
        };

        public static async Task Main(string[] args)
        {
            // Resolve sports list (comma-separated arg to override)
            var sports = (args?.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                ? args[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : DefaultSports;

            Console.WriteLine("[Startup] Sisal sports queue: " + string.Join(", ", sports));
            Console.WriteLine("Press Ctrl+C to stop.\n");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, __) =>
            {
                // exit immediately (consistent with your Domus runner behavior)
                Environment.Exit(130);
            };

            var scraper = new SisalScraper(); // uses your existing implementation

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    foreach (var sport in sports)
                    {
                        if (cts.IsCancellationRequested) break;

                        Console.WriteLine($"\n==== [{DateTime.Now:HH:mm:ss}] SISAL START {sport} ====");
                        try
                        {
                            await scraper.RunAsync(sport); // your current method
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ERROR][Sisal] {sport}: {ex.Message}");
                        }
                        Console.WriteLine($"==== [{DateTime.Now:HH:mm:ss}] SISAL END {sport} ====\n");
                    }
                }
            }
            finally
            {
                Console.WriteLine("Sisal loop stopped.");
            }
        }
    }
}
