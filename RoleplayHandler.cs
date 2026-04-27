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

        // Cooldown POR comando (não global): cada comando tem seu próprio timer
        // Chave: "userId:acao" → última vez usado
        private static readonly Dictionary<string, DateTime> _cooldowns = new();

        // Tempo de cooldown: 1 HORA por comando
        private static readonly TimeSpan TempoEspera = TimeSpan.FromHours(1);

        // Cor verde estilo "sucesso"
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

                    // COOLDOWN POR USUÁRIO + AÇÃO (1 HORA)
                    string chaveCd = $"{user.Id}:{acao}";
                    if (_cooldowns.TryGetValue(chaveCd, out var lastUse))
                    {
                        var passou = DateTime.UtcNow - lastUse;
                        if (passou < TempoEspera)
                        {
                            var falta = TempoEspera - passou;
                            string tempoFormatado = FormatarTempo(falta);

                            var aviso = await msg.Channel.SendMessageAsync(
                                $"<:erro:1493078898462949526> {user.Mention}, você já usou **z{acao}** recentemente! " +
                                $"Aguarde mais **{tempoFormatado}** pra usar de novo."
                            );
                            _ = Task.Delay(5000).ContinueWith(_ => aviso.DeleteAsync());
                            return;
                        }
                    }

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

                    // Marca o cooldown só depois de validar tudo
                    _cooldowns[chaveCd] = DateTime.UtcNow;

                    await ExecutarAcao(msg, user, mencionado, acao);
                }
                catch (Exception ex) { Console.WriteLine($"[Roleplay Error]: {ex.Message}\n{ex.StackTrace}"); }
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

            // ★★★ CORRIGIDO: AGORA QUEM GANHA É QUEM USOU O COMANDO ★★★
            // Pega o saldo ANTES pra confirmar que adicionou
            long saldoAntes = EconomyHelper.GetSaldo(user.Guild.Id, user.Id);

            try
            {
                EconomyHelper.AdicionarSaldo(user.Guild.Id, user.Id, recompensa);
                EconomyHelper.RegistrarTransacao(user.Guild.Id, _client.CurrentUser.Id, user.Id, recompensa, $"ROLEPLAY_{acao.ToUpper()}");

                // Verifica se realmente foi adicionado
                long saldoDepois = EconomyHelper.GetSaldo(user.Guild.Id, user.Id);
                long diff = saldoDepois - saldoAntes;

                Console.WriteLine($"[Roleplay] {user.Username} fez '{acao}' em {alvo.Username} | Saldo: {saldoAntes} → {saldoDepois} (+{diff}) | Esperado: +{recompensa}");

                if (diff != recompensa)
                {
                    Console.WriteLine($"[Roleplay AVISO] Diferença esperada não bate! Banco pode ter problema.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roleplay AdicionarSaldo Error]: {ex.Message}\n{ex.StackTrace}");
                await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> Erro ao adicionar saldo: `{ex.Message}`");
                return;
            }

            // Monta a mensagem (quem fez ganha o dinheiro)
            string textoMsg = acao switch
            {
                "beijar" => $"<a:sucess:1494692628372132013> **Beijo apaixonado!** Ao beijar {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                "tapa" => $"<a:sucess:1494692628372132013> **Tapa amigável!** Ao dar um tapinha em {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                "abracar" => $"<a:sucess:1494692628372132013> **Abraço fortinho!** Ao abraçar {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                _ => ""
            };

            // Embed limpo: só o gif
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

        // Formata tempo restante de forma amigável
        private string FormatarTempo(TimeSpan t)
        {
            if (t.TotalMinutes < 1)
                return $"{(int)t.TotalSeconds}s";
            if (t.TotalHours < 1)
                return $"{t.Minutes}min e {t.Seconds}s";
            return $"{(int)t.TotalHours}h e {t.Minutes}min";
        }
    }
}
