using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace Botzinho.Commands
{
    public static class BinService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task ExecutarZBinAsync(SocketUserMessage message, string bin)
        {
            string cleanBin = new string(bin.Where(char.IsDigit).ToArray());

            if (cleanBin.Length < 6)
            {
                await message.ReplyAsync("<:erro:1493078898462949526> **Deixa de ser burro animal:** Insira pelo menos 6 dígitos.");
                return;
            }

            if (cleanBin.Length > 8) cleanBin = cleanBin.Substring(0, 8);

            using (message.Channel.EnterTypingState())
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, $"https://lookup.binlist.net/{cleanBin}");
                    request.Headers.Add("Accept-Version", "3");

                    var response = await _httpClient.SendAsync(request);

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        await message.ReplyAsync("⚠️ **Alvo Inexistente:** BIN não localizada.");
                        return;
                    }

                    if ((int)response.StatusCode == 429)
                    {
                        await message.ReplyAsync("⏳ **Firewall Ativo:** Muitas consultas. Tente novamente em breve.");
                        return;
                    }

                    response.EnsureSuccessStatusCode();
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<BinResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (data == null) return;

                    // Tratamento de Dados Premium
                    string scheme = Capitalize(data.Scheme) ?? "Desconhecido";
                    string type = Capitalize(data.Type) ?? "N/A";
                    string brand = Capitalize(data.Brand) ?? "Common";
                    string bankName = data.Bank?.Name?.ToUpper() ?? "DESCONHECIDO";
                    string countryName = data.Country?.Name ?? "Global";
                    string countryCode = data.Country?.Alpha2 ?? "??";

                    // Montagem do Embed Estilo Cyberpunk/Minimalista
                    var embed = new EmbedBuilder()
                        .WithTitle($"🔍 Consulta: {cleanBin.Substring(0, 6)}")
                        .WithColor(new Color(138, 43, 226)) // Roxo Neon (Violeta)
                        .AddField("💳 BANDEIRA", $"`{scheme}`", true)
                        .AddField("📂 TIPO", $"`{type}`", true)
                        .AddField("✨ NÍVEL", $"`{brand}`", true)
                        .AddField("🏢 BANCO", $"```fix\n{bankName}```", false) // Bloco em destaque para o banco
                        .AddField("📍 PAÍS", $"{countryName} ({countryCode})", true)
                        .AddField("🛡️ STATUS", "Verificado", true)
                        .WithCurrentTimestamp()
                        .Build();

                    await message.ReplyAsync(embed: embed);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro Bin: {ex.Message}");
                    await message.ReplyAsync($"<:erro:1493078898462949526> **Falha na Matrix:** Tente novamente.");
                }
            }
        }

        private static string Capitalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return char.ToUpper(text[0]) + text.Substring(1).ToLower();
        }
    }

    // Classes de mapeamento JSON
    public class BinResponse
    {
        public BinNumber Number { get; set; }
        public string Scheme { get; set; }
        public string Type { get; set; }
        public string Brand { get; set; }
        public bool? Prepaid { get; set; }
        public BinCountry Country { get; set; }
        public BinBank Bank { get; set; }
    }

    public class BinNumber { public int? Length { get; set; } public bool? Luhn { get; set; } }
    public class BinCountry { public string Name { get; set; } public string Alpha2 { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } }
    public class BinBank { public string Name { get; set; } public string Url { get; set; } public string Phone { get; set; } }
}
