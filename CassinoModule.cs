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
        private static readonly Dictionary<ulong, (List<int> Player, List<int> Dealer, long Bet)> BlackjackAtivo = new();

        private const string GIF_ROLETA = "https://media.discordapp.net/attachments/1161794729462214779/1168565874748309564/roletazany.gif?ex=69dd05c7&is=69dbb447&hm=5cc06ebd5f399270a152db1fbb2c1e15272adb0d3ac37dc5d6106967c5d80bad&=";
        private const string IMG_MOEDA = "https://cdn.discordapp.net/attachments/1110495236716773447/1163499638461042831/coin_1540515.png";

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

            // --- ZROLETA ---
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
                    await msg.Channel.SendMessageAsync($@"<:erro:1493078898462949526> Você não tem **coins** em banco para apostar.");
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

<:erro:1493078898462949526> | **Desistir da aposta:** Clique no <:erro:1493078898462949526> para recuperar seu dinheiro agora.")
                    .WithFooter($"Apostador: {user.Username} • Hoje às {DateTime.Now:HH:mm}", user.GetAvatarUrl())
                    .WithColor(new Color(43, 45, 49)).Build();

                var components = new ComponentBuilder()
                    .WithButton("Branco (6.0x)", $"roleta_branco_{user.Id}", ButtonStyle.Secondary, new Emoji("⚪"))
                    .WithButton("Preto (1.5x)", $"roleta_preto_{user.Id}", ButtonStyle.Secondary, new Emoji("⚫"))
                    .WithButton("Vermelho (1.5x)", $"roleta_vermelho_{user.Id}", ButtonStyle.Danger, new Emoji("🔴"))
                    .WithButton(null, $"roleta_cancel_{user.Id}", ButtonStyle.Secondary, Emote.Parse("<:erro:1493078898462949526>"));

                await msg.Channel.SendMessageAsync(embed: embed, components: components.Build());
            }

            // --- ZCF / ZCOINFLIP ---
            else if (content.StartsWith("zcf") || content.StartsWith("zcoinflip"))
            {
                var user = msg.Author as SocketGuildUser;
                if (user == null) return;
                var guildId = user.Guild.Id;

                string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zcoinflip (valor)`"); return; }
                long banco = EconomyHelper.GetBanco(guildId, user.Id);
                string valTxt = p[1].ToLower();
                long val = valTxt == "all" ? banco : (valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.TryParse(valTxt, out var res) ? res : 0);

                if (val <= 0 || banco < val) { await msg.Channel.SendMessageAsync($@"<:erro:1493078898462949526> Você não possui **{EconomyHelper.FormatarSaldo(val)} coins** no banco para apostar."); return; }
                if (ApostasAtivas.ContainsKey(user.Id)) return;

                ApostasAtivas[user.Id] = val;
                EconomyHelper.RemoverBanco(guildId, user.Id, val);

                var eb = new EmbedBuilder().WithAuthor("Cara ou Coroa", IMG_MOEDA).WithDescription($"🪙 | **Aposta:** `{EconomyHelper.FormatarSaldo(val)}`").WithFooter($"Apostador: {user.Username}").WithColor(new Color(114, 137, 218));
                var cb = new ComponentBuilder().WithButton("Cara", $"cf_cara_{user.Id}").WithButton("Coroa", $"cf_coroa_{user.Id}").WithButton(null, $"cf_cancel_{user.Id}", ButtonStyle.Danger, new Emoji("❌"));
                await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
            }

            // --- ZBJ / ZBLACKJACK ---
            else if (content.StartsWith("zbj") || content.StartsWith("zblackjack"))
            {
                var user = msg.Author as SocketGuildUser;
                if (user == null) return;
                var guildId = user.Guild.Id;

                string[] p = content.Split(' '); if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Uso:** `zbj [valor]`"); return; }
                long banco = EconomyHelper.GetBanco(guildId, user.Id);
                long val = p[1] == "all" ? banco : (p[1].EndsWith("k") ? (long)(double.Parse(p[1].Replace("k", "")) * 1000) : p[1].EndsWith("m") ? (long)(double.Parse(p[1].Replace("m", "")) * 1000000) : long.Parse(p[1]));

                if (val <= 0 || banco < val || BlackjackAtivo.ContainsKey(user.Id)) return;

                EconomyHelper.RemoverBanco(guildId, user.Id, val);
                var deck = new List<int> { 2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 10, 10, 11 };
                var r = new Random();
                var pHand = new List<int> { deck[r.Next(deck.Count)], deck[r.Next(deck.Count)] };
                var dHand = new List<int> { deck[r.Next(deck.Count)] };
                BlackjackAtivo[user.Id] = (pHand, dHand, val);

                var eb = new EmbedBuilder().WithAuthor("Blackjack 🃏").WithDescription($"**Suas:** {string.Join(", ", pHand)} (Total: {pHand.Sum()})\n**Dealer:** {dHand[0]} e [?]\n💰 **Aposta:** `{EconomyHelper.FormatarSaldo(val)}`").WithColor(Color.Blue);
                var cb = new ComponentBuilder().WithButton("Comprar", $"bj_hit_{user.Id}").WithButton("Parar", $"bj_stand_{user.Id}", ButtonStyle.Secondary);
                await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
            }
        }

        private async Task HandleButtons(SocketMessageComponent component)
        {
            var customId = component.Data.CustomId;
            var partes = customId.Split('_');
            if (partes.Length < 3) return;

            var prefix = partes[0]; // roleta, cf, bj
            var escolha = partes[1]; // branco, cancel, cara, hit, etc.
            var userId = ulong.Parse(partes[2]);

            if (component.User.Id != userId)
            {
                await component.RespondAsync("<:erro:1493078898462949526> Saia daqui, esse jogo não é seu!", ephemeral: true);
                return;
            }

            var guildId = (component.User as SocketGuildUser).Guild.Id;

            // --- BOTÕES ROLETA ---
            if (prefix == "roleta")
            {
                if (!ApostasAtivas.TryGetValue(userId, out long valorAposta)) { await component.RespondAsync("❌ Jogo finalizado ou erro.", ephemeral: true); return; }

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

            // --- BOTÕES COINFLIP ---
            else if (prefix == "cf")
            {
                if (!ApostasAtivas.TryGetValue(userId, out long val)) { await component.RespondAsync("❌ Jogo finalizado ou erro.", ephemeral: true); return; }
                ApostasAtivas.Remove(userId);
                if (escolha == "cancel") { EconomyHelper.AdicionarBanco(guildId, userId, val); await component.UpdateAsync(x => { x.Content = $"✅ {component.User.Mention} desistiu."; x.Embed = null; x.Components = null; }); return; }

                string res = new Random().Next(0, 2) == 0 ? "cara" : "coroa"; bool win = escolha == res;
                var eb = new EmbedBuilder().WithAuthor("Cara ou Coroa", IMG_MOEDA).WithThumbnailUrl(IMG_MOEDA);

                if (win) { EconomyHelper.AdicionarBanco(guildId, userId, val * 2); eb.WithColor(Color.Green).WithDescription($"Ganhou! Deu **{res}**.\n💰 +{EconomyHelper.FormatarSaldo(val * 2)}"); }
                else { eb.WithColor(Color.Red).WithDescription($"Perdeu! Deu **{res}**.\n❌ -{EconomyHelper.FormatarSaldo(val)}"); }

                await component.UpdateAsync(x => { x.Embed = eb.Build(); x.Components = null; x.Content = component.User.Mention; });
            }

            // --- BOTÕES BLACKJACK ---
            else if (prefix == "bj")
            {
                if (!BlackjackAtivo.TryGetValue(userId, out var game)) { await component.RespondAsync("❌ Jogo finalizado ou erro.", ephemeral: true); return; }
                var r = new Random();
                var deck = new List<int> { 2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 10, 10, 11 };

                if (escolha == "hit")
                {
                    game.Player.Add(deck[r.Next(deck.Count)]);
                    if (game.Player.Sum() > 21)
                    {
                        BlackjackAtivo.Remove(userId);
                        await component.UpdateAsync(x => { x.Content = $"💥 **Estourou!** Total: {game.Player.Sum()}. Perdeu `{EconomyHelper.FormatarSaldo(game.Bet)}`."; x.Embed = null; x.Components = null; });
                        return;
                    }
                    await component.UpdateAsync(x => x.Embed = new EmbedBuilder().WithAuthor("Blackjack 🃏").WithDescription($"**Suas:** {string.Join(", ", game.Player)} (Total: {game.Player.Sum()})\n**Dealer:** {game.Dealer[0]} e [?]").WithColor(Color.Blue).Build());
                }
                else
                {
                    BlackjackAtivo.Remove(userId);
                    while (game.Dealer.Sum() < 17) game.Dealer.Add(deck[r.Next(deck.Count)]);
                    int pS = game.Player.Sum(); int dS = game.Dealer.Sum();
                    string resT = ""; Color col;
                    if (dS > 21 || pS > dS) { resT = $"🏆 **Ganhou!** Dealer fez {dS}. Prêmio: `{EconomyHelper.FormatarSaldo(game.Bet * 2)}`"; EconomyHelper.AdicionarBanco(guildId, userId, game.Bet * 2); col = Color.Green; }
                    else if (pS == dS) { resT = "⚖️ **Empate!** Valor devolvido."; EconomyHelper.AdicionarBanco(guildId, userId, game.Bet); col = Color.LightGrey; }
                    else { resT = $"❌ **Perdeu!** Dealer fez {dS}."; col = Color.Red; }

                    await component.UpdateAsync(x => { x.Embed = new EmbedBuilder().WithTitle("Resultado Blackjack").WithDescription($"{resT}\nSuas: {pS} | Dealer: {dS}").WithColor(col).Build(); x.Components = null; });
                }
            }
        }
    }
}
