using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Cassino
{
    public class ApostaModule
    {
        private readonly DiscordSocketClient _client;

        // Guarda as apostas pendentes. Key = ID do Desafiante, Value = (ID do Alvo, Valor)
        private static readonly Dictionary<ulong, (ulong Alvo, long Valor)> ApostasAtivas = new();

        public ApostaModule(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleCommand;
            _client.ButtonExecuted += HandleButtons;
        }

        private async Task HandleCommand(SocketMessage msg)
        {
            if (msg.Author.IsBot || msg is not SocketUserMessage userMsg) return;
            var content = msg.Content.ToLower().Trim();
            var user = msg.Author as SocketGuildUser;
            var guildId = user.Guild.Id;

            if (content.StartsWith("zapostar"))
            {
                string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (p.Length < 3)
                {
                    await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zapostar @usuario [valor]`");
                    return;
                }

                // Tenta pegar a pessoa mencionada
                var alvo = msg.MentionedUsers.FirstOrDefault();
                if (alvo == null)
                {
                    await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você precisa mencionar com quem quer apostar.");
                    return;
                }

                if (alvo.IsBot || alvo.Id == user.Id)
                {
                    await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você não pode apostar com bots ou consigo mesmo.");
                    return;
                }

                long bancoDesafiante = EconomyHelper.GetBanco(guildId, user.Id);

                // Pega o valor (que deve ser a última palavra da mensagem)
                string valTxt = p.Last().ToLower();
                long val = valTxt == "all" ? bancoDesafiante : (valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.TryParse(valTxt, out var res) ? res : 0);

                if (val <= 0)
                {
                    await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Valor inválido para aposta.");
                    return;
                }

                // TRAVA DO LIMITE DE 5M
                if (val > 5000000)
                {
                    await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Opa, vá com calma! O valor máximo para duelos é de **5M** (5.000.000) coins.");
                    return;
                }

                if (bancoDesafiante < val)
                {
                    await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> Você não possui `{EconomyHelper.FormatarSaldo(val)}` coins no banco para bancar essa aposta.");
                    return;
                }

                if (ApostasAtivas.ContainsKey(user.Id))
                {
                    await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você já tem um desafio pendente! Cancele o anterior clicando no X ou aguarde.");
                    return;
                }

                // Registra a aposta pendente
                ApostasAtivas[user.Id] = (alvo.Id, val);

                var eb = new EmbedBuilder()
                    .WithAuthor("⚔️ Duelo de Apostas", "https://cdn-icons-png.flaticon.com/512/3063/3063822.png")
                    .WithDescription($@"<a:teste:1490570407307378712> O jogador {user.Mention} desafiou você para um X1!

<a:7moneyz:1493015410637930508> | **Valor cobrado de cada:** `{EconomyHelper.FormatarSaldo(val)}`
🏆 | **Prêmio ao Vencedor:** `{EconomyHelper.FormatarSaldo(val * 2)}`

{alvo.Mention}, você tem coragem de aceitar?")
                    .WithColor(new Color(160, 80, 220))
                    .WithFooter($"Desafiante: {user.Username} • O desafiante pode cancelar no X");

                var cb = new ComponentBuilder()
                    .WithButton("Aceitar Duelo", $"aposta_acc_{user.Id}", ButtonStyle.Success, Emote.Parse("<:acerto:1493079138783727756>"))
                    .WithButton("Recusar / Cancelar", $"aposta_rec_{user.Id}", ButtonStyle.Danger, Emote.Parse("<:erro:1493078898462949526>"));

                await msg.Channel.SendMessageAsync(text: alvo.Mention, embed: eb.Build(), components: cb.Build());
            }
        }

        private async Task HandleButtons(SocketMessageComponent component)
        {
            var customId = component.Data.CustomId;
            var partes = customId.Split('_');
            if (partes.Length < 3 || partes[0] != "aposta") return;

            var escolha = partes[1]; // "acc" ou "rec"
            ulong desafianteId = ulong.Parse(partes[2]);

            if (!ApostasAtivas.TryGetValue(desafianteId, out var aposta))
            {
                await component.RespondAsync("<:erro:1493078898462949526> Este desafio já expirou, foi cancelado ou já finalizou.", ephemeral: true);
                return;
            }

            var guildId = (component.User as SocketGuildUser).Guild.Id;

            // BOTÃO DE RECUSAR / CANCELAR
            if (escolha == "rec")
            {
                if (component.User.Id == desafianteId)
                {
                    ApostasAtivas.Remove(desafianteId);
                    await component.UpdateAsync(x => { x.Content = $"🚫 O desafiante <@{desafianteId}> desistiu e cancelou a aposta."; x.Embed = null; x.Components = null; });
                }
                else if (component.User.Id == aposta.Alvo)
                {
                    ApostasAtivas.Remove(desafianteId);
                    await component.UpdateAsync(x => { x.Content = $"🚫 <@{aposta.Alvo}> correu do duelo de <@{desafianteId}> e recusou a aposta."; x.Embed = null; x.Components = null; });
                }
                else
                {
                    await component.RespondAsync("<:erro:1493078898462949526> Apenas os envolvidos no duelo podem cancelar.", ephemeral: true);
                }
                return;
            }

            // BOTÃO DE ACEITAR
            if (escolha == "acc")
            {
                if (component.User.Id != aposta.Alvo)
                {
                    await component.RespondAsync("<:erro:1493078898462949526> Saia daí! Apenas o jogador desafiado pode aceitar este duelo.", ephemeral: true);
                    return;
                }

                long bancoDesafiante = EconomyHelper.GetBanco(guildId, desafianteId);
                long bancoAlvo = EconomyHelper.GetBanco(guildId, aposta.Alvo);

                // Checa se o desafiante gastou o dinheiro nesse meio tempo
                if (bancoDesafiante < aposta.Valor)
                {
                    ApostasAtivas.Remove(desafianteId);
                    await component.UpdateAsync(x => { x.Content = $"<:erro:1493078898462949526> O duelo foi cancelado porque <@{desafianteId}> não tem mais o dinheiro no banco."; x.Embed = null; x.Components = null; });
                    return;
                }

                // Checa se o alvo tem dinheiro para bancar a aposta
                if (bancoAlvo < aposta.Valor)
                {
                    await component.RespondAsync($"<:erro:1493078898462949526> Você não possui `{EconomyHelper.FormatarSaldo(aposta.Valor)}` no banco para aceitar esse desafio.", ephemeral: true);
                    return;
                }

                // ✅ APOSTA CONFIRMADA! Tira o duelo dos ativos
                ApostasAtivas.Remove(desafianteId);

                // Desconta o valor dos dois no banco
                EconomyHelper.RemoverBanco(guildId, desafianteId, aposta.Valor);
                EconomyHelper.RemoverBanco(guildId, aposta.Alvo, aposta.Valor);

                // Lógica de Sorteio (50% / 50%)
                var random = new Random();
                bool desafianteGanhou = random.Next(0, 2) == 0;

                ulong vencedorId = desafianteGanhou ? desafianteId : aposta.Alvo;
                ulong perdedorId = desafianteGanhou ? aposta.Alvo : desafianteId;
                long premioTotal = aposta.Valor * 2;

                // Entrega o prêmio para o vencedor e registra no histórico
                EconomyHelper.AdicionarBanco(guildId, vencedorId, premioTotal);
                EconomyHelper.RegistrarTransacao(guildId, perdedorId, vencedorId, premioTotal, "DUELO_GANHO");

                var eb = new EmbedBuilder()
                    .WithAuthor("⚔️ Duelo Finalizado!")
                    .WithDescription($@"<a:teste:1490570407307378712> O sangue foi derramado e temos um campeão!

🏆 **Vencedor:** <@{vencedorId}>
💰 **Levou pra casa:** `{EconomyHelper.FormatarSaldo(premioTotal)}` coins

💀 **Perdedor:** <@{perdedorId}> (Perdeu `{EconomyHelper.FormatarSaldo(aposta.Valor)}`)")
                    .WithColor(Color.Gold);

                await component.UpdateAsync(x => {
                    x.Content = $"Duelo épico entre <@{desafianteId}> e <@{aposta.Alvo}>!";
                    x.Embed = eb.Build();
                    x.Components = null;
                });
            }
        }
    }
}
