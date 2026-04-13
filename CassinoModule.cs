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
        private static readonly Dictionary<ulong, long> ApostasAtivas = new();
        private const string GIF_ROLETA = "https://media.discordapp.net/attachments/1161794729462214779/1168565874748309564/roletazany.gif?ex=69dd05c7&is=69dbb447&hm=5cc06ebd5f399270a152db1fbb2c1e15272adb0d3ac37dc5d6106967c5d80bad&=";

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

                string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Uso:** `zroleta [valor]`"); return; }

                // BUSCANDO DO BANCO
                long saldoBanco = EconomyHelper.GetBanco(guildId, user.Id);
                long valorAposta = 0;

                if (partes[1] == "all") { valorAposta = saldoBanco; }
                else
                {
                    string input = partes[1].Replace("k", "000").Replace("m", "000000");
                    if (!long.TryParse(input, out valorAposta)) { await msg.Channel.SendMessageAsync("❌ Valor inválido."); return; }
                }

                if (valorAposta <= 0 || saldoBanco < valorAposta)
                {
                    await msg.Channel.SendMessageAsync($@"<:negativo:1492950137587241114> Você não tem **coins** em banco para apostar.");
                    return;
                }
                if (ApostasAtivas.ContainsKey(user.Id)) return;

                ApostasAtivas[user.Id] = valorAposta;
                EconomyHelper.RemoverBanco(guildId, user.Id, valorAposta);

                var embed = new EmbedBuilder()
                    .WithAuthor("Roleta Zany", user.GetAvatarUrl())
                    .WithDescription($@"💰 **Aposta:** `{EconomyHelper.FormatarSaldo(valorAposta)}` (Banco)
⚪ **Branco:** 6.0x | ⚫ **Preto:** 1.5x | 🔴 **Vermelho:** 1.5x")
                    .WithColor(new Color(43, 45, 49)).Build();

                var components = new ComponentBuilder()
                    .WithButton("Branco", $"roleta_branco_{user.Id}", ButtonStyle.Secondary, new Emoji("⚪"))
                    .WithButton("Preto", $"roleta_preto_{user.Id}", ButtonStyle.Secondary, new Emoji("⚫"))
                    .WithButton("Vermelho", $"roleta_vermelho_{user.Id}", ButtonStyle.Danger, new Emoji("🔴"))
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

            if (component.User.Id != userId) return;
            if (!ApostasAtivas.TryGetValue(userId, out long valorAposta)) return;
            var guildId = (component.User as SocketGuildUser).Guild.Id;

            if (escolha == "cancel")
            {
                ApostasAtivas.Remove(userId);
                EconomyHelper.AdicionarBanco(guildId, userId, valorAposta);
                await component.UpdateAsync(x => { x.Content = "✅ Aposta cancelada, saldo devolvido ao banco."; x.Embed = null; x.Components = null; });
                return;
            }

            ApostasAtivas.Remove(userId);
            await component.UpdateAsync(x => { x.Embed = new EmbedBuilder().WithDescription("⚫ **Girando roleta...**").WithImageUrl(GIF_ROLETA).Build(); x.Components = null; });

            await Task.Delay(4000);

            var random = new Random().Next(1, 101);
            string corSorteada = random <= 10 ? "branco" : (random <= 55 ? "preto" : "vermelho");
            bool ganhou = escolha == corSorteada;
            long premio = (long)(valorAposta * (corSorteada == "branco" ? 6.0 : 1.5));

            if (ganhou) EconomyHelper.AdicionarBanco(guildId, userId, premio);

            var embedFim = new EmbedBuilder()
                .WithTitle(ganhou ? "🏆 Ganhou!" : "❌ Perdeu!")
                .WithDescription($"A roleta parou no: **{corSorteada.ToUpper()}**\n" + (ganhou ? $"💰 Recebeu: `{EconomyHelper.FormatarSaldo(premio)}` no banco." : $"😔 Perdeu: `{EconomyHelper.FormatarSaldo(valorAposta)}` do banco."))
                .WithColor(ganhou ? Color.Green : Color.Red).Build();

            await component.ModifyOriginalResponseAsync(x => x.Embed = embedFim);
        }
    }
}
