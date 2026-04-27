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
            // Filtra apenas números
            string cleanBin = new string(bin.Where(char.IsDigit).ToArray());

            if (cleanBin.Length < 6)
            {
                await message.ReplyAsync("<:erro:1493078898462949526> **Erro:** Digite ao menos os 6 primeiros dígitos da BIN.");
                return;
            }

            // Pega apenas os primeiros 8 dígitos (limite da API)
            if (cleanBin.Length > 8) cleanBin = cleanBin.Substring(0, 8);

            using (message.Channel.EnterTypingState())
            {
                try
                {
                    // A API BinList exige essa configuração de Header em alguns casos
                    var request = new HttpRequestMessage(HttpMethod.Get, $"https://lookup.binlist.net/{cleanBin}");
                    request.Headers.Add("Accept-Version", "3");

                    var response = await _httpClient.SendAsync(request);

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        await message.ReplyAsync("❌ **BIN não encontrada** na base de dados mundial.");
                        return;
                    }

                    if (response.StatusCode == (System.Net.HttpStatusCode)429)
                    {
                        await message.ReplyAsync("⏳ **Limite atingido:** A API está recusando conexões por excesso de uso. Tente em 1 minuto.");
                        return;
                    }

                    response.EnsureSuccessStatusCode();

                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<BinResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (data == null) throw new Exception("Falha na desserialização dos dados.");

                    // Formatação de strings para evitar nulos
                    string scheme = Capitalize(data.Scheme) ?? "Desconhecido";
                    string type = Capitalize(data.Type) ?? "Desconhecido";
                    string brand = Capitalize(data.Brand) ?? "Comum";
                    string prepaid = data.Prepaid.HasValue ? (data.Prepaid.Value ? "Sim" : "Não") : "N/A";

                    string bankInfo = data.Bank != null
                        ? $"**Nome:** {data.Bank.Name ?? "?"}\n**Site:** {data.Bank.Url ?? "-"}\n**Fone:** {data.Bank.Phone ?? "-"}"
                        : "Informações indisponíveis";

                    string countryInfo = data.Country != null
                        ? $"{data.Country.Alpha2} {data.Country.Name} (Lat: {data.Country.Latitude}, Long: {data.Country.Longitude})"
                        : "Desconhecido";

                    var embed = new EmbedBuilder()
                        .WithTitle($"🔍 Resultado da BIN: {cleanBin.Substring(0, 6)}")
                        .WithColor(new Color(114, 137, 218)) // Azul Discord
                        .AddField("💳 Cartão", $"**Bandeira:** {scheme}\n**Tipo:** {type}\n**Nível:** {brand}\n**Pré-pago:** {prepaid}", true)
                        .AddField("🌍 País", countryInfo, true)
                        .AddField("🏦 Banco", bankInfo, false)
                        .WithFooter("Dados providos por BinList API")
                        .WithCurrentTimestamp()
                        .Build();

                    await message.ReplyAsync(embed: embed);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro BinService: {ex.Message}");
                    await message.ReplyAsync($"⚠️ **Erro Interno:** Ocorreu um problema ao processar sua consulta.");
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