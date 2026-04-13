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
    // --- CLASSES DO BLACKJACK VISUAL ---
    public class Card
    {
        public string Suit { get; set; } // P (Paus), O (Ouros), C (Copas), E (Espadas)
        public string Value { get; set; } // 2-10, J, Q, K, A
        public int Score { get; set; } // 2-10, J,Q,K = 10, A = 1 ou 11

        // Nome do arquivo de imagem, ex: "k_spades.png"
        public string ImagePath => $"{Value.ToLower()}_{GetFullSuitName()}.png";

        private string GetFullSuitName()
        {
            return Suit switch { "P" => "clubs", "O" => "diamonds", "C" => "hearts", "E" => "spades", _ => "" };
        }
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

            while (total > 21 && aceCount > 0)
            {
                total -= 10;
                aceCount--;
            }
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

            // Mão do Dealer
            DesenharMao(canvas, "Mão do Dealer", dealerHand, dealerRevealed, 100, regularFont, boldFont);
            // Mão do Jogador
            DesenharMao(canvas, "Sua Mão", playerHand, true, 350, regularFont, boldFont);

            var p = Path.Combine(Path.GetTempPath(), $"bj_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot()) using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(p)) data.SaveTo(str);
            return p;
        }

        private static void DesenharMao(SKCanvas canvas, string title, List<Card> hand, bool revealed, float y, SKTypeface regularFont, SKTypeface boldFont)
        {
            // Background da área das cartas
            var bgRect = new SKRect(30, y, 870, y + 210);
            canvas.DrawRoundRect(bgRect, 20, 20, new SKPaint { Color = new SKColor(0, 0, 0, 60), IsAntialias = true });

            var paintTitle = new SKPaint { Color = SKColors.White, TextSize = 28, Typeface = boldFont, IsAntialias = true };
            canvas.DrawText(title, 60, y + 45, paintTitle);

            int score = revealed ? BlackjackLogic.CalculateScore(hand) : hand.Skip(1).Sum(c => c.Score);
            string scoreText = revealed ? $"Valor: {score}" : $"Valor: ? + {score}";

            // Fundo do score
            var scorePaint = new SKPaint { Color = SKColors.White, TextSize = 22, Typeface = boldFont, IsAntialias = true };
            float scoreWidth = scorePaint.MeasureText(scoreText);
            var scoreRect = new SKRect(840 - scoreWidth - 20, y + 20, 840, y + 60);
            canvas.DrawRoundRect(scoreRect, 10, 10, new SKPaint { Color = new SKColor(0, 0, 0, 100), IsAntialias = true });
            canvas.DrawText(scoreText, 840 - scoreWidth - 10, y + 48, scorePaint);

            // Centralizar cartas
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

            // Sombreado da carta para dar efeito 3D
            var shadowRect = new SKRect(x + 2, y + 2, x + 102, y + 142);
            canvas.DrawRoundRect(shadowRect, 8, 8, new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true });

            // Fundo da carta (Branco)
            canvas.DrawRoundRect(rect, 8, 8, new SKPaint { Color = SKColors.White, IsAntialias = true });

            if (isFaceDown)
            {
                // Desenha o verso da carta (um quadrado roxo com borda branca)
                var innerRect = new SKRect(x + 6, y + 6, x + 94, y + 134);
                canvas.DrawRoundRect(innerRect, 4, 4, new SKPaint { Color = new SKColor(110, 40, 180), IsAntialias = true }); // Cor roxa

                // Letra "Z" no meio para simbolizar a Zoe/Zany
                var paintLogo = new SKPaint { Color = SKColors.White, TextSize = 40, Typeface = font, TextAlign = SKTextAlign.Center, IsAntialias = true };
                canvas.DrawText("Z", x + 50, y + 85, paintLogo);
                return;
            }

            // Descobre o símbolo e a cor do naipe baseado na letra (P, O, C, E)
            string suitSymbol = card.Suit switch { "P" => "♣", "O" => "♦", "C" => "♥", "E" => "♠", _ => "?" };
            SKColor suitColor = (card.Suit == "O" || card.Suit == "C") ? SKColors.Red : SKColors.Black;
            string displayValue = card.Value.ToUpper();

            // Pincéis para os textos
            var paintText = new SKPaint { Color = suitColor, TextSize = 20, Typeface = font, TextAlign = SKTextAlign.Left, IsAntialias = true };
            var paintSmallSuit = new SKPaint { Color = suitColor, TextSize = 16, Typeface = font, TextAlign = SKTextAlign.Left, IsAntialias = true };
            var paintBigSuit = new SKPaint { Color = suitColor, TextSize = 50, Typeface = font, TextAlign = SKTextAlign.Center, IsAntialias = true };

            // Canto Superior Esquerdo (Valor + Naipe pequeno)
            canvas.DrawText(displayValue, x + 8, y + 24, paintText);
            canvas.DrawText(suitSymbol, x + 8, y + 42, paintSmallSuit);

            // Centro (Naipe gigante)
            canvas.DrawText(suitSymbol, x + 50, y + 90, paintBigSuit);

            // Canto Inferior Direito (Valor + Naipe pequeno invertido)
            var paintTextRight = new SKPaint { Color = suitColor, TextSize = 20, Typeface = font, TextAlign = SKTextAlign.Right, IsAntialias = true };
            var paintSmallSuitRight = new SKPaint { Color = suitColor, TextSize = 16, Typeface = font, TextAlign = SKTextAlign.Right, IsAntialias = true };

            canvas.DrawText(displayValue, x + 92, y + 130, paintTextRight);
            canvas.DrawText(suitSymbol, x + 92, y + 112, paintSmallSuitRight);
        }

        // --- GERADOR DE IMAGEM DO CRASH ESTÉTICO ---
        public static async Task<string> GerarImagemCrash(double multiplicador, string status)
        {
            int w = 600; int h = 300;
            using var surface = SKSurface.Create(new SKImageInfo(w, h));
            var canvas = surface.Canvas;

            var corRoxoZoe = new SKColor(160, 80, 220);
            var corRoxoEscuroZoe = new SKColor(110, 40, 180);
            var corFontePrincipal = SKColors.White;
            var corLinhaGrafico = SKColors.White;

            SKTypeface tfBold = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);
            string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Poppins-Bold.ttf");
            if (File.Exists(fontPath)) { using (var stream = File.OpenRead(fontPath)) tfBold = SKTypeface.FromStream(stream); }

            var rect = new SKRect(0, 0, w, h);
            var colorsFundo = new SKColor[] { corRoxoZoe, corRoxoEscuroZoe };
            var shaderFundo = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), colorsFundo, null, SKShaderTileMode.Clamp);
            using (var paintFundo = new SKPaint { Shader = shaderFundo, IsAntialias = true })
            {
                canvas.DrawRoundRect(rect, 20, 20, paintFundo);
            }

            var paintStatus = new SKPaint { Color = corFontePrincipal, TextSize = 24, Typeface = tfBold, IsAntialias = true };
            string emoji = status == "JOGANDO" ? "⚙️" : (status == "CRASH" ? "💥" : "✅");
            string textoStatus = $"{emoji} {status.ToUpper()}";
            canvas.DrawText(textoStatus, 40, 60, paintStatus);

            var paintMult = new SKPaint { Color = corFontePrincipal, TextSize = 80, Typeface = tfBold, TextAlign = SKTextAlign.Center, IsAntialias = true };
            canvas.DrawText($"{multiplicador:F2}x", w / 2, 160, paintMult);

            float startX = 60; float startY = h - 60;
            float endX = w / 2.5f;
            float heightOffset = Math.Min((float)(multiplicador * 15), 120);
            float endY = startY - heightOffset;

            using (var paintBrilho = new SKPaint { Color = corLinhaGrafico, StrokeWidth = 10, Style = SKPaintStyle.Stroke, IsAntialias = true })
            {
                paintBrilho.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5);
                canvas.DrawLine(startX, startY, endX, endY, paintBrilho);
            }

            using (var paintLinhaPrincipal = new SKPaint { Color = corLinhaGrafico, StrokeWidth = 6, Style = SKPaintStyle.Stroke, IsAntialias = true })
            {
                canvas.DrawLine(startX, startY, endX, endY, paintLinhaPrincipal);
            }

            if (status == "JOGANDO" || status == "WIN")
            {
                var imgPathAviao = Path.Combine(AppContext.BaseDirectory, "Assets", "image_12.png");
                if (File.Exists(imgPathAviao))
                {
                    using (var streamAviao = File.OpenRead(imgPathAviao))
                    using (var originalBitmap = SKBitmap.Decode(streamAviao))
                    {
                        using (var surfaceAviaoRoxo = SKSurface.Create(new SKImageInfo(originalBitmap.Width, originalBitmap.Height)))
                        {
                            var canvasAviaoRoxo = surfaceAviaoRoxo.Canvas;
                            canvasAviaoRoxo.Clear(corRoxoZoe);

                            using (var paintMask = new SKPaint { BlendMode = SKBlendMode.DstIn, IsAntialias = true })
                            {
                                canvasAviaoRoxo.DrawBitmap(originalBitmap, 0, 0, paintMask);
                            }

                            using (var aviaoRoxoImage = surfaceAviaoRoxo.Snapshot())
                            {
                                double dy = endY - startY;
                                double dx = endX - startX;
                                double anguloRad = Math.Atan2(dy, dx);
                                float anguloGraus = (float)(anguloRad * (180 / Math.PI));

                                float aviaoX = endX;
                                float aviaoY = endY;
                                float esc = 0.3f;

                                canvas.Save();
                                canvas.Translate(aviaoX, aviaoY);
                                canvas.RotateDegrees(anguloGraus);
                                canvas.Scale(esc);
                                canvas.Translate(-aviaoRoxoImage.Width / 2f, -aviaoRoxoImage.Height / 2f);

                                canvas.DrawImage(aviaoRoxoImage, 0, 0);

                                canvas.Restore();
                            }
                        }
                    }
                }
            }

            using (var paintBase = new SKPaint { Color = new SKColor(255, 255, 255, 100), StrokeWidth = 2, Style = SKPaintStyle.Stroke, IsAntialias = true })
            {
                paintBase.PathEffect = SKPathEffect.CreateDash(new float[] { 10, 10 }, 0);
                canvas.DrawLine(startX, startY + 20, w - 60, startY + 20, paintBase);
            }

            var pathImg = Path.Combine(Path.GetTempPath(), $"crash_{Guid.NewGuid()}.png");
            using (var img = surface.Snapshot()) using (var d = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var s = File.OpenWrite(pathImg)) d.SaveTo(s);

            return pathImg;
        }
    }


    // --- MODULO PRINCIPAL ---
    public class CassinoModule
    {
        private readonly DiscordSocketClient _client;

        // Cooldown exclusivo para os jogos (5 segundos)
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();

        private static readonly Dictionary<ulong, long> CoinflipAtivo = new();
        private static readonly Dictionary<ulong, long> RoletaAtiva = new();

        // Alterado para suportar o baralho de objetos da nova versão do BJ
        private static readonly Dictionary<ulong, (List<Card> Player, List<Card> Dealer, List<Card> Deck, long Bet)> BlackjackAtivo = new();

        // Variável de controle do CRASH
        private static readonly Dictionary<ulong, (double MultiplicadorAtual, bool Retirou, long Aposta)> CrashGamesAtivos = new();

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
            var user = msg.Author as SocketGuildUser;
            var guildId = user.Guild.Id;

            string[] cmds = { "zroleta", "zcf", "zcoinflip", "zbj", "zblackjack", "zcrash" };
            if (!cmds.Any(c => content.StartsWith(c))) return;
            if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 2)
            {
                var aviso = await msg.Channel.SendMessageAsync($"⏳ {user.Mention}, vá com calma! Aguarde **2 segundos** para apostar novamente.");
                _ = Task.Delay(2000).ContinueWith(_ => aviso.DeleteAsync());
                return;
            }
            _cooldowns[user.Id] = DateTime.UtcNow;
            // ---------------------------

            // --- ZROLETA ---
            if (content.StartsWith("zroleta"))
            {
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
                if (RoletaAtiva.ContainsKey(user.Id)) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você já tem um jogo em andamento! Termine ele antes de começar outro.!"); return; }

                RoletaAtiva[user.Id] = valorAposta;
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
                string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zcoinflip (valor)`"); return; }
                long banco = EconomyHelper.GetBanco(guildId, user.Id);
                string valTxt = p[1].ToLower();
                long val = valTxt == "all" ? banco : (valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.TryParse(valTxt, out var res) ? res : 0);

                if (val <= 0 || banco < val)
                {
                    await msg.Channel.SendMessageAsync($@"<:erro:1493078898462949526> Você não possui **{EconomyHelper.FormatarSaldo(val)} coins** no banco para apostar.");
                    return;
                }
                if (CoinflipAtivo.ContainsKey(user.Id))
                {
                    await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você já tem um jogo em andamento! Termine ele antes de começar outro.");
                    return;
                }

                CoinflipAtivo[user.Id] = val;
                EconomyHelper.RemoverBanco(guildId, user.Id, val);

                // Embed com o design idêntico ao seu Print
                var eb = new EmbedBuilder()
                    .WithAuthor("Cara ou Coroa", IMG_MOEDA)
                    .WithDescription($@"• **Olá,** {user.Mention}**!** Bem-vindo(a) ao jogo **Cara** ou **Coroa**.

<:6821purplecash:1493263367488536606> | **Valor em aposta:** `{EconomyHelper.FormatarSaldo(val)}`

<:seta:1493089125979656385> | **Como funciona:**
Escolha entre **Cara** ou **Coroa** e aposte. Se acertar, você ganha o dobro da aposta; se errar, você perde o valor apostado.

<:erro:1493078898462949526> | **Desistir da aposta:**
Se decidir não continuar, clique no <:erro:1493078898462949526> para desistir da aposta.")
                    .WithFooter($"Apostador: {user.Username} • Hoje às {DateTime.Now:HH:mm}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .WithColor(new Color(160, 80, 220)); // Cor roxa da borda

                // Botões cinzas com os emojis corretos
                var cb = new ComponentBuilder()
                    .WithButton("Cara", $"cf_cara_{user.Id}", ButtonStyle.Secondary, new Emoji("🙂"))
                    .WithButton("Coroa", $"cf_coroa_{user.Id}", ButtonStyle.Secondary, new Emoji("👑"))
                    .WithButton(null, $"cf_cancel_{user.Id}", ButtonStyle.Secondary, Emote.Parse("<:erro:1493078898462949526>"));

                await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
            }

            // --- ZBJ / ZBLACKJACK ---
            else if (content.StartsWith("zbj") || content.StartsWith("zblackjack"))
            {
                string[] p = content.Split(' '); if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Uso:** `zbj [valor]`"); return; }
                long banco = EconomyHelper.GetBanco(guildId, user.Id);
                string valTxt = p[1].ToLower();
                long val = valTxt == "all" ? banco : (valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.Parse(valTxt));

                if (val <= 0 || banco < val || BlackjackAtivo.ContainsKey(user.Id)) return;

                EconomyHelper.RemoverBanco(guildId, user.Id, val);

                var deck = BlackjackLogic.CreateDeck();
                deck.Shuffle();

                var playerHand = new List<Card> { deck[0], deck[1] };
                deck.RemoveRange(0, 2);

                var dealerHand = new List<Card> { deck[0], deck[1] };
                deck.RemoveRange(0, 2);

                BlackjackAtivo[user.Id] = (playerHand, dealerHand, deck, val);

                string imgPath = await CasinoImageHelper.GerarImagemBlackjack(playerHand, dealerHand, false, "BLACKJACK", new SKColor(140, 82, 198)); // Cor Roxa Base

                var eb = new EmbedBuilder()
                    .WithAuthor($"Blackjack | {user.Username}", _client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                    .WithDescription($@"• 💸 **Aposta:** {EconomyHelper.FormatarSaldo(val)}
  ◦ 💵 **Possível ganho:** {EconomyHelper.FormatarSaldo(val * 2)}")
                    .WithImageUrl($"attachment://{Path.GetFileName(imgPath)}")
                    .WithFooter($"Apostador: {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .WithColor(new Color(160, 80, 220));

                var cb = new ComponentBuilder()
                    .WithButton("Pedir Carta", $"bj_hit_{user.Id}", ButtonStyle.Primary, new Emoji("🃏"))
                    .WithButton("Parar", $"bj_stand_{user.Id}", ButtonStyle.Success, new Emoji("🛑"));

                using (var stream = File.OpenRead(imgPath))
                {
                    await msg.Channel.SendFileAsync(stream, Path.GetFileName(imgPath), embed: eb.Build(), components: cb.Build());
                }

                if (File.Exists(imgPath)) File.Delete(imgPath);
            }

            // --- ZCRASH ---
            else if (content.StartsWith("zcrash"))
            {
                string[] p = content.Split(' ');
                if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Uso:** `zcrash [valor]` ou `zcrash all`"); return; }

                long banco = EconomyHelper.GetBanco(guildId, user.Id);
                string valTxt = p[1].ToLower();
                long aposta = valTxt == "all" ? banco : (valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.TryParse(valTxt, out var v) ? v : 0);

                if (aposta < 10) { await msg.Channel.SendMessageAsync("❌ Aposta mínima: 10 cpoints."); return; }
                if (aposta > banco) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Saldo no banco insuficiente."); return; }
                if (CrashGamesAtivos.ContainsKey(user.Id)) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você já tem um jogo de Crash em andamento!"); return; }

                EconomyHelper.RemoverBanco(guildId, user.Id, aposta);

                double rand = new Random().NextDouble();
                double crashPoint = Math.Max(1.0, 0.99 / (1.0 - rand));
                if (crashPoint > 50.0) crashPoint = 50.0;
                if (crashPoint < 1.05) crashPoint = 1.0;

                CrashGamesAtivos[user.Id] = (1.0, false, aposta);

                string imgPath = await CasinoImageHelper.GerarImagemCrash(1.0, "JOGANDO");

                var eb = new EmbedBuilder()
                    .WithAuthor($"Crash | {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .WithDescription($"• 💸 **Aposta:** `{EconomyHelper.FormatarSaldo(aposta)}`\n• 💰 **Ganhos:** `{EconomyHelper.FormatarSaldo(aposta)}`")
                    .WithColor(new Color(140, 82, 255))
                    .WithImageUrl($"attachment://{Path.GetFileName(imgPath)}");

                var cb = new ComponentBuilder().WithButton($"Retirar {EconomyHelper.FormatarSaldo(aposta)}", $"crash_retirar_{user.Id}", ButtonStyle.Success, new Emoji("💸"));

                Discord.Rest.RestUserMessage jogoMsg;
                using (var stream = File.OpenRead(imgPath))
                {
                    jogoMsg = await msg.Channel.SendFileAsync(stream, Path.GetFileName(imgPath), embed: eb.Build(), components: cb.Build());
                }
                if (File.Exists(imgPath)) File.Delete(imgPath);

                // LOOP DO CRASH
                _ = Task.Run(async () =>
                {
                    double currentMult = 1.0;
                    bool bateuCrash = false;

                    while (!bateuCrash)
                    {
                        await Task.Delay(2000);

                        if (CrashGamesAtivos.TryGetValue(user.Id, out var state) && state.Retirou) break;

                        currentMult += 0.15 + (currentMult * 0.05);

                        if (currentMult >= crashPoint)
                        {
                            currentMult = crashPoint;
                            bateuCrash = true;
                        }

                        if (CrashGamesAtivos.TryGetValue(user.Id, out var currentState) && currentState.Retirou) break;
                        CrashGamesAtivos[user.Id] = (currentMult, false, aposta);

                        string newStatus = bateuCrash ? "CRASH" : "JOGANDO";
                        string newImg = await CasinoImageHelper.GerarImagemCrash(currentMult, newStatus);

                        var newEb = new EmbedBuilder()
                            .WithAuthor($"Crash | {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                            .WithColor(bateuCrash ? Color.Red : new Color(140, 82, 255))
                            .WithImageUrl($"attachment://{Path.GetFileName(newImg)}");

                        using (var stream = File.OpenRead(newImg))
                        {
                            var attachment = new FileAttachment(stream, Path.GetFileName(newImg));

                            if (bateuCrash)
                            {
                                CrashGamesAtivos.Remove(user.Id);
                                EconomyHelper.RegistrarTransacao(guildId, user.Id, _client.CurrentUser.Id, aposta, "CRASH_PERDA");
                                newEb.WithDescription($"💥 **CRASH!**\n• 💸 **Aposta perdida:** `{EconomyHelper.FormatarSaldo(aposta)}`");
                                var btnPerdeu = new ComponentBuilder().WithButton("Perdeu", "btn_disabled", ButtonStyle.Danger, disabled: true);

                                try { await jogoMsg.ModifyAsync(x => { x.Embed = newEb.Build(); x.Attachments = new[] { attachment }; x.Components = btnPerdeu.Build(); }); } catch { }
                            }
                            else
                            {
                                long ganhoAtual = (long)(aposta * currentMult);
                                newEb.WithDescription($"• 💸 **Aposta:** `{EconomyHelper.FormatarSaldo(aposta)}`\n• 💰 **Ganhos:** `{EconomyHelper.FormatarSaldo(ganhoAtual)}`");
                                var btnAtivo = new ComponentBuilder().WithButton($"Retirar {EconomyHelper.FormatarSaldo(ganhoAtual)}", $"crash_retirar_{user.Id}", ButtonStyle.Success, new Emoji("💸"));

                                try { await jogoMsg.ModifyAsync(x => { x.Embed = newEb.Build(); x.Attachments = new[] { attachment }; x.Components = btnAtivo.Build(); }); } catch { }
                            }
                        }
                        if (File.Exists(newImg)) File.Delete(newImg);
                    }
                });
            }
        }

        private async Task HandleButtons(SocketMessageComponent component)
        {
            var customId = component.Data.CustomId;
            var partes = customId.Split('_');
            if (partes.Length < 3) return;

            var prefix = partes[0];

            // 👇 ADICIONE ESTA LINHA AQUI! Ela faz o Cassino ignorar os botões da Aposta
            if (prefix != "roleta" && prefix != "cf" && prefix != "bj" && prefix != "crash") return;

            var escolha = partes[1];
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
                if (!RoletaAtiva.TryGetValue(userId, out long valorAposta)) { await component.RespondAsync("❌ Jogo finalizado ou erro.", ephemeral: true); return; }

                if (escolha == "cancel")
                {
                    RoletaAtiva.Remove(userId);
                    EconomyHelper.AdicionarBanco(guildId, userId, valorAposta);
                    await component.UpdateAsync(x => {
                        x.Content = $"<:acerto:1493079138783727756> {component.User.Mention} desistiu e recuperou seus `{EconomyHelper.FormatarSaldo(valorAposta)}` cpoints no banco.";
                        x.Embed = null; x.Components = null;
                    });
                    return;
                }

                RoletaAtiva.Remove(userId);
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

                var embedFim = new EmbedBuilder().WithAuthor("Resultado da Roleta", "https://cdn-icons-png.flaticon.com/512/1055/1055823.png").WithFooter($"Apostador: {component.User.Username}", component.User.GetAvatarUrl() ?? component.User.GetDefaultAvatarUrl()).WithTimestamp(DateTime.Now);

                if (ganhou)
                {
                    EconomyHelper.AdicionarBanco(guildId, userId, premio);
                    EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, userId, premio, "ROLETA_GANHO"); // Registra o Log de Ganho
                    embedFim.WithColor(Color.Green).WithDescription($@"<a:ganhador:1493088070923452599> **Parabéns! A sorte passou por aqui!**

🎡 A roleta parou no: {emojiCor} **{corSorteada.ToUpper()}**
<a:7moneyz:1493015410637930508> Você recebeu: `{EconomyHelper.FormatarSaldo(premio)}` cpoints no banco.");
                }
                else
                {
                    EconomyHelper.RegistrarTransacao(guildId, userId, _client.CurrentUser.Id, valorAposta, "ROLETA_PERDA"); // Registra o Log de Perda
                    embedFim.WithColor(Color.Red).WithDescription($@"<:erro:1493078898462949526> **Não foi dessa vez...**

🎡 A roleta parou no: {emojiCor} **{corSorteada.ToUpper()}**
<:erro:1493078898462949526> Você perdeu: `{EconomyHelper.FormatarSaldo(valorAposta)}` cpoints do banco.");
                }

                await component.ModifyOriginalResponseAsync(x => { x.Embed = embedFim.Build(); x.Content = component.User.Mention; });
            }

            // --- BOTÕES COINFLIP ---
            else if (prefix == "cf")
            {
                if (!CoinflipAtivo.TryGetValue(userId, out long val)) { await component.RespondAsync("❌ Jogo finalizado ou erro.", ephemeral: true); return; }
                if (escolha == "cancel") { CoinflipAtivo.Remove(userId); EconomyHelper.AdicionarBanco(guildId, userId, val); await component.UpdateAsync(x => { x.Content = $"✅ {component.User.Mention} desistiu."; x.Embed = null; x.Components = null; }); return; }

                CoinflipAtivo.Remove(userId);
                string res = new Random().Next(0, 2) == 0 ? "cara" : "coroa"; bool win = escolha == res;
                var eb = new EmbedBuilder().WithAuthor("Cara ou Coroa", IMG_MOEDA).WithThumbnailUrl(IMG_MOEDA);

                if (win)
                {
                    EconomyHelper.AdicionarBanco(guildId, userId, val * 2);
                    EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, userId, val * 2, "COINFLIP_GANHO"); // Registra Vitória
                    eb.WithColor(Color.Green).WithDescription($"Ganhou! Deu **{res}**.\n<a:ganhador:1493088070923452599> <:mais:1493267829611303023> {EconomyHelper.FormatarSaldo(val * 2)}");
                }
                else
                {
                    EconomyHelper.RegistrarTransacao(guildId, userId, _client.CurrentUser.Id, val, "COINFLIP_PERDA"); // Registra Derrota
                    eb.WithColor(Color.Red).WithDescription($"Perdeu! Deu **{res}**.\n❌ -{EconomyHelper.FormatarSaldo(val)}");
                }

                await component.UpdateAsync(x => { x.Embed = eb.Build(); x.Components = null; x.Content = component.User.Mention; });
            }

            // --- BOTÕES BLACKJACK ---
            else if (prefix == "bj")
            {
                if (!BlackjackAtivo.TryGetValue(userId, out var game)) { await component.RespondAsync("❌ Jogo finalizado ou erro.", ephemeral: true); return; }

                if (escolha == "hit")
                {
                    game.Player.Add(game.Deck[0]);
                    game.Deck.RemoveAt(0);

                    int pS = BlackjackLogic.CalculateScore(game.Player);

                    if (pS > 21) // Estourou - Perdeu
                    {
                        BlackjackAtivo.Remove(userId);
                        EconomyHelper.RegistrarTransacao(guildId, userId, _client.CurrentUser.Id, game.Bet, "BLACKJACK_PERDA");

                        string imgLose = await CasinoImageHelper.GerarImagemBlackjack(game.Player, game.Dealer, true, "ESTOUROU!", new SKColor(180, 20, 20));

                        var ebLose = new EmbedBuilder()
                            .WithAuthor($"Blackjack | {component.User.Username}", _client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                            .WithDescription($@"<:explosao:1493358933610332342> **ESTOUROU!**

• <:moedazoe:1493359715420340364> **Aposta Perdida:** {EconomyHelper.FormatarSaldo(game.Bet)}")
                            .WithImageUrl($"attachment://{Path.GetFileName(imgLose)}")
                            .WithFooter($"Apostador: {component.User.Username}", component.User.GetAvatarUrl() ?? component.User.GetDefaultAvatarUrl())
                            .WithColor(Color.Red);

                        using (var stream = File.OpenRead(imgLose))
                        {
                            var attachment = new FileAttachment(stream, Path.GetFileName(imgLose));
                            await component.UpdateAsync(x => { x.Embed = ebLose.Build(); x.Attachments = new[] { attachment }; x.Components = null; });
                        }
                        if (File.Exists(imgLose)) File.Delete(imgLose);
                        return;
                    }

                    // Continua jogando
                    string imgPlay = await CasinoImageHelper.GerarImagemBlackjack(game.Player, game.Dealer, false, "BLACKJACK", new SKColor(140, 82, 198));
                    var ebPlay = new EmbedBuilder()
                        .WithAuthor($"Blackjack | {component.User.Username}", _client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                        .WithDescription($@"• <:moedazoe:1493359715420340364> **Aposta:** {EconomyHelper.FormatarSaldo(game.Bet)}
  ◦ <:dinheiro:1493360319928733838> **Possível ganho:** {EconomyHelper.FormatarSaldo(game.Bet * 2)}")
                        .WithImageUrl($"attachment://{Path.GetFileName(imgPlay)}")
                        .WithFooter($"Apostador: {component.User.Username}", component.User.GetAvatarUrl() ?? component.User.GetDefaultAvatarUrl())
                        .WithColor(new Color(160, 80, 220));

                    using (var stream = File.OpenRead(imgPlay))
                    {
                        var attachment = new FileAttachment(stream, Path.GetFileName(imgPlay));
                        await component.UpdateAsync(x => { x.Embed = ebPlay.Build(); x.Attachments = new[] { attachment }; });
                    }
                    if (File.Exists(imgPlay)) File.Delete(imgPlay);
                }
                else if (escolha == "stand")
                {
                    BlackjackAtivo.Remove(userId);
                    int pS = BlackjackLogic.CalculateScore(game.Player);

                    while (BlackjackLogic.CalculateScore(game.Dealer) < 17)
                    {
                        game.Dealer.Add(game.Deck[0]);
                        game.Deck.RemoveAt(0);
                    }

                    int dS = BlackjackLogic.CalculateScore(game.Dealer);
                    string resT = ""; SKColor bgCol; Color ebCol; string statusDesc = "";

                    if (dS > 21 || pS > dS)
                    {
                        resT = "VITÓRIA!";
                        EconomyHelper.AdicionarBanco(guildId, userId, game.Bet * 2);
                        EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, userId, game.Bet * 2, "BLACKJACK_GANHO");
                        bgCol = new SKColor(40, 180, 80); ebCol = Color.Green;
                        statusDesc = $@"<a:ganhador:1493088070923452599> **BlackJack!** **VITÓRIA CONFIRMADA!**

• <:moedazoe:1493359715420340364> **Aposta:** {EconomyHelper.FormatarSaldo(game.Bet)}

  ◦ <:dinheiro:1493360319928733838> **Ganhos:** {EconomyHelper.FormatarSaldo(game.Bet * 2)}";
                    }
                    else if (pS == dS)
                    {
                        resT = "EMPATE!";
                        EconomyHelper.AdicionarBanco(guildId, userId, game.Bet);
                        EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, userId, game.Bet, "BLACKJACK_EMPATE");
                        bgCol = new SKColor(120, 120, 120); ebCol = Color.LightGrey;
                        statusDesc = $@"<:perdeu:1493361130075328754> **EMPATE!**

• <:moedazoe:1493359715420340364> **Aposta:** {EconomyHelper.FormatarSaldo(game.Bet)}

  ◦ <:dinheiro:1493360319928733838> **Devolvido:** {EconomyHelper.FormatarSaldo(game.Bet)}";
                    }
                    else
                    {
                        resT = "DERROTA!";
                        EconomyHelper.RegistrarTransacao(guildId, userId, _client.CurrentUser.Id, game.Bet, "BLACKJACK_PERDA");
                        bgCol = new SKColor(180, 40, 40); ebCol = Color.Red;
                        statusDesc = $@"<:perdeu:1493361130075328754> **DERROTA!**

• <:moedazoe:1493359715420340364> **Aposta Perdida:** {EconomyHelper.FormatarSaldo(game.Bet)}";
                    }

                    string imgEnd = await CasinoImageHelper.GerarImagemBlackjack(game.Player, game.Dealer, true, resT, bgCol);

                    var ebEnd = new EmbedBuilder()
                        .WithAuthor($"Blackjack | {component.User.Username}", _client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                        .WithDescription(statusDesc)
                        .WithImageUrl($"attachment://{Path.GetFileName(imgEnd)}")
                        .WithFooter($" Apostador: {component.User.Username}", component.User.GetAvatarUrl() ?? component.User.GetDefaultAvatarUrl())
                        .WithColor(ebCol);

                    using (var stream = File.OpenRead(imgEnd))
                    {
                        var attachment = new FileAttachment(stream, Path.GetFileName(imgEnd));
                        await component.UpdateAsync(x => { x.Embed = ebEnd.Build(); x.Attachments = new[] { attachment }; x.Components = null; });
                    }
                    if (File.Exists(imgEnd)) File.Delete(imgEnd);
                }
            }

            // --- BOTÃO DE RETIRAR DO CRASH ---
            else if (prefix == "crash")
            {
                if (escolha == "retirar")
                {
                    if (CrashGamesAtivos.TryGetValue(userId, out var state))
                    {
                        if (state.Retirou) return;

                        CrashGamesAtivos[userId] = (state.MultiplicadorAtual, true, state.Aposta);
                        long lucroTotal = (long)(state.Aposta * state.MultiplicadorAtual);

                        EconomyHelper.AdicionarBanco(guildId, userId, lucroTotal);
                        EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, userId, lucroTotal, "CRASH_GANHO");

                        string imgWin = await CasinoImageHelper.GerarImagemCrash(state.MultiplicadorAtual, "WIN");

                        var ebWin = new EmbedBuilder()
                            .WithAuthor($"Crash | {component.User.Username}", component.User.GetAvatarUrl() ?? component.User.GetDefaultAvatarUrl())
                            .WithDescription($"✅ **VOCÊ RETIROU!**\n• 💸 **Aposta:** `{EconomyHelper.FormatarSaldo(state.Aposta)}`\n• 💰 **Lucro Total:** `{EconomyHelper.FormatarSaldo(lucroTotal)}`")
                            .WithColor(new Color(46, 204, 113))
                            .WithImageUrl($"attachment://{Path.GetFileName(imgWin)}");

                        var btnWin = new ComponentBuilder().WithButton($"Ganhou {EconomyHelper.FormatarSaldo(lucroTotal)}", "btn_win", ButtonStyle.Success, disabled: true);

                        using (var stream = File.OpenRead(imgWin))
                        {
                            var attachment = new FileAttachment(stream, Path.GetFileName(imgWin));
                            await component.UpdateAsync(x => { x.Embed = ebWin.Build(); x.Attachments = new[] { attachment }; x.Components = btnWin.Build(); });
                        }
                        if (File.Exists(imgWin)) File.Delete(imgWin);

                        CrashGamesAtivos.Remove(userId);
                    }
                    else
                    {
                        await component.RespondAsync("❌ Esse jogo já terminou ou você tomou Crash.", ephemeral: true);
                    }
                }
            }
        }
    }
}
