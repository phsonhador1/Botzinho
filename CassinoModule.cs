using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using SkiaSharp;

namespace Botzinho.Cassino
{
    // --- CLASSES DO BLACKJACK VISUAL MANTIDAS ---
    public class Card
    {
        public string Suit { get; set; }
        public string Value { get; set; }
        public int Score { get; set; }
        public string ImagePath => $"{Value.ToLower()}_{GetFullSuitName()}.png";
        private string GetFullSuitName() => Suit switch { "P" => "clubs", "O" => "diamonds", "C" => "hearts", "E" => "spades", _ => "" };
    }

    public static class BlackjackLogic
    {
        public static List<Card> CreateDeck()
        {
            string[] suits = { "P", "O", "C", "E" };
            string[] values = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "j", "q", "k", "a" };
            var deck = new List<Card>();
            foreach (var suit in suits)
            {
                foreach (var value in values)
                {
                    int score = int.TryParse(value, out int s) ? s : (value == "a" ? 11 : 10);
                    deck.Add(new Card { Suit = suit, Value = value, Score = score });
                }
            }
            return deck;
        }

        public static int CalculateScore(List<Card> hand)
        {
            int total = hand.Sum(c => c.Score);
            int aceCount = hand.Count(c => c.Value == "a");
            while (total > 21 && aceCount > 0) { total -= 10; aceCount--; }
            return total;
        }

        public static void Shuffle(this List<Card> deck)
        {
            var r = new Random();
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = r.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }
    }

    public static class CasinoImageHelper
    {
        public static async Task<string> GerarImagemBlackjack(List<Card> playerHand, List<Card> dealerHand, bool dealerRevealed, string stateText, SKColor accent)
        {
            int width = 900; int height = 600;
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(accent);
            var boldFont = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);
            var regularFont = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Normal);
            var paintWhite48 = new SKPaint { Color = SKColors.White, TextSize = 48, Typeface = boldFont, TextAlign = SKTextAlign.Center, IsAntialias = true };
            canvas.DrawText(stateText, width / 2, 70, paintWhite48);
            DesenharMao(canvas, "Mão do Dealer", dealerHand, dealerRevealed, 100, regularFont, boldFont);
            DesenharMao(canvas, "Sua Mão", playerHand, true, 350, regularFont, boldFont);
            var p = Path.Combine(Path.GetTempPath(), $"bj_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot()) using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(p)) data.SaveTo(str);
            return p;
        }

        private static void DesenharMao(SKCanvas canvas, string title, List<Card> hand, bool revealed, float y, SKTypeface regularFont, SKTypeface boldFont)
        {
            var bgRect = new SKRect(30, y, 870, y + 210);
            canvas.DrawRoundRect(bgRect, 20, 20, new SKPaint { Color = new SKColor(0, 0, 0, 60), IsAntialias = true });
            var paintTitle = new SKPaint { Color = SKColors.White, TextSize = 28, Typeface = boldFont, IsAntialias = true };
            canvas.DrawText(title, 60, y + 45, paintTitle);
            int score = revealed ? BlackjackLogic.CalculateScore(hand) : hand.Skip(1).Sum(c => c.Score);
            string scoreText = revealed ? $"Valor: {score}" : $"Valor: ? + {score}";
            var scorePaint = new SKPaint { Color = SKColors.White, TextSize = 22, Typeface = boldFont, IsAntialias = true };
            float scoreWidth = scorePaint.MeasureText(scoreText);
            var scoreRect = new SKRect(840 - scoreWidth - 20, y + 20, 840, y + 60);
            canvas.DrawRoundRect(scoreRect, 10, 10, new SKPaint { Color = new SKColor(0, 0, 0, 100), IsAntialias = true });
            canvas.DrawText(scoreText, 840 - scoreWidth - 10, y + 48, scorePaint);
            float cardWidth = 100;
            float totalCardsWidth = (hand.Count * cardWidth) + ((hand.Count - 1) * 15);
            float startX = (900 - totalCardsWidth) / 2;
            for (int i = 0; i < hand.Count; i++)
            {
                bool isFaceDown = !revealed && i == 0;
                DesenharCartaCriadaNoCodigo(canvas, hand[i], isFaceDown, startX, y + 50, boldFont);
                startX += cardWidth + 15;
            }
        }

        private static void DesenharCartaCriadaNoCodigo(SKCanvas canvas, Card card, bool isFaceDown, float x, float y, SKTypeface font)
        {
            var rect = new SKRect(x, y, x + 100, y + 140);
            canvas.DrawRoundRect(new SKRect(x + 2, y + 2, x + 102, y + 142), 8, 8, new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true });
            canvas.DrawRoundRect(rect, 8, 8, new SKPaint { Color = SKColors.White, IsAntialias = true });
            if (isFaceDown)
            {
                canvas.DrawRoundRect(new SKRect(x + 6, y + 6, x + 94, y + 134), 4, 4, new SKPaint { Color = new SKColor(110, 40, 180), IsAntialias = true });
                canvas.DrawText("Z", x + 50, y + 85, new SKPaint { Color = SKColors.White, TextSize = 40, Typeface = font, TextAlign = SKTextAlign.Center, IsAntialias = true });
                return;
            }
            string suitSymbol = card.Suit switch { "P" => "♣", "O" => "♦", "C" => "♥", "E" => "♠", _ => "?" };
            SKColor suitColor = (card.Suit == "O" || card.Suit == "C") ? SKColors.Red : SKColors.Black;
            var paintText = new SKPaint { Color = suitColor, TextSize = 20, Typeface = font, IsAntialias = true };
            canvas.DrawText(card.Value.ToUpper(), x + 8, y + 24, paintText);
            canvas.DrawText(suitSymbol, x + 50, y + 90, new SKPaint { Color = suitColor, TextSize = 50, Typeface = font, TextAlign = SKTextAlign.Center, IsAntialias = true });
        }

        public static async Task<string> GerarImagemCoinflip(bool deuCara, bool ganhou, long valor)
        {
            // Aumentamos o canvas para dar "respiro" visual e centralizar melhor
            int width = 600; int height = 280;
            string fileName = Path.Combine(Path.GetTempPath(), $"cf_beauty_{Guid.NewGuid()}.png");
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;

            // 1. Fundo Gradiente Suave (Profissional)
            var bgPaint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(0, height),
                    new SKColor[] { new SKColor(25, 25, 35), new SKColor(10, 10, 15) }, // Roxo muito escuro para preto
                    null,
                    (SkiaSharp.SKShaderTileMode)0)
            };
            canvas.DrawRect(new SKRect(0, 0, width, height), bgPaint);

            // 2. Moldura Neon Sutil (Arredondada)
            var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3,
                Color = ganhou ? SKColors.LimeGreen.WithAlpha(100) : SKColors.Red.WithAlpha(100),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4) // Efeito Neon
            };
            canvas.DrawRoundRect(new SKRect(15, 15, width - 15, height - 15), 20, 20, borderPaint);

            // 3. Centralização Horizontal
            float centroX = width / 2f;

            // 4. Desenhar Moeda (Centralizada e com Sombra)
            string coinAsset = deuCara ? "coin_cara.png" : "coin_coroa.png";
            string coinPath = Path.Combine(AppContext.BaseDirectory, "Assets", coinAsset);
            if (File.Exists(coinPath))
            {
                using var stream = new FileStream(coinPath, FileMode.Open, FileAccess.Read);
                using var bitmap = SKBitmap.Decode(stream);

                // Sombra da moeda (Dá profundidade)
                canvas.DrawCircle(centroX + 3, 83, 62, new SKPaint { Color = new SKColor(0, 0, 0, 100), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5) });

                // Moeda
                float moedaSize = 120;
                float moedaX = centroX - (moedaSize / 2f);
                canvas.DrawBitmap(bitmap, new SKRect(moedaX, 25, moedaX + moedaSize, 25 + moedaSize), new SKPaint { IsAntialias = true });
            }

            // 5. Hierarquia de Fontes (Arial Bold para tudo, variando tamanho)
            var boldTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);

            // Título Principal (O Maior)
            var paintTitulo = new SKPaint
            {
                Color = ganhou ? SKColors.LimeGreen : SKColors.Red,
                TextSize = 52,
                Typeface = boldTypeface,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
                FakeBoldText = true // Destaca ainda mais
            };

            // Subtexto (O Menor e mais suave)
            var paintSubtexto = new SKPaint
            {
                Color = SKColors.LightGray,
                TextSize = 24,
                Typeface = boldTypeface,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            };

            // Valor (Médio e em destaque)
            var paintValor = new SKPaint
            {
                Color = ganhou ? SKColors.LimeGreen : SKColors.Red,
                TextSize = 42,
                Typeface = boldTypeface,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            };

            // 6. Desenhar Textos (Perfeitamente Centralizados e Espaçados)
            // Usamos o centroX e coordenadas Y fixas para um espaçamento harmônico
            canvas.DrawText(ganhou ? "🎉 VITÓRIA!" : "💀 DERROTA!", centroX, 185, paintTitulo);
            canvas.DrawText(ganhou ? "O destino sorriu para você." : "A sorte não estava ao seu lado.", centroX, 220, paintSubtexto);

            // Formata o valor e desenha com espaçamento
            string valorFormatado = $"{(ganhou ? "+" : "-")} {EconomyHelper.FormatarSaldo(valor)}";
            canvas.DrawText(valorFormatado, centroX, 260, paintValor);

            // 7. Salvar e Limpar
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(fileName))
            {
                data.SaveTo(str);
            }

            return fileName;
        }
    }

    public class CassinoModule
    {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();
        private static readonly Dictionary<ulong, long> CoinflipAtivo = new();
        private static readonly Dictionary<ulong, long> RoletaAtiva = new();
        private static readonly Dictionary<ulong, (List<Card> Player, List<Card> Dealer, List<Card> Deck, long Bet)> BlackjackAtivo = new();

        private const string GIF_ROLETA = "https://media.discordapp.net/attachments/1161794729462214779/1168565874748309564/roletazany.gif";
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
            var user = msg.Author as SocketGuildUser;
            var guildId = user.Guild.Id;

            string[] cmds = { "zroleta", "zcf", "zcoinflip", "zbj", "zblackjack" };
            if (!cmds.Any(c => content.StartsWith(c))) return;
            if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 2)
            {
                var aviso = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> {user.Mention}, vá com calma! Aguarde **2 segundos** para **apostar** novamente.");
                _ = Task.Delay(2000).ContinueWith(_ => aviso.DeleteAsync());
                return;
            }
            _cooldowns[user.Id] = DateTime.UtcNow;

            if (content.StartsWith("zroleta"))
            {
                string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Uso correto:** `zroleta [valor]` ou `zroleta all`."); return; }
                long saldoBanco = EconomyHelper.GetBanco(guildId, user.Id);
                long valorAposta = partes[1] == "all" ? saldoBanco : (partes[1].EndsWith("k") ? (long)(double.Parse(partes[1].Replace("k", "")) * 1000) : partes[1].EndsWith("m") ? (long)(double.Parse(partes[1].Replace("m", "")) * 1000000) : long.TryParse(partes[1], out var v) ? v : 0);
                if (valorAposta <= 0 || saldoBanco < valorAposta) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você não tem **coins** em banco."); return; }
                if (RoletaAtiva.ContainsKey(user.Id)) return;
                RoletaAtiva[user.Id] = valorAposta;
                EconomyHelper.RemoverBanco(guildId, user.Id, valorAposta);
                var eb = new EmbedBuilder().WithAuthor("Roleta", "https://cdn-icons-png.flaticon.com/512/1055/1055823.png").WithDescription($"<a:teste:1490570407307378712> **Olá, {user.Mention}!**\n\n<a:7moneyz:1493015410637930508> **Aposta:** `{EconomyHelper.FormatarSaldo(valorAposta)}`").WithColor(new Color(43, 45, 49));
                var cb = new ComponentBuilder().WithButton("Branco (6.0x)", $"roleta_branco_{user.Id}", ButtonStyle.Secondary, new Emoji("⚪")).WithButton("Preto (1.5x)", $"roleta_preto_{user.Id}", ButtonStyle.Secondary, new Emoji("⚫")).WithButton("Vermelho (1.5x)", $"roleta_vermelho_{user.Id}", ButtonStyle.Danger, new Emoji("🔴")).WithButton(null, $"roleta_cancel_{user.Id}", ButtonStyle.Secondary, Emote.Parse("<:erro:1493078898462949526>"));
                await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
            }
            else if (content.StartsWith("zcf") || content.StartsWith("zcoinflip"))
            {
                string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zcoinflip (valor)`"); return; }
                long banco = EconomyHelper.GetBanco(guildId, user.Id);
                long val = p[1] == "all" ? banco : (p[1].EndsWith("k") ? (long)(double.Parse(p[1].Replace("k", "")) * 1000) : p[1].EndsWith("m") ? (long)(double.Parse(p[1].Replace("m", "")) * 1000000) : long.TryParse(p[1], out var r) ? r : 0);

                if (val <= 0 || banco < val || CoinflipAtivo.ContainsKey(user.Id)) return;

                CoinflipAtivo[user.Id] = val;
                EconomyHelper.RemoverBanco(guildId, user.Id, val);

                // --- MENSAGEM RESTAURADA IGUAL AO SEU PRINT ---
                var eb = new EmbedBuilder()
                    .WithAuthor("Cara ou Coroa", IMG_MOEDA)
                    .WithDescription($@"• **Olá,** {user.Mention}**!** Bem-vindo(a) ao jogo **Cara** ou **Coroa**.

<:6821purplecash:1493263367488536606> | **Valor em aposta:** `{EconomyHelper.FormatarSaldo(val)}`

<:seta:1493089125979656385> | **Como funciona:**
Escolha entre **Cara** ou **Coroa** e aposte. Se acertar, você ganha o dobro da aposta; se errar, você perde o valor apostado.

<:erro:1493078898462949526> | **Desistir da aposta:**
Se decidir não continuar, clique no <:erro:1493078898462949526> para desistir da aposta.")
                    .WithFooter($"Apostador: {user.Username} • Hoje às {DateTime.Now:HH:mm}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .WithColor(new Color(160, 80, 220));

                var cb = new ComponentBuilder()
                    .WithButton("Cara", $"cf_cara_{user.Id}", ButtonStyle.Secondary, new Emoji("🙂"))
                    .WithButton("Coroa", $"cf_coroa_{user.Id}", ButtonStyle.Secondary, new Emoji("👑"))
                    .WithButton(null, $"cf_cancel_{user.Id}", ButtonStyle.Secondary, Emote.Parse("<:erro:1493078898462949526>"));

                await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
            }
            else if (content.StartsWith("zbj") || content.StartsWith("zblackjack"))
            {
                string[] p = content.Split(' '); if (p.Length < 2) return;
                long banco = EconomyHelper.GetBanco(guildId, user.Id);
                long val = p[1] == "all" ? banco : (p[1].EndsWith("k") ? (long)(double.Parse(p[1].Replace("k", "")) * 1000) : p[1].EndsWith("m") ? (long)(double.Parse(p[1].Replace("m", "")) * 1000000) : long.Parse(p[1]));
                if (val <= 0 || banco < val || BlackjackAtivo.ContainsKey(user.Id)) return;
                EconomyHelper.RemoverBanco(guildId, user.Id, val);
                var deck = BlackjackLogic.CreateDeck(); deck.Shuffle();
                var pH = new List<Card> { deck[0], deck[1] }; deck.RemoveRange(0, 2);
                var dH = new List<Card> { deck[0], deck[1] }; deck.RemoveRange(0, 2);
                BlackjackAtivo[user.Id] = (pH, dH, deck, val);
                string path = await CasinoImageHelper.GerarImagemBlackjack(pH, dH, false, "BLACKJACK", new SKColor(140, 82, 198));
                var eb = new EmbedBuilder().WithAuthor($"Blackjack | {user.Username}").WithImageUrl($"attachment://bj.png").WithColor(new Color(160, 80, 220));
                var cb = new ComponentBuilder().WithButton("Pedir Carta", $"bj_hit_{user.Id}", ButtonStyle.Primary, new Emoji("🃏")).WithButton("Parar", $"bj_stand_{user.Id}", ButtonStyle.Success, new Emoji("🛑"));
                using (var s = File.OpenRead(path)) await msg.Channel.SendFileAsync(s, "bj.png", embed: eb.Build(), components: cb.Build());
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private async Task HandleButtons(SocketMessageComponent component)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var customId = component.Data.CustomId;
                    var partes = customId.Split('_');
                    if (partes.Length < 3) return;
                    var prefix = partes[0];
                    if (prefix != "roleta" && prefix != "cf" && prefix != "bj") return;
                    var escolha = partes[1];
                    var userId = ulong.Parse(partes[2]);
                    if (component.User.Id != userId) { await component.RespondAsync("<:erro:1493078898462949526> Não é seu jogo!", ephemeral: true); return; }
                    var guildId = (component.User as SocketGuildUser).Guild.Id;

                    if (prefix == "roleta")
                    {
                        if (!RoletaAtiva.TryGetValue(userId, out long val)) return;
                        if (escolha == "cancel") { RoletaAtiva.Remove(userId); EconomyHelper.AdicionarBanco(guildId, userId, val); await component.UpdateAsync(x => { x.Content = $"✅ Recuou {EconomyHelper.FormatarSaldo(val)}"; x.Embed = null; x.Components = null; }); return; }
                        RoletaAtiva.Remove(userId);
                        await component.UpdateAsync(x => { x.Embed = new EmbedBuilder().WithDescription("⚫ **Girando...**").WithImageUrl(GIF_ROLETA).Build(); x.Components = null; });
                        await Task.Delay(4000);
                        var rnd = new Random().Next(1, 101);
                        string cor = rnd <= 10 ? "branco" : (rnd <= 55 ? "preto" : "vermelho");
                        bool win = escolha == cor;
                        long pr = (long)(val * (cor == "branco" ? 6.0 : 1.5));
                        if (win) { EconomyHelper.AdicionarBanco(guildId, userId, pr); EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, userId, pr, "ROLETA_GANHO"); }
                        else EconomyHelper.RegistrarTransacao(guildId, userId, _client.CurrentUser.Id, val, "ROLETA_PERDA");
                        var eb = new EmbedBuilder().WithDescription(win ? $"🎉 Ganhou! Cor: {cor}. Prêmio: {EconomyHelper.FormatarSaldo(pr)}" : $"❌ Perdeu! Cor: {cor}.").WithColor(win ? Color.Green : Color.Red);
                        await component.ModifyOriginalResponseAsync(x => { x.Embed = eb.Build(); x.Content = component.User.Mention; });
                    }
                    else if (prefix == "cf")
                    {
                        if (!CoinflipAtivo.TryGetValue(userId, out long val)) return;
                        if (escolha == "cancel") { CoinflipAtivo.Remove(userId); EconomyHelper.AdicionarBanco(guildId, userId, val); await component.UpdateAsync(x => { x.Content = "✅ Cancelado."; x.Embed = null; x.Components = null; }); return; }
                        CoinflipAtivo.Remove(userId);
                        string res = new Random().Next(0, 2) == 0 ? "cara" : "coroa";
                        bool win = escolha == res;
                        if (win) { EconomyHelper.AdicionarBanco(guildId, userId, val * 2); EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, userId, val * 2, "COINFLIP_GANHO"); }
                        else EconomyHelper.RegistrarTransacao(guildId, userId, _client.CurrentUser.Id, val, "COINFLIP_PERDA");

                        string path = await CasinoImageHelper.GerarImagemCoinflip(res == "cara", win, val);
                        var eb = new EmbedBuilder().WithImageUrl($"attachment://cf.png").WithColor(win ? Color.Green : Color.Red);
                        using (var s = File.OpenRead(path)) { var att = new FileAttachment(s, "cf.png"); await component.UpdateAsync(x => { x.Embed = eb.Build(); x.Attachments = new[] { att }; x.Components = null; x.Content = component.User.Mention; }); }
                        if (File.Exists(path)) File.Delete(path);
                    }
                    else if (prefix == "bj")
                    {
                        if (!BlackjackAtivo.TryGetValue(userId, out var g)) return;
                        if (escolha == "hit")
                        {
                            g.Player.Add(g.Deck[0]); g.Deck.RemoveAt(0);
                            if (BlackjackLogic.CalculateScore(g.Player) > 21)
                            {
                                BlackjackAtivo.Remove(userId); EconomyHelper.RegistrarTransacao(guildId, userId, _client.CurrentUser.Id, g.Bet, "BLACKJACK_PERDA");
                                string p = await CasinoImageHelper.GerarImagemBlackjack(g.Player, g.Dealer, true, "ESTOUROU!", SKColors.Red);
                                using (var s = File.OpenRead(p)) { var a = new FileAttachment(s, "bj.png"); await component.UpdateAsync(x => { x.Embed = new EmbedBuilder().WithImageUrl("attachment://bj.png").WithColor(Color.Red).Build(); x.Attachments = new[] { a }; x.Components = null; }); }
                                if (File.Exists(p)) File.Delete(p);
                            }
                            else
                            {
                                string p = await CasinoImageHelper.GerarImagemBlackjack(g.Player, g.Dealer, false, "BLACKJACK", new SKColor(140, 82, 198));
                                using (var s = File.OpenRead(p)) { var a = new FileAttachment(s, "bj.png"); await component.UpdateAsync(x => { x.Attachments = new[] { a }; }); }
                                if (File.Exists(p)) File.Delete(p);
                            }
                        }
                        else if (escolha == "stand")
                        {
                            BlackjackAtivo.Remove(userId);
                            while (BlackjackLogic.CalculateScore(g.Dealer) < 17) { g.Dealer.Add(g.Deck[0]); g.Deck.RemoveAt(0); }
                            int pS = BlackjackLogic.CalculateScore(g.Player); int dS = BlackjackLogic.CalculateScore(g.Dealer);
                            bool win = dS > 21 || pS > dS; bool draw = pS == dS;
                            if (win) EconomyHelper.AdicionarBanco(guildId, userId, g.Bet * 2); else if (draw) EconomyHelper.AdicionarBanco(guildId, userId, g.Bet);
                            string p = await CasinoImageHelper.GerarImagemBlackjack(g.Player, g.Dealer, true, win ? "VITÓRIA!" : (draw ? "EMPATE!" : "DERROTA!"), win ? SKColors.Green : (draw ? SKColors.Gray : SKColors.Red));
                            using (var s = File.OpenRead(p)) { var a = new FileAttachment(s, "bj.png"); await component.UpdateAsync(x => { x.Embed = new EmbedBuilder().WithImageUrl("attachment://bj.png").WithColor(win ? Color.Green : Color.Red).Build(); x.Attachments = new[] { a }; x.Components = null; }); }
                            if (File.Exists(p)) File.Delete(p);
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[Botão Cassino Erro]: {ex.Message}"); }
            });
            await Task.CompletedTask;
        }
    }
}
