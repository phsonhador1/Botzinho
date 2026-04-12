using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Cassino
{
    public class CassinoHandler
    {
        private readonly DiscordSocketClient _client;

        // Dicionário para gerenciar apostas em tempo real e evitar bugs de duplicação
        private static readonly Dictionary<ulong, long> ApostasAtivas = new();

        // LINK DO SEU GIF (O link que você mandou)
        private const string GIF_ROLETA = "https://media.discordapp.net/attachments/1161794729462214779/1168565874748309564/roletazany.gif?ex=69dd05c7&is=69dbb447&hm=5cc06ebd5f399270a152db1fbb2c1e15272adb0d3ac37dc5d6106967c5d80bad&=";

      
        public static readonly HashSet<ulong> IDsAutorizados = new HashSet<ulong>
        {
            1472642376970404002, 
        };

        public CassinoHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleCommand;
            _client.ButtonExecuted += HandleButtons;
        }

        private async Task HandleCommand(SocketMessage msg)
        {
            if (msg.Author.IsBot || msg is not SocketUserMessage userMsg) return;

            var content = msg.Content.ToLower().Trim();

            // --- COMANDO ZADDSALDO (APENAS ADMINISTRADORES AUTORIZADOS) ---
            // --- DENTRO DO HANDLECOMMAND ---

            if (content.StartsWith("zaddsaldo"))
            {
                var user = msg.Author as SocketGuildUser;
                if (user == null) return;
                var guildId = user.Guild.Id;

                // 1. Verificação de ID Autorizado
                if (!IDsAutorizados.Contains(user.Id)) return;

                // 2. Validação de argumentos
                string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length < 3)
                {
                    await msg.Channel.SendMessageAsync("❓ **Uso:** `zaddsaldo @usuario [valor]`");
                    return;
                }

                // 3. Busca do usuário alvo
                var mencionado = msg.MentionedUsers.FirstOrDefault();
                if (mencionado == null) { await msg.Channel.SendMessageAsync("❌ Mencione um usuário."); return; }

                IGuildUser alvo = user.Guild.GetUser(mencionado.Id);
                if (alvo == null) { try { alvo = await ((IGuild)user.Guild).GetUserAsync(mencionado.Id); } catch { } }
                if (alvo == null) { await msg.Channel.SendMessageAsync("❌ Usuário não encontrado."); return; }

                // --- AQUI ESTÁ A CORREÇÃO ---
                // Declaramos a variável aqui em cima para o código "conhecer" ela
                long valorFinalParaAdicionar = 0;
                string valorTexto = partes.Last().ToLower();

                // 4. Lógica de conversão (k e m)
                if (valorTexto.EndsWith("k"))
                {
                    if (double.TryParse(valorTexto.Replace("k", ""), out var vK))
                        valorFinalParaAdicionar = (long)(vK * 1000);
                }
                else if (valorTexto.EndsWith("m"))
                {
                    if (double.TryParse(valorTexto.Replace("m", ""), out var vM))
                        valorFinalParaAdicionar = (long)(vM * 1000000);
                }
                else
                {
                    long.TryParse(valorTexto, out valorFinalParaAdicionar);
                }

                // 5. Validação final e Banco de Dados
                if (valorFinalParaAdicionar <= 0)
                {
                    await msg.Channel.SendMessageAsync("❌ Valor inválido para adicionar.");
                    return;
                }

                // Adiciona no PostgreSQL
                EconomyHelper.AdicionarSaldo(guildId, alvo.Id, valorFinalParaAdicionar);

                await msg.Channel.SendMessageAsync($"✅ **Sucesso!** Foram adicionados `{EconomyHelper.FormatarSaldo(valorFinalParaAdicionar)}` cpoints para {alvo.Mention}.");
                return;
            }

            // --- OUTROS COMANDOS DO CASSINO (ZROLETA, etc.) ---
            if (content.StartsWith("zroleta"))
            {
                var user = msg.Author as SocketGuildUser;
                if (user == null) return;
                var guildId = user.Guild.Id;

                string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length < 2)
                {
                    await msg.Channel.SendMessageAsync("❓ **Uso correto:** `zroleta [valor]` ou `zroleta all`.");
                    return;
                }

                long saldoAtual = EconomyHelper.GetSaldo(guildId, user.Id);
                long valorAposta = 0;

                if (partes[1] == "all") { valorAposta = saldoAtual; }
                else
                {
                    string input = partes[1].Replace("k", "000").Replace("m", "000000");
                    if (!long.TryParse(input, out valorAposta)) { await msg.Channel.SendMessageAsync("❌ Valor de aposta inválido."); return; }
                }

                if (valorAposta <= 0) { await msg.Channel.SendMessageAsync("❌ Você precisa apostar um valor maior que zero!"); return; }
                if (saldoAtual < valorAposta) { await msg.Channel.SendMessageAsync($"❌ Saldo insuficiente. Você tem `{EconomyHelper.FormatarSaldo(saldoAtual)}`."); return; }
                if (ApostasAtivas.ContainsKey(user.Id)) { await msg.Channel.SendMessageAsync("⚠️ Termine o jogo anterior antes de começar outro!"); return; }

                ApostasAtivas[user.Id] = valorAposta;
                EconomyHelper.RemoverSaldo(guildId, user.Id, valorAposta);

                var embed = new EmbedBuilder()
                    .WithAuthor("Roleta", "https://cdn-icons-png.flaticon.com/512/1055/1055823.png")
                    .WithThumbnailUrl("https://cdn-icons-png.flaticon.com/512/1055/1055823.png")
                    .WithDescription($@"• **Olá, {user.Mention}! Bem-vindo(a) à Roleta da {_client.CurrentUser.Username}.**

💰 | **Valor em aposta:** `{EconomyHelper.FormatarSaldo(valorAposta)}`

💡 | **Como funciona:** Escolha uma cor. Se o sorteio parar nela, você ganha o prêmio!
⚪ **Branco:** 6.0x (Difícil)
⚫ **Preto:** 1.5x
🔴 **Vermelho:** 1.5x

🧧 | **Desistir da aposta:** Clique no ❌ para recuperar seu dinheiro agora.")
                    .WithFooter($"Apostador: {user.Username} • Hoje às {DateTime.Now:HH:mm}", user.GetAvatarUrl())
                    .WithColor(new Color(43, 45, 49))
                    .Build();

                var components = new ComponentBuilder()
                    .WithButton("Branco (6.0x)", $"roleta_branco_{user.Id}", ButtonStyle.Secondary, new Emoji("⚪"))
                    .WithButton("Preto (1.5x)", $"roleta_preto_{user.Id}", ButtonStyle.Secondary, new Emoji("⚫"))
                    .WithButton("Vermelho (1.5x)", $"roleta_vermelho_{user.Id}", ButtonStyle.Danger, new Emoji("🔴"))
                    .WithButton(null, $"roleta_cancel_{user.Id}", ButtonStyle.Secondary, new Emoji("❌"));

                await msg.Channel.SendMessageAsync(embed: embed, components: components.Build());
            }
        }

        private async Task HandleButtons(SocketMessageComponent component)
        {
            var customId = component.Data.CustomId;
            if (!customId.StartsWith("roleta_")) return;

            var partes = customId.Split('_');
            var escolha = partes[1];
            var userId = ulong.Parse(partes[2]);

            if (component.User.Id != userId) { await component.RespondAsync("❌ Saia daqui, essa roleta não é sua!", ephemeral: true); return; }
            if (!ApostasAtivas.TryGetValue(userId, out long valorAposta)) { await component.RespondAsync("❌ Jogo finalizado ou erro.", ephemeral: true); return; }

            var guildId = (component.User as SocketGuildUser).Guild.Id;

            // --- DESISTÊNCIA ---
            if (escolha == "cancel")
            {
                ApostasAtivas.Remove(userId);
                EconomyHelper.AdicionarSaldo(guildId, userId, valorAposta);
                await component.UpdateAsync(x => {
                    x.Content = $"✅ {component.User.Mention} desistiu e recuperou seus `{EconomyHelper.FormatarSaldo(valorAposta)}` cpoints.";
                    x.Embed = null; x.Components = null;
                });
                return;
            }

            // --- ANIMAÇÃO DE GIRO COM GIF ---
            ApostasAtivas.Remove(userId);

            var embedAnimacao = new EmbedBuilder()
                .WithAuthor("Roleta", "https://cdn-icons-png.flaticon.com/512/1055/1055823.png")
                .WithDescription("⚫ **Girando roleta...**")
                .WithImageUrl(GIF_ROLETA)
                .WithColor(new Color(43, 45, 49))
                .Build();

            await component.UpdateAsync(x => {
                x.Embed = embedAnimacao;
                x.Components = null;
            });

            // DELAY DE 4 SEGUNDOS PARA O SUSPENSE
            await Task.Delay(4000);

            // --- RESULTADO ---
            var random = new Random().Next(1, 101);
            string corSorteada;
            double multiplicador;

            if (random <= 10) { corSorteada = "branco"; multiplicador = 6.0; }
            else if (random <= 55) { corSorteada = "preto"; multiplicador = 1.5; }
            else { corSorteada = "vermelho"; multiplicador = 1.5; }

            bool ganhou = escolha == corSorteada;
            long premio = (long)(valorAposta * multiplicador);
            string emojiCor = corSorteada switch { "branco" => "⚪", "preto" => "⚫", _ => "🔴" };

            var embedFim = new EmbedBuilder()
                .WithAuthor("Resultado da Roleta", "https://cdn-icons-png.flaticon.com/512/1055/1055823.png")
                .WithFooter($"Apostador: {component.User.Username}", component.User.GetAvatarUrl())
                .WithTimestamp(DateTime.Now);

            if (ganhou)
            {
                EconomyHelper.AdicionarSaldo(guildId, userId, premio);
                embedFim.WithColor(Color.Green)
                    .WithDescription($@"<a:7moneyz:1493015410637930508> **Parabéns! O sorte passou por aqui!**

🎡 A roleta parou no: {emojiCor} **{corSorteada.ToUpper()}**
💰 Você recebeu: `{EconomyHelper.FormatarSaldo(premio)}` cpoints");
            }
            else
            {
                embedFim.WithColor(Color.Red)
                    .WithDescription($@"<a:negativo:1492950137587241114> **Não foi dessa vez...**

🎡 A roleta parou no: {emojiCor} **{corSorteada.ToUpper()}**
❌ Você perdeu: `{EconomyHelper.FormatarSaldo(valorAposta)}` cpoints");
            }

            await component.ModifyOriginalResponseAsync(x => {
                x.Embed = embedFim.Build();
                x.Content = component.User.Mention;
            });
        }
    }
}
