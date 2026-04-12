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

        // Dicionário para gerenciar apostas em tempo real e evitar bugs de duplicação de saldo
        private static readonly Dictionary<ulong, long> ApostasAtivas = new();

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

            if (content.StartsWith("zroleta"))
            {
                var user = msg.Author as SocketGuildUser;
                if (user == null) return;
                var guildId = user.Guild.Id;

                // 1. Tratamento do valor da aposta
                string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length < 2)
                {
                    await msg.Channel.SendMessageAsync("❓ **Uso:** `zroleta [valor]` ou `zroleta all`.");
                    return;
                }

                long saldoAtual = EconomyHelper.GetSaldo(guildId, user.Id);
                long valorAposta = 0;

                if (partes[1] == "all")
                {
                    valorAposta = saldoAtual;
                }
                else
                {
                    string input = partes[1].Replace("k", "000").Replace("m", "000000");
                    if (!long.TryParse(input, out valorAposta))
                    {
                        await msg.Channel.SendMessageAsync("❌ Valor de aposta inválido.");
                        return;
                    }
                }

                // 2. Validações de Segurança
                if (valorAposta <= 0)
                {
                    await msg.Channel.SendMessageAsync("❌ Você não pode apostar o vento, coloque um valor válido!");
                    return;
                }
                if (saldoAtual < valorAposta)
                {
                    await msg.Channel.SendMessageAsync($"❌ Saldo insuficiente. Você possui apenas `{EconomyHelper.FormatarSaldo(saldoAtual)}`.");
                    return;
                }
                if (ApostasAtivas.ContainsKey(user.Id))
                {
                    await msg.Channel.SendMessageAsync("⚠️ Termine sua rodada atual antes de abrir outra!");
                    return;
                }

                // 3. Bloqueio de Saldo (Retira antes de começar para evitar trapaças)
                ApostasAtivas[user.Id] = valorAposta;
                EconomyHelper.RemoverSaldo(guildId, user.Id, valorAposta);

                // 4. Construção do Painel Profissional
                var embed = new EmbedBuilder()
                    .WithAuthor("Roleta", "https://cdn-icons-png.flaticon.com/512/1055/1055823.png")
                    .WithThumbnailUrl("https://cdn-icons-png.flaticon.com/512/1055/1055823.png")
                    .WithDescription($@"• **Olá, {user.Mention}! Bem-vindo(a) à Roleta da {_client.CurrentUser.Username}.**

💰 | **Valor em aposta:** `{EconomyHelper.FormatarSaldo(valorAposta)}`

💡 | **Como funciona:**
Ao escolher uma cor abaixo, representada pelos botões, você terá a chance de ganhar com base nos multiplicadores. Cada cor tem seu próprio multiplicador. Se a roleta parar na cor escolhida, você receberá uma recompensa de acordo com o multiplicador correspondente.

🧧 | **Desistir da aposta:**
Se decidir não continuar, clique no ❌ para desistir da aposta.")
                    .WithFooter($"Apostador: {user.Username} • Hoje às {DateTime.Now:HH:mm}", user.GetAvatarUrl())
                    .WithColor(new Color(43, 45, 49)) // Cor Dark do Discord
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
            var escolha = partes[1]; // branco, preto, vermelho ou cancel
            var userId = ulong.Parse(partes[2]);

            // Valida se quem clicou é o dono da aposta
            if (component.User.Id != userId)
            {
                await component.RespondAsync("❌ Saia daqui, essa roleta não é sua!", ephemeral: true);
                return;
            }

            if (!ApostasAtivas.TryGetValue(userId, out long valorAposta))
            {
                await component.RespondAsync("❌ Essa aposta expirou ou já foi finalizada.", ephemeral: true);
                return;
            }

            var guildId = (component.User as SocketGuildUser).Guild.Id;

            // 5. Opção Desistir
            if (escolha == "cancel")
            {
                ApostasAtivas.Remove(userId);
                EconomyHelper.AdicionarSaldo(guildId, userId, valorAposta); // Devolve o dinheiro apostado

                await component.UpdateAsync(x => {
                    x.Content = $"✅ {component.User.Mention} desistiu da rodada e recuperou seus `{EconomyHelper.FormatarSaldo(valorAposta)}` cpoints.";
                    x.Embed = null;
                    x.Components = null;
                });
                return;
            }

            // 6. Lógica do Giro (Probabilidades: Branco 10%, Preto 45%, Vermelho 45%)
            ApostasAtivas.Remove(userId);

            var sorteio = new Random().Next(1, 101);
            string corGanhadora;
            double multiplicador;

            if (sorteio <= 10) { corGanhadora = "branco"; multiplicador = 6.0; }
            else if (sorteio <= 55) { corGanhadora = "preto"; multiplicador = 1.5; }
            else { corGanhadora = "vermelho"; multiplicador = 1.5; }

            bool venceu = escolha == corGanhadora;
            long resultadoFinal = (long)(valorAposta * multiplicador);
            string emojiCor = corGanhadora switch { "branco" => "⚪", "preto" => "⚫", _ => "🔴" };

            var embedFim = new EmbedBuilder()
                .WithAuthor("Resultado da Roleta", "https://cdn-icons-png.flaticon.com/512/1055/1055823.png")
                .WithFooter($"Apostador: {component.User.Username}", component.User.GetAvatarUrl())
                .WithTimestamp(DateTime.Now);

            if (venceu)
            {
                EconomyHelper.AdicionarSaldo(guildId, userId, resultadoFinal);
                embedFim.WithColor(Color.Green)
                    .WithDescription($@"🎊 **Parabéns! O sorte passou por aqui!**

🎡 A roleta parou no: {emojiCor} **{corGanhadora.ToUpper()}**
💰 Você recebeu: `{EconomyHelper.FormatarSaldo(resultadoFinal)}` cpoints");
            }
            else
            {
                embedFim.WithColor(Color.Red)
                    .WithDescription($@"💸 **Não foi dessa vez...**

🎡 A roleta parou no: {emojiCor} **{corGanhadora.ToUpper()}**
❌ Você perdeu: `{EconomyHelper.FormatarSaldo(valorAposta)}` cpoints");
            }

            await component.UpdateAsync(x => {
                x.Embed = embedFim.Build();
                x.Components = null;
                x.Content = component.User.Mention;
            });
        }
    }
}
