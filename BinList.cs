using System;
using System.Collections.Generic;
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

        // Cache simples para evitar consultas repetidas e economizar API
        private static readonly Dictionary<string, HandyBinResponse> _cache = new Dictionary<string, HandyBinResponse>();

        // COLOQUE SUA CHAVE AQUI
        private static readonly string _apiKey = "SUA_KEY_AQUI";

        public static async Task ExecutarZBinAsync(SocketUserMessage message, string bin)
        {
            string cleanBin = new string(bin.Where(char.IsDigit).ToArray());

            if (cleanBin.Length < 6)
            {
                await message.ReplyAsync("<:erro:1493078898462949526> **Carga falhou:** Insira pelo menos 6 dígitos.");
                return;
            }

            string binParaBusca = cleanBin.Substring(0, 6);

            using (message.Channel.EnterTypingState())
            {
                try
                {
                    HandyBinResponse data;

                    // 1. Verifica se já temos no Cache
                    if (_cache.ContainsKey(binParaBusca))
                    {
                        data = _cache[binParaBusca];
                    }
                    else
                    {
                        // 2. Se não tiver, busca na HandyAPI
                        var request = new HttpRequestMessage(HttpMethod.Get, $"https://data.handyapi.com/bin/{binParaBusca}");
                        request.Headers.Add("x-api-key", _apiKey);

                        var response = await _httpClient.SendAsync(request);

                        if (!response.IsSuccessStatusCode)
                        {
                            await message.ReplyAsync("⏳ **Sistema Indisponível:** Limite atingido ou chave inválida.");
                            return;
                        }

                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        data = JsonSerializer.Deserialize<HandyBinResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (data == null || data.Status != "SUCCESS")
                        {
                            await message.ReplyAsync("⚠️ **Alvo Inexistente:** BIN não encontrada na base de dados.");
                            return;
                        }

                        // Salva no cache para a próxima vez
                        _cache[binParaBusca] = data;
                    }

                    // 3. Montagem do Embed Premium
                    var embed = new EmbedBuilder()
                        .WithTitle($"🔍 CONSULTA: {binParaBusca}")
                        .WithColor(new Color(138, 43, 226)) // Roxo Neon
                        .AddField("💳 BANDEIRA", $"`{data.Scheme ?? "N/A"}`", true)
                        .AddField("📂 TIPO", $"`{data.Type ?? "N/A"}`", true)
                        .AddField("✨ NÍVEL", $"`{data.CardTier ?? "N/A"}`", true)
                        .AddField("🏢 BANCO", $"```fix\n{(data.Issuer ?? "DESCONHECIDO")}\n```", false)
                        .AddField("📍 PAÍS", $"{data.Country?.Name} ({data.Country?.A2})", true)
                        .AddField("🛡️ STATUS", "Verificado", true)
                        .WithCurrentTimestamp()
                        .Build();

                    await message.ReplyAsync(embed: embed);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro HandyAPI: {ex.Message}");
                    await message.ReplyAsync($"<:erro:1493078898462949526> **Falha na Matrix:** Conexão recusada.");
                }
            }
        }
    }

    // Classes de Mapeamento para HandyAPI
    public class HandyBinResponse
    {
        public string Status { get; set; }
        public string Scheme { get; set; }
        public string Type { get; set; }
        public string Issuer { get; set; }
        public string CardTier { get; set; }
        public HandyCountry Country { get; set; }
    }

    public class HandyCountry
    {
        public string A2 { get; set; }
        public string Name { get; set; }
    }
}
