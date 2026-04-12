using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Cassino
{
    public class CassinoHandler
    {
        private readonly DiscordSocketClient _client;

        public CassinoHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
            _client.ButtonExecuted += HandleButton;
        }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () =>
            {
                try { await ProcessarMensagem(msg); }
                catch (Exception ex) { Console.WriteLine($"[Cassino] Erro Msg: {ex.Message}"); }
            });
            return Task.CompletedTask;
        }

        private async Task ProcessarMensagem(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;
            if (msg is not SocketUserMessage userMsg) return;
            var user = msg.Author as SocketGuildUser;
            if (user == null) return;

            var content = msg.Content.ToLower().Trim();
            var guildId = user.Guild.Id;

            // --- JOGO DA ROLETA ---
            if (content.StartsWith("zroleta"))
            {
                var partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length != 2)
                {
                    await msg.Channel.SendMessageAsync("Uso correto: `zroleta [valor]` ou `zroleta all`");
                    return;
                }

                var saldoAtual = EconomyHelper.GetSaldo(guildId, user.Id);
                long valorAposta = 0;

                if (partes[1] == "all" || partes[1] == "tudo")
                {
                    valorAposta = saldoAtual;
                }
                else if (!long.TryParse(partes[1], out valorAposta))
                {
                    await msg.Channel.SendMessageAsync("Valor de aposta inválido.");
                    return;
                }

                if (valorAposta <= 0)
                {
                    await msg.Channel.SendMessageAsync("O valor da aposta deve ser maior que zero.");
                    return;
                }

                if (saldoAtual < valorAposta)
                {
                    await msg.Channel.SendMessageAsync($"Você não tem cpoints suficientes! Seu saldo: `{EconomyHelper.FormatarSaldo(saldoAtual)}`");
                    return;
                }

                // Remove o dinheiro antecipadamente
                EconomyHelper.RemoverSaldo(guildId, user.Id, valorAposta);

                var embed = new EmbedBuilder()
                    .WithTitle("🎰 Roleta")
                    .WithDescription(
                        $"• **Olá, {user.Mention}! Bem-vindo(a) à Roleta da {_client.CurrentUser.Username}.**\n\n" +
                        $"💸 | **Valor em aposta:** `{EconomyHelper.FormatarSaldo(valorAposta)}`\n\n" +
                        $"💡 | **Como funciona:**\n" +
                        "Ao escolher uma cor abaixo, representada pelos botões, você terá a chance de ganhar com base nos multiplicadores. Cada cor tem seu próprio multiplicador. Se a roleta parar na cor escolhida, você receberá uma recompensa de acordo com o multiplicador correspondente.\n\n" +
                        $"🛑 | **Desistir da aposta:**\n" +
                        "Se decidir não continuar, clique no ❌ para desistir da aposta."
                    )
                    .WithThumbnailUrl("https://i.imgur.com/gK9JXXh.png")
                    .WithColor(new Discord.Color(80, 0, 80))
                    .WithFooter($"Apostador: {user.Username} • Hoje às {DateTime.Now:HH:mm}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .Build();

                // Salvamos os dados DENTRO do botão (ID_Usuario + Valor)
                var configAposta = $"{user.Id}_{valorAposta}";
                var botoes = new ComponentBuilder()
                    .WithButton("Branco (6.0x)", $"rol_branco_{configAposta}", ButtonStyle.Secondary, new Emoji("⚪"))
                    .WithButton("Preto (1.5x)", $"rol_preto_{configAposta}", ButtonStyle.Secondary, new Emoji("⚫"))
                    .WithButton("Vermelho (1.5x)", $"rol_vermelho_{configAposta}", ButtonStyle.Danger, new Emoji("🔴"))
                    .WithButton("", $"rol_cancelar_{configAposta}", ButtonStyle.Secondary, new Emoji("❌"))
                    .Build();

                await msg.Channel.SendMessageAsync(embed: embed, components: botoes);
            }
        }

        private async Task HandleButton(SocketMessageComponent component)
        {
            var customId = component.Data.CustomId;
            var guildId = ((SocketGuildUser)component.User).Guild.Id;

            if (!customId.StartsWith("rol_")) return;

            // Decodifica a aposta salva no botão (rol_cor_userid_valor)
            var partes = customId.Split('_');
            if (partes.Length != 4) return;

            string corEscolhida = partes[1]; // branco, preto, vermelho ou cancelar
            ulong donoApostaId = ulong.Parse(partes[2]);
            long valorAposta = long.Parse(partes[3]);

            if (component.User.Id != donoApostaId)
            {
                await component.RespondAsync("Você não pode clicar na aposta de outra pessoa!", ephemeral: true);
                return;
            }

            // Desativa os botões criando uma cópia cinza deles
            var botoesDesativados = new ComponentBuilder();
            foreach (var messageComponent in component.Message.Components)
            {
                if (messageComponent is ActionRowComponent actionRow)
                {
                    var rowBuilder = new ActionRowBuilder();
                    foreach (var innerComp in actionRow.Components)
                    {
                        if (innerComp is ButtonComponent btn)
                        {
                            rowBuilder.AddComponent(btn.ToBuilder().WithDisabled(true));
                        }
                    }
                    botoesDesativados.AddRow(rowBuilder);
                }
            }

            // CANCELAR APOSTA
            if (corEscolhida == "cancelar")
            {
                EconomyHelper.AdicionarSaldo(guildId, donoApostaId, valorAposta);

                var embedCancelado = component.Message.Embeds.First().ToEmbedBuilder()
                    .WithDescription($"❌ | {component.User.Mention} desistiu da aposta. O valor de `{EconomyHelper.FormatarSaldo(valorAposta)}` foi devolvido para a carteira.")
                    .WithColor(Color.Red)
                    .Build();

                await component.UpdateAsync(m => { m.Embed = embedCancelado; m.Components = botoesDesativados.Build(); });
                return;
            }

            // LÓGICA DE GIRAR A ROLETA
            var random = new Random();
            int numeroCorte = random.Next(1, 101); // 1 a 100

            string corSorteada;
            double multiplicadorSorteado = 0;
            string emojiSorteado;

            if (numeroCorte <= 16) { corSorteada = "branco"; emojiSorteado = "⚪"; multiplicadorSorteado = 6.0; }
            else if (numeroCorte <= 58) { corSorteada = "vermelho"; emojiSorteado = "🔴"; multiplicadorSorteado = 1.5; }
            else { corSorteada = "preto"; emojiSorteado = "⚫"; multiplicadorSorteado = 1.5; }

            bool ganhou = (corEscolhida == corSorteada);
            EmbedBuilder embedFinal;

            if (ganhou)
            {
                long premio = (long)(valorAposta * multiplicadorSorteado);
                EconomyHelper.AdicionarSaldo(guildId, donoApostaId, premio);
                var saldoAtual = EconomyHelper.GetSaldo(guildId, donoApostaId);

                embedFinal = component.Message.Embeds.First().ToEmbedBuilder()
                    .WithTitle("🎰 Roleta - VITÓRIA!")
                    .WithDescription(
                        $"{emojiSorteado} A roleta girou e parou no **{corSorteada.ToUpper()}**!\n\n" +
                        $"🎉 Parabéns {component.User.Mention}! Você multiplicou sua aposta por **{multiplicadorSorteado}x** e ganhou `{EconomyHelper.FormatarSaldo(premio)}` cpoints!\n" +
                        $"-# ◦ Novo saldo: {EconomyHelper.FormatarSaldo(saldoAtual)} cpoints"
                    )
                    .WithColor(Color.Green);
            }
            else
            {
                var saldoAtual = EconomyHelper.GetSaldo(guildId, donoApostaId);

                embedFinal = component.Message.Embeds.First().ToEmbedBuilder()
                    .WithTitle("🎰 Roleta - DERROTA")
                    .WithDescription(
                        $"{emojiSorteado} A roleta girou e parou no **{corSorteada.ToUpper()}**!\n\n" +
                        $"💸 Que pena, {component.User.Mention}. Você perdeu `{EconomyHelper.FormatarSaldo(valorAposta)}` cpoints nessa rodada.\n" +
                        $"-# ◦ Novo saldo: {EconomyHelper.FormatarSaldo(saldoAtual)} cpoints"
                    )
                    .WithColor(Color.Red);
            }

            await component.UpdateAsync(m => { m.Embed = embedFinal.Build(); m.Components = botoesDesativados.Build(); });
        }
    }
}
