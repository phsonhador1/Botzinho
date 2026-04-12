using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Cassino
{
    // Classe para guardar as informações da aposta enquanto o botão não é clicado
    public class ApostaRoleta
    {
        public ulong UserId { get; set; }
        public long Valor { get; set; }
    }

    public class CassinoHandler
    {
        private readonly DiscordSocketClient _client;

        // Dicionário para rastrear as apostas ativas pelos IDs das mensagens
        private static readonly Dictionary<ulong, ApostaRoleta> _apostasAtivas = new();

        public CassinoHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
            _client.ButtonExecuted += HandleButton; // Escuta os cliques nos botões
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

                // 1. Remove o dinheiro antecipadamente (se cancelar, ele recebe de volta)
                EconomyHelper.RemoverSaldo(guildId, user.Id, valorAposta);

                // 2. Cria o Embed profissional idêntico à foto
                var emojiRoleta = "<:roleta:123456789012345678>"; // Substitua pelo ID do seu emoji de roleta, se tiver
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
                    // URL da imagem da ficha que você enviou
                    .WithThumbnailUrl("https://i.imgur.com/gK9JXXh.png")
                    .WithColor(new Discord.Color(80, 0, 80))
                    .WithFooter($"Apostador: {user.Username} • Hoje às {DateTime.Now:HH:mm}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .Build();

                // 3. Cria os Botões
                var botoes = new ComponentBuilder()
                    .WithButton("Branco (6.0x)", "rol_branco", ButtonStyle.Secondary, new Emoji("⚪"))
                    .WithButton("Preto (1.5x)", "rol_preto", ButtonStyle.Secondary, new Emoji("⚫"))
                    .WithButton("Vermelho (1.5x)", "rol_vermelho", ButtonStyle.Danger, new Emoji("🔴"))
                    .WithButton("", "rol_cancelar", ButtonStyle.Secondary, new Emoji("❌"))
                    .Build();

                // 4. Envia a mensagem e guarda o ID
                var mensagemEnviada = await msg.Channel.SendMessageAsync(embed: embed, components: botoes);

                _apostasAtivas[mensagemEnviada.Id] = new ApostaRoleta
                {
                    UserId = user.Id,
                    Valor = valorAposta
                };
            }
        }

        // --- SISTEMA DE CLIQUE NOS BOTÕES ---
        private async Task HandleButton(SocketMessageComponent component)
        {
            var customId = component.Data.CustomId;
            var messageId = component.Message.Id;
            var guildId = ((SocketGuildUser)component.User).Guild.Id;

            if (!customId.StartsWith("rol_")) return;

            if (!_apostasAtivas.TryGetValue(messageId, out var aposta))
            {
                await component.RespondAsync("Esta aposta já foi finalizada ou expirou.", ephemeral: true);
                return;
            }

            if (component.User.Id != aposta.UserId)
            {
                await component.RespondAsync("Você não pode clicar na aposta de outra pessoa!", ephemeral: true);
                return;
            }

            // Confirma o recebimento da interação para não dar "Interação Falhou"
            await component.DeferAsync();

            // CORREÇÃO: Desativa os botões reconstruindo-os com a tipagem estrita do Discord.Net
            var botoesDesativados = new ComponentBuilder();

            foreach (var messageComponent in component.Message.Components)
            {
                // Verifica se o componente base é realmente uma linha de botões
                if (messageComponent is ActionRowComponent actionRow)
                {
                    var rowBuilder = new ActionRowBuilder();

                    foreach (var innerComp in actionRow.Components)
                    {
                        if (innerComp is ButtonComponent btn)
                        {
                            // Passa o Builder do botão diretamente (SEM o .Build() no final)
                            rowBuilder.AddComponent(btn.ToBuilder().WithDisabled(true));
                        }
                    }
                    botoesDesativados.AddRow(rowBuilder);
                }
            }

            // CANCELAR APOSTA
            if (customId == "rol_cancelar")
            {
                EconomyHelper.AdicionarSaldo(guildId, aposta.UserId, aposta.Valor);
                _apostasAtivas.Remove(messageId);

                var embedCancelado = component.Message.Embeds.First().ToEmbedBuilder()
                    .WithDescription($"❌ | {component.User.Mention} desistiu da aposta. O valor de `{EconomyHelper.FormatarSaldo(aposta.Valor)}` foi devolvido para a carteira.")
                    .WithColor(Color.Red)
                    .Build();

                await component.Message.ModifyAsync(m => { m.Embed = embedCancelado; m.Components = botoesDesativados.Build(); });
                return;
            }

            // LÓGICA DE GIRAR A ROLETA (Sorteio)
            var random = new Random();
            int numeroCorte = random.Next(1, 101); // 1 a 100

            string corSorteada;
            double multiplicadorSorteado = 0;
            string emojiSorteado;

            // Probabilidades ajustadas aos multiplicadores:
            // Branco (6.0x) = ~16% chance
            // Vermelho (1.5x) = ~42% chance
            // Preto (1.5x) = ~42% chance
            if (numeroCorte <= 16) { corSorteada = "rol_branco"; emojiSorteado = "⚪"; multiplicadorSorteado = 6.0; }
            else if (numeroCorte <= 58) { corSorteada = "rol_vermelho"; emojiSorteado = "🔴"; multiplicadorSorteado = 1.5; }
            else { corSorteada = "rol_preto"; emojiSorteado = "⚫"; multiplicadorSorteado = 1.5; }

            // Verificação de Ganho ou Perda
            string nomeCor = corSorteada.Replace("rol_", "").ToUpper();
            bool ganhou = (customId == corSorteada);

            EmbedBuilder embedFinal;

            if (ganhou)
            {
                long premio = (long)(aposta.Valor * multiplicadorSorteado);
                EconomyHelper.AdicionarSaldo(guildId, aposta.UserId, premio);
                var saldoAtual = EconomyHelper.GetSaldo(guildId, aposta.UserId);

                embedFinal = component.Message.Embeds.First().ToEmbedBuilder()
                    .WithTitle("🎰 Roleta - VITÓRIA!")
                    .WithDescription(
                        $"{emojiSorteado} A roleta girou e parou no **{nomeCor}**!\n\n" +
                        $"🎉 Parabéns {component.User.Mention}! Você multiplicou sua aposta por **{multiplicadorSorteado}x** e ganhou `{EconomyHelper.FormatarSaldo(premio)}` cpoints!\n" +
                        $"-# ◦ Novo saldo: {EconomyHelper.FormatarSaldo(saldoAtual)} cpoints"
                    )
                    .WithColor(Color.Green);
            }
            else
            {
                var saldoAtual = EconomyHelper.GetSaldo(guildId, aposta.UserId);

                embedFinal = component.Message.Embeds.First().ToEmbedBuilder()
                    .WithTitle("🎰 Roleta - DERROTA")
                    .WithDescription(
                        $"{emojiSorteado} A roleta girou e parou no **{nomeCor}**!\n\n" +
                        $"💸 Que pena, {component.User.Mention}. Você perdeu `{EconomyHelper.FormatarSaldo(aposta.Valor)}` cpoints nessa rodada.\n" +
                        $"-# ◦ Novo saldo: {EconomyHelper.FormatarSaldo(saldoAtual)} cpoints"
                    )
                    .WithColor(Color.Red);
            }

            // Atualiza a mensagem
            _apostasAtivas.Remove(messageId);
            await component.Message.ModifyAsync(m => { m.Embed = embedFinal.Build(); m.Components = botoesDesativados.Build(); });
        }
    }
}
