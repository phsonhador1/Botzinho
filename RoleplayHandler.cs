using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Botzinho.Economy;

namespace Botzinho.Roleplay
{
    public class RoleplayHandler
    {
        private readonly DiscordSocketClient _client;

        // Cooldown POR comando: cada comando tem seu próprio timer
        private static readonly Dictionary<string, DateTime> _cooldowns = new();

        // Tempo de cooldown: 1 HORA por comando
        private static readonly TimeSpan TempoEspera = TimeSpan.FromHours(1);

        // Cor verde estilo "sucesso"
        private static readonly Color VerdeSucesso = new Color(67, 181, 129);

        // Random global pro sorteio dos gifs
        private static readonly Random _random = new Random();

        // ★★★ BIBLIOTECA DE GIFS PERMANENTES - SEM API ★★★
        // URLs do Tenor que são CDN público e estáveis
        private static readonly string[] _gifsBeijo = new[]
        {
            "https://media1.tenor.com/m/JZcfg2Ahg-IAAAAd/anime-kiss.gif",
            "https://media1.tenor.com/m/4UCCuZ2QFXkAAAAd/anime-kiss.gif",
            "https://media1.tenor.com/m/I8mzJzPABMcAAAAC/anime-kiss.gif",
            "https://media1.tenor.com/m/6sPXIKDk6wgAAAAd/kiss-anime.gif",
            "https://media1.tenor.com/m/V2mfOPjNK8MAAAAC/kiss-couple.gif",
            "https://media1.tenor.com/m/HCbtRgZxEqsAAAAC/anime-kiss.gif",
            "https://media1.tenor.com/m/KUmzPAt9aYsAAAAC/anime-kiss.gif",
            "https://media1.tenor.com/m/zVK1IKlOj7IAAAAC/anime-kiss.gif",
            "https://media1.tenor.com/m/Ds0wuKyz8rYAAAAC/anime-kiss.gif",
            "https://media1.tenor.com/m/AbCxHvjk5J0AAAAC/anime-kiss.gif"
        };

        private static readonly string[] _gifsTapa = new[]
        {
            "https://media1.tenor.com/m/aPdRoavHd5oAAAAC/anime-slap.gif",
            "https://media1.tenor.com/m/Pp2k9NNB5_8AAAAC/slap-anime.gif",
            "https://media1.tenor.com/m/yC_OCIVTndsAAAAd/anime-slap.gif",
            "https://media1.tenor.com/m/Gp8wMsiWZRgAAAAC/anime-slap.gif",
            "https://media1.tenor.com/m/zIRjHpHnFVoAAAAC/anime-slap.gif",
            "https://media1.tenor.com/m/UfF0ssMTyOcAAAAC/slap-anime.gif",
            "https://media1.tenor.com/m/3qUbbgBwTQ4AAAAC/slap-anime.gif",
            "https://media1.tenor.com/m/8s_3qbfcfO0AAAAC/anime-slap.gif",
            "https://media1.tenor.com/m/RNOWOe6N4uIAAAAC/slap-anime.gif",
            "https://media1.tenor.com/m/LEZTyiAZGPsAAAAC/anime-slap.gif"
        };

        private static readonly string[] _gifsAbraco = new[]
        {
            "https://media1.tenor.com/m/kBtdh11_HHwAAAAC/hug-anime.gif",
            "https://media1.tenor.com/m/QbWAm1uW3HQAAAAC/anime-hug.gif",
            "https://media1.tenor.com/m/BPUiVnp9jkkAAAAC/anime-hug.gif",
            "https://media1.tenor.com/m/AzHfbGiV7rIAAAAC/anime-hug.gif",
            "https://media1.tenor.com/m/lXFPVZAhHN0AAAAd/anime-hug.gif",
            "https://media1.tenor.com/m/1MwPBLKnX5UAAAAC/hug-anime.gif",
            "https://media1.tenor.com/m/CHVDYmIkrukAAAAC/anime-hug.gif",
            "https://media1.tenor.com/m/srpcMWLhSKwAAAAC/anime-hug.gif",
            "https://media1.tenor.com/m/yFMgLibfMr8AAAAC/anime-hug.gif",
            "https://media1.tenor.com/m/4_NvjYiBEbsAAAAC/anime-hug.gif"
        };

        public RoleplayHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;

            Console.WriteLine($"[Roleplay] Handler INICIALIZADO! {_gifsBeijo.Length} gifs de beijo, {_gifsTapa.Length} de tapa, {_gifsAbraco.Length} de abraço");
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

                    Console.WriteLine($"[Roleplay] Comando detectado: '{content}' por {user.Username}");

                    // COOLDOWN POR USUÁRIO + AÇÃO
                    string chaveCd = $"{user.Id}:{acao}";
                    if (_cooldowns.TryGetValue(chaveCd, out var lastUse))
                    {
                        var passou = DateTime.UtcNow - lastUse;
                        if (passou < TempoEspera)
                        {
                            var falta = TempoEspera - passou;
                            string tempoFormatado = FormatarTempo(falta);

                            Console.WriteLine($"[Roleplay] {user.Username} em cooldown ({tempoFormatado} restantes)");

                            var aviso = await msg.Channel.SendMessageAsync(
                                $"<:erro:1493078898462949526> {user.Mention}, você já usou **z{acao}** recentemente! " +
                                $"Aguarde mais **{tempoFormatado}** pra usar de novo."
                            );
                            _ = Task.Delay(8000).ContinueWith(_ => aviso.DeleteAsync());
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
            // ★ Sorteia gif local (SEM API!)
            string gifUrl = SortearGif(acao);

            // Recompensa aleatória entre 50K e 500K
            long recompensa = _random.NextInt64(50_000, 500_001);

            // Adiciona o saldo
            try
            {
                long saldoAntes = EconomyHelper.GetSaldo(user.Guild.Id, user.Id);
                EconomyHelper.AdicionarSaldo(user.Guild.Id, user.Id, recompensa);
                EconomyHelper.RegistrarTransacao(user.Guild.Id, _client.CurrentUser.Id, user.Id, recompensa, $"ROLEPLAY_{acao.ToUpper()}");
                long saldoDepois = EconomyHelper.GetSaldo(user.Guild.Id, user.Id);

                Console.WriteLine($"[Roleplay] {user.Username} fez '{acao}' em {alvo.Username} | Saldo: {saldoAntes} → {saldoDepois} (+{saldoDepois - saldoAntes})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roleplay AdicionarSaldo Error]: {ex.Message}");
                await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> Erro ao adicionar saldo: `{ex.Message}`");
                return;
            }

            // Monta a mensagem
            string textoMsg = acao switch
            {
                "beijar" => $"<a:sucess:1494692628372132013> **Beijo apaixonado!** Ao beijar {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                "tapa" => $"<a:sucess:1494692628372132013> **Tapa amigável!** Ao dar um tapinha em {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                "abracar" => $"<a:sucess:1494692628372132013> **Abraço fortinho!** Ao abraçar {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                _ => ""
            };

            try
            {
                var embed = new EmbedBuilder()
                    .WithColor(VerdeSucesso)
                    .WithImageUrl(gifUrl)
                    .Build();

                await msg.Channel.SendMessageAsync(text: textoMsg, embed: embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roleplay SendMessage Error]: {ex.Message}");
                // Última tentativa: manda só o texto
                try { await msg.Channel.SendMessageAsync(text: textoMsg); } catch { }
            }
        }

        // Sorteia gif aleatório da lista local
        private string SortearGif(string acao)
        {
            string[] lista = acao switch
            {
                "beijar" => _gifsBeijo,
                "tapa" => _gifsTapa,
                "abracar" => _gifsAbraco,
                _ => _gifsAbraco
            };

            return lista[_random.Next(lista.Length)];
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
