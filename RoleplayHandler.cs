using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Botzinho.Economy;

namespace Botzinho.Roleplay
{
    public class RoleplayHandler
    {
        private readonly DiscordSocketClient _client;
        private static readonly HttpClient _http = new HttpClient();
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();

        // Cor verde estilo "sucesso" do print
        private static readonly Color VerdeSucesso = new Color(67, 181, 129);

        public RoleplayHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
        }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () => {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage) return;
                    var user = msg.Author as SocketGuildUser;
                    if (user == null) return;

                    var content = msg.Content.Trim();
                    var contentLower = content.ToLower();

                    string acao = null;
                    if (contentLower.StartsWith("zbeijar") || contentLower.StartsWith("zbeijo"))
                        acao = "beijar";
                    else if (contentLower.StartsWith("ztapa") || contentLower.StartsWith("zslap"))
                        acao = "tapa";
                    else if (contentLower.StartsWith("zabracar") || contentLower.StartsWith("zabraco") || contentLower.StartsWith("zhug"))
                        acao = "abracar";
                    else
                        return;

                    // Cooldown 5s
                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 5)
                    {
                        var aviso = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> {user.Mention}, calma! Aguarde **5 segundos** entre comandos.");
                        _ = Task.Delay(2500).ContinueWith(_ => aviso.DeleteAsync());
                        return;
                    }
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    // Pega o mencionado
                    var mencionado = msg.MentionedUsers.FirstOrDefault() as SocketGuildUser;
                    if (mencionado == null)
                    {
                        await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, mencione alguém! Exemplo: `z{acao} @fulano`");
                        return;
                    }

                    if (mencionado.Id == user.Id)
                    {
                        await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, você não pode fazer isso consigo mesmo!");
                        return;
                    }

                    if (mencionado.IsBot)
                    {
                        await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, não posso participar disso, sou apenas um bot!");
                        return;
                    }

                    await ExecutarAcao(msg, user, mencionado, acao);
                }
                catch (Exception ex) { Console.WriteLine($"[Roleplay Error]: {ex.Message}"); }
            });
            return Task.CompletedTask;
        }

        private async Task ExecutarAcao(SocketMessage msg, SocketGuildUser user, SocketGuildUser alvo, string acao)
        {
            // Pega gif
            string endpoint = acao switch
            {
                "beijar" => "kiss",
                "tapa" => "slap",
                "abracar" => "hug",
                _ => "hug"
            };

            string gifUrl = await BuscarGifAsync(endpoint);

            // Recompensa aleatória entre 50K e 500K
            var random = new Random();
            long recompensa = random.NextInt64(50_000, 500_001);

            // Adiciona o saldo no mencionado (quem recebeu a ação)
            try
            {
                EconomyHelper.AdicionarSaldo(user.Guild.Id, alvo.Id, recompensa);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roleplay AdicionarSaldo Error]: {ex.Message}");
            }

            // Monta a mensagem estilo casual
            string textoMsg = acao switch
            {
                "beijar" => $"<a:sucess:1494692628372132013> **Beijo apaixonado!** Ao beijar {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:cash:0000> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                "tapa" => $"<a:sucess:1494692628372132013> **Tapa amigável!** Ao dar um tapinha em {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:cash:0000> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                "abracar" => $"<a:sucess:1494692628372132013> **Abraço fortinho!** Ao abraçar {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:cash:0000> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                _ => ""
            };

            // Embed bem clean: só o gif, sem título nem descrição
            var embed = new EmbedBuilder()
                .WithColor(VerdeSucesso)
                .WithImageUrl(gifUrl ?? "")
                .Build();

            await msg.Channel.SendMessageAsync(text: textoMsg, embed: embed);
        }

        private async Task<string> BuscarGifAsync(string endpoint)
        {
            try
            {
                string url = $"https://api.waifu.pics/sfw/{endpoint}";
                var response = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                return doc.RootElement.GetProperty("url").GetString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BuscarGif Error]: {ex.Message}");
                return null;
            }
        }
    }
}