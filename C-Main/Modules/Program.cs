using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MainbotCSharp
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            if (File.Exists(envFile))
            {
                LoadEnvironmentFromFile(envFile);
                Console.WriteLine("✅ Environment variables loaded from .env file");
            }
            else
            {
                Console.WriteLine("⚠️  No .env file found, using system environment variables");
            }

            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<Bot>();
                })
                .UseConsoleLifetime();

            var host = builder.Build();

            var bot = host.Services.GetRequiredService<Bot>();
            await bot.InitializeAsync();

            await host.RunAsync();
        }

        private static void LoadEnvironmentFromFile(string filePath)
        {
            try
            {
                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2)
                        continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (value.StartsWith("\"") && value.EndsWith("\""))
                        value = value[1..^1];

                    Environment.SetEnvironmentVariable(key, value);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading .env file: {ex.Message}");
            }
        }
    }
}
