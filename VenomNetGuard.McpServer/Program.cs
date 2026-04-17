using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VenomNetGuard.McpServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await Console.Error.WriteLineAsync("[VenomNetGuard MCP] Server nastartován...");
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) => { e.Cancel = true; cts.Cancel(); };

            await RunMessageLoopAsync(cts.Token);
        }

        static async Task RunMessageLoopAsync(CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), Console.InputEncoding);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;

                if (!string.IsNullOrWhiteSpace(line))
                {
                    await ProcessMessageAsync(line);
                }
            }
        }

        static async Task ProcessMessageAsync(string jsonMessage)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonMessage);
                var root = doc.RootElement;

                if (root.TryGetProperty("method", out var methodElement))
                {
                    string method = methodElement.GetString();
                    int? id = root.TryGetProperty("id", out var idElem) ? idElem.GetInt32() : null;

                    if (method == "tools/list")
                    {
                        await SendToolsListResponse(id);
                    }
                    else if (method == "tools/call")
                    {
                        var paramsElement = root.GetProperty("params");
                        string toolName = paramsElement.GetProperty("name").GetString();

                        if (toolName == "get_venom_alerts")
                        {
                            await HandleGetVenomAlerts(id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[ERROR] Chyba zpracování: {ex.Message}");
            }
        }

        static async Task SendToolsListResponse(int? id)
        {
            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    tools = new[]
                    {
                        new
                        {
                            name = "get_venom_alerts",
                            description = "Získá aktuální bezpečnostní incidenty a hrozby z databáze Venom Net Guard, která v reálném čase monitoruje systém.",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new {},
                                required = new string[] {}
                            }
                        }
                    }
                }
            };

            Console.WriteLine(JsonSerializer.Serialize(response));
        }

        static async Task HandleGetVenomAlerts(int? id)
        {
            string resultText;

            try
            {
                // V produkci budou obě .exe pravděpodobně ve stejné složce.
                // Pro vývoj (kdy běží z různých bin/Debug složek) zkusíme najít cestu k hlavní aplikaci.
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string devPath = Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", "..", "VenomNetGuard", "bin", "Debug", "net8.0-windows", "nexus_data.json"));
                string prodPath = Path.Combine(currentDir, "nexus_data.json");

                string targetFilePath = File.Exists(prodPath) ? prodPath : devPath;

                if (File.Exists(targetFilePath))
                {
                    // Přečteme JSON vytvořený hlavní WPF aplikací
                    string jsonData = await File.ReadAllTextAsync(targetFilePath);
                    resultText = $"Data z Venom Net Guard:\n{jsonData}";
                }
                else
                {
                    resultText = "Bezpečnostní log Venom Net Guard je aktuálně prázdný nebo hlavní aplikace ještě nezaznamenala žádné události (soubor nexus_data.json nenalezen).";
                }
            }
            catch (Exception ex)
            {
                resultText = $"Chyba při čtení lokální databáze: {ex.Message}";
            }

            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = new
                {
                    content = new[]
                    {
                        new { type = "text", text = resultText }
                    }
                }
            };

            Console.WriteLine(JsonSerializer.Serialize(response));
        }
    }
}