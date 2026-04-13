using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Cassino
{
    public class CassinoModule
    {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, long> ApostasAtivas = new();
        private const string GIF_ROLETA = "https://media.discordapp.net/attachments/1161794729462214779/1168565874748309564/roletazany.gif?ex=69dd05c7&is=69dbb447&hm=5cc06ebd5f399270a152db1fbb2c1e15272adb0d3ac37dc5d6106967c5d80bad&=";

        public CassinoModule(DiscordSocketClient client)
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
                if (partes.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Uso correto:** `zroleta [valor]` ou `zroleta all`."); return; }

                long saldoBanco = EconomyHelper.GetBanco(guildId, user.Id);
                long valorAposta = 0;

                if (partes[1] == "all") { valorAposta = saldoBanco; }
                else
                {
                    string vTxt = partes[1].ToLower();
                    valorAposta = vTxt.EndsWith("k") ? (long)(double.Parse(vTxt.Replace("k", "")) * 1000) : vTxt.EndsWith("m") ? (long)(double.Parse(vTxt.Replace("m", "")) * 1000000) : long.TryParse(vTxt, out var v) ? v : 0;
                }

                if (valorAposta <= 0 || saldoBanco < valorAposta)
                {
                    await msg.Channel.SendMessageAsync($@"<:negativo:1492950137587241114> Você não tem **coins** em banco para apostar.");
                    return;
                }
                if (ApostasAtivas.ContainsKey(user.Id)) { await msg.Channel.SendMessageAsync("⚠️ Termine o jogo anterior antes de começar outro!"); return; }

                ApostasAtivas[user.Id] = valorAposta;
                EconomyHelper.RemoverBanco(guildId, user.Id, valorAposta);

                var embed = new EmbedBuilder()
                    .WithAuthor("Roleta", "https://cdn-icons-png.flaticon.com/512/1055/1055823.png")
                    .WithThumbnailUrl("https://cdn-icons-png.flaticon.com/512/1055/1055823.png")
                    .WithDescription($@"<a:teste:1490570407307378712> **Olá, {user.Mention}! Bem-vindo(a) à Roleta da {_client.CurrentUser.Username}.**

<a:7moneyz:1493015410637930508> | **Valor em aposta:** `{EconomyHelper.FormatarSaldo(valorAposta)}`

<:seta:1493089125979656385> | **Como funciona:** Escolha uma cor. Se o sorteio parar nela, você ganha o prêmio!
⚪ **Branco:** 6.0x (Difícil)
⚫ **Preto:** 1.5x
🔴 **Vermelho:** 1.5x

🧧 | **Desistir da aposta:** Clique no <:erro:1493078898462949526> para recuperar seu dinheiro agora.")
                    .WithFooter($"Apostador: {user.Username} • Hoje às {DateTime.Now:HH:mm}", user.GetAvatarUrl())
                    .WithColor(new Color(43, 45, 49)).Build();

                var components = new ComponentBuilder()
                    .WithButton("Branco (6.0x)", $"roleta_branco_{user.Id}", ButtonStyle.Secondary, new Emoji("⚪"))
                    .WithButton("Preto (1.5x)", $"roleta_preto_{user.Id}", ButtonStyle.Secondary, new Emoji("⚫"))
                    .WithButton("Vermelho (1.5x)", $"roleta_vermelho_{user.Id}", ButtonStyle.Danger, new Emoji("🔴"))
                    .WithButton(null, $"roleta_cancel_{user.Id}", ButtonStyle.Secondary, Emote.Parse("<:erro:1493078898462949526>"));

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

            if (component.User.Id != userId) { await component.RespondAsync("<:erro:1493078898462949526> Saia daqui, essa roleta não é sua!", ephemeral: true); return; }
            if (!ApostasAtivas.TryGetValue(userId, out long valorAposta)) { await component.RespondAsync("❌ Jogo finalizado ou erro.", ephemeral: true); return; }
            var guildId = (component.User as SocketGuildUser).Guild.Id;

            if (escolha == "cancel")
            {
                ApostasAtivas.Remove(userId);
                EconomyHelper.AdicionarBanco(guildId, userId, valorAposta);
                await component.UpdateAsync(x => {
                    x.Content = $"<:acerto:1493079138783727756> {component.User.Mention} desistiu e recuperou seus `{EconomyHelper.FormatarSaldo(valorAposta)}` cpoints no banco.";
                    x.Embed = null; x.Components = null;
                });
                return;
            }

            ApostasAtivas.Remove(userId);
            await component.UpdateAsync(x => {
                x.Embed = new EmbedBuilder().WithAuthor("Roleta", "https://cdn-icons-png.flaticon.com/512/1055/1055823.png").WithDescription("⚫ **Girando roleta...**").WithImageUrl(GIF_ROLETA).WithColor(new Color(43, 45, 49)).Build();
                x.Components = null;
            });

            await Task.Delay(4000);

            var random = new Random().Next(1, 101);
            string corSorteada = random <= 10 ? "branco" : (random <= 55 ? "preto" : "vermelho");
            bool ganhou = escolha == corSorteada;
            long premio = (long)(valorAposta * (corSorteada == "branco" ? 6.0 : 1.5));
            string emojiCor = corSorteada switch { "branco" => "⚪", "preto" => "⚫", _ => "🔴" };

            var embedFim = new EmbedBuilder().WithAuthor("Resultado da Roleta", "https://cdn-icons-png.flaticon.com/512/1055/1055823.png").WithFooter($"Apostador: {component.User.Username}", component.User.GetAvatarUrl()).WithTimestamp(DateTime.Now);

            if (ganhou)
            {
                EconomyHelper.AdicionarBanco(guildId, userId, premio);
                embedFim.WithColor(Color.Green).WithDescription($@"<a:ganhador:1493088070923452599> **Parabéns! A sorte passou por aqui!**

🎡 A roleta parou no: {emojiCor} **{corSorteada.ToUpper()}**
<a:7moneyz:1493015410637930508> Você recebeu: `{EconomyHelper.FormatarSaldo(premio)}` cpoints no banco.");
            }
            else
            {
                embedFim.WithColor(Color.Red).WithDescription($@"<:erro:1493078898462949526> **Não foi dessa vez...**

🎡 A roleta parou no: {emojiCor} **{corSorteada.ToUpper()}**
<:erro:1493078898462949526> Você perdeu: `{EconomyHelper.FormatarSaldo(valorAposta)}` cpoints do banco.");
            }

            await component.ModifyOriginalResponseAsync(x => { x.Embed = embedFim.Build(); x.Content = component.User.Mention; });
        }
    }
}
