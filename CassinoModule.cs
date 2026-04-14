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
    // --- CLASSES DO BLACKJACK (MANTIDAS) ---
    public class Card { public string Suit { get; set; } public string Value { get; set; } public int Score { get; set; } public string ImagePath => $"{Value.ToLower()}_{Suit switch { "P" => "clubs", "O" => "diamonds", "C" => "hearts", "E" => "spades", _ => "" }}.png"; }
    public static class BlackjackLogic { public static List<Card> CreateDeck() { string[] suits = { "P", "O", "C", "E" }; string[] values = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "j", "q", "k", "a" }; var deck = new List<Card>(); foreach (var suit in suits) { foreach (var value in values) { deck.Add(new Card { Suit = suit, Value = value, Score = int.TryParse(value, out int s) ? s : (value == "a" ? 11 : 10) }); } } return deck; } public static int CalculateScore(List<Card> hand) { int total = hand.Sum(c => c.Score); int aceCount = hand.Count(c => c.Value == "a"); while (total > 21 && aceCount > 0) { total -= 10; aceCount--; } return total; } public static void Shuffle(this List<Card> deck) { var r = new Random(); for (int i = deck.Count - 1; i > 0; i--) { int j = r.Next(i + 1); (deck[i], deck[j]) = (deck[j], deck[i]); } } }

    public static class CasinoImageHelper
    {
        public static async Task<string> GerarImagemBlackjack(List<Card> playerHand, List<Card> dealerHand, bool dealerRevealed, string stateText, SKColor accent) { int width = 900; int height = 600; using var surface = SKSurface.Create(new SKImageInfo(width, height)); var canvas = surface.Canvas; canvas.Clear(accent); var boldFont = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold); var regularFont = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Normal); canvas.DrawText(stateText, width / 2, 70, new SKPaint { Color = SKColors.White, TextSize = 48, Typeface = boldFont, TextAlign = SKTextAlign.Center, IsAntialias = true }); DesenharMao(canvas, "Mão do Dealer", dealerHand, dealerRevealed, 100, regularFont, boldFont); DesenharMao(canvas, "Sua Mão", playerHand, true, 350, regularFont, boldFont); var p = Path.Combine(Path.GetTempPath(), $"bj_{DateTime.Now.Ticks}.png"); using (var img = surface.Snapshot()) using (var data = img.Encode(SKEncodedImageFormat.Png, 100)) using (var str = File.OpenWrite(p)) data.SaveTo(str); return p; }
        private static void DesenharMao(SKCanvas canvas, string title, List<Card> hand, bool revealed, float y, SKTypeface regularFont, SKTypeface boldFont) { var bgRect = new SKRect(30, y, 870, y + 210); canvas.DrawRoundRect(bgRect, 20, 20, new SKPaint { Color = new SKColor(0, 0, 0, 60), IsAntialias = true }); canvas.DrawText(title, 60, y + 45, new SKPaint { Color = SKColors.White, TextSize = 28, Typeface = boldFont, IsAntialias = true }); int score = revealed ? BlackjackLogic.CalculateScore(hand) : hand.Skip(1).Sum(c => c.Score); string scoreText = revealed ? $"Valor: {score}" : $"Valor: ? + {score}"; var scorePaint = new SKPaint { Color = SKColors.White, TextSize = 22, Typeface = boldFont, IsAntialias = true }; float scoreWidth = scorePaint.MeasureText(scoreText); canvas.DrawRoundRect(new SKRect(840 - scoreWidth - 20, y + 20, 840, y + 60), 10, 10, new SKPaint { Color = new SKColor(0, 0, 0, 100), IsAntialias = true }); canvas.DrawText(scoreText, 840 - scoreWidth - 10, y + 48, scorePaint); float cardWidth = 100; float startX = (900 - ((hand.Count * cardWidth) + ((hand.Count - 1) * 15))) / 2; for (int i = 0; i < hand.Count; i++) { DesenharCartaCriadaNoCodigo(canvas, hand[i], !revealed && i == 0, startX, y + 50, boldFont); startX += cardWidth + 15; } }
        private static void DesenharCartaCriadaNoCodigo(SKCanvas canvas, Card card, bool isFaceDown, float x, float y, SKTypeface font) { var rect = new SKRect(x, y, x + 100, y + 140); canvas.DrawRoundRect(new SKRect(x + 2, y + 2, x + 102, y + 142), 8, 8, new SKPaint { Color = new SKColor(0, 0, 0, 80), IsAntialias = true }); canvas.DrawRoundRect(rect, 8, 8, new SKPaint { Color = SKColors.White, IsAntialias = true }); if (isFaceDown) { canvas.DrawRoundRect(new SKRect(x + 6, y + 6, x + 94, y + 134), 4, 4, new SKPaint { Color = new SKColor(110, 40, 180), IsAntialias = true }); canvas.DrawText("Z", x + 50, y + 85, new SKPaint { Color = SKColors.White, TextSize = 40, Typeface = font, TextAlign = SKTextAlign.Center, IsAntialias = true }); return; } SKColor suitColor = (card.Suit == "O" || card.Suit == "C") ? SKColors.Red : SKColors.Black; string suitSymbol = card.Suit switch { "P" => "♣", "O" => "♦", "C" => "♥", "E" => "♠", _ => "?" }; string displayValue = card.Value.ToUpper(); canvas.DrawText(displayValue, x + 8, y + 24, new SKPaint { Color = suitColor, TextSize = 20, Typeface = font, TextAlign = SKTextAlign.Left, IsAntialias = true }); canvas.DrawText(suitSymbol, x + 8, y + 42, new SKPaint { Color = suitColor, TextSize = 16, Typeface = font, TextAlign = SKTextAlign.Left, IsAntialias = true }); canvas.DrawText(suitSymbol, x + 50, y + 90, new SKPaint { Color = suitColor, TextSize = 50, Typeface = font, TextAlign = SKTextAlign.Center, IsAntialias = true }); canvas.DrawText(displayValue, x + 92, y + 130, new SKPaint { Color = suitColor, TextSize = 20, Typeface = font, TextAlign = SKTextAlign.Right, IsAntialias = true }); canvas.DrawText(suitSymbol, x + 92, y + 112, new SKPaint { Color = suitColor, TextSize = 16, Typeface = font, TextAlign = SKTextAlign.Right, IsAntialias = true }); }

        // --- GERADOR DE IMAGEM DO CRASH (IDENTICA À PRINT) ---
        public static async Task<string> GerarImagemCrash(double multiplicador, string status)
        {
            int w = 600; int h = 300;
            using var surface = SKSurface.Create(new SKImageInfo(w, h));
            var canvas = surface.Canvas;

            SKColor corFundo = status == "WIN" ? SKColor.Parse("#3dbb7e") : SKColor.Parse("#4caf50"); 
            if (status == "CRASH") corFundo = SKColor.Parse("#e74c3c");
            
            canvas.Clear(SKColors.Transparent);
            using (var paintFundo = new SKPaint { Color = corFundo, IsAntialias = true })
            {
                canvas.DrawRoundRect(new SKRect(0, 0, w, h), 20, 20, paintFundo);
            }

            var fontBold = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);

            // TEXTO SUPERIOR
            string textoTopo = status == "WIN" ? "✦ VITÓRIA!" : "✦ EM JOGO";
            canvas.DrawText(textoTopo, 40, 60, new SKPaint { Color = new SKColor(255, 255, 255, 200), TextSize = 24, Typeface = fontBold, IsAntialias = true });

            // MULTIPLICADOR GIGANTE
            canvas.DrawText($"{multiplicador:F2}x", w - 40, 180, new SKPaint { Color = SKColors.White, TextSize = 100, Typeface = fontBold, TextAlign = SKTextAlign.Right, IsAntialias = true });

            // GRÁFICO (CURVA)
            float startX = 60; float startY = h - 60;
            float endX = w / 2.2f; 
            float heightOffset = Math.Min((float)((multiplicador - 1.0) * 45), 130); 
            float endY = startY - heightOffset;

            using (var paintLinha = new SKPaint { Color = SKColors.White, StrokeWidth = 6, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round })
            {
                var path = new SKPath();
                path.MoveTo(startX, startY);
                path.QuadTo(startX + (endX - startX) / 2, startY, endX, endY);
                canvas.DrawPath(path, paintLinha);
            }

            canvas.DrawCircle(endX, endY, 8, new SKPaint { Color = SKColors.White, IsAntialias = true });

            using (var paintBase = new SKPaint { Color = new SKColor(255, 255, 255, 80), StrokeWidth = 2, Style = SKPaintStyle.Stroke, IsAntialias = true })
            {
                paintBase.PathEffect = SKPathEffect.CreateDash(new float[] { 8, 8 }, 0);
                canvas.DrawLine(startX, startY + 20, w - 60, startY + 20, paintBase);
            }

            var paintEixo = new SKPaint { Color = new SKColor(255, 255, 255, 120), TextSize = 16, Typeface = fontBold, IsAntialias = true, TextAlign = SKTextAlign.Center };
            canvas.DrawText("1.0x", startX + 50, startY + 45, paintEixo);
            canvas.DrawText("2.0x", startX + 180, startY + 45, paintEixo);
            canvas.DrawText("3.0x", startX + 310, startY + 45, paintEixo);

            var pathImg = Path.Combine(Path.GetTempPath(), $"crash_{Guid.NewGuid()}.png");
            using (var img = surface.Snapshot()) using (var d = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var s = File.OpenWrite(pathImg)) d.SaveTo(s);
            return pathImg;
        }
    }

    public class CassinoModule
    {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();
        private static readonly Dictionary<ulong, long> CoinflipAtivo = new();
        private static readonly Dictionary<ulong, long> RoletaAtiva = new();
        private static readonly Dictionary<ulong, (List<Card> Player, List<Card> Dealer, List<Card> Deck, long Bet)> BlackjackAtivo = new();
        private static readonly Dictionary<ulong, (double MultiplicadorAtual, bool Retirou, long Aposta)> CrashGamesAtivos = new();

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

            if (content.StartsWith("zcrash"))
            {
                string[] p = content.Split(' ');
                if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Uso:** `zcrash [valor]`"); return; }
                long banco = EconomyHelper.GetBanco(guildId, user.Id);
                string valTxt = p[1].ToLower();
                long aposta = valTxt == "all" ? banco : (valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : long.TryParse(valTxt, out var v) ? v : 0);
                if (aposta < 10 || aposta > banco) return;
                if (CrashGamesAtivos.ContainsKey(user.Id)) return;

                EconomyHelper.RemoverBanco(guildId, user.Id, aposta);
                double crashPoint = Math.Max(1.0, 0.98 / (1.0 - new Random().NextDouble()));
                if (crashPoint > 40.0) crashPoint = 40.0;

                CrashGamesAtivos[user.Id] = (1.0, false, aposta);
                string imgPath = await CasinoImageHelper.GerarImagemCrash(1.0, "JOGANDO");
                
                var eb = new EmbedBuilder().WithAuthor($"Crash | {user.Username}", user.GetAvatarUrl())
                    .WithDescription($@"• 💰 **Aposta:** `{EconomyHelper.FormatarSaldo(aposta)}`").WithColor(Color.Green).WithImageUrl($"attachment://{Path.GetFileName(imgPath)}");

                var cb = new ComponentBuilder()
                    .WithButton($"Ganhou {EconomyHelper.FormatarSaldo(aposta)}", $"crash_sacar_{user.Id}", ButtonStyle.Success, new Emoji("✅"))
                    .WithButton("1.00x", "fake", ButtonStyle.Secondary, disabled: true);

                var jogoMsg = await msg.Channel.SendFileAsync(imgPath, embed: eb.Build(), components: cb.Build());
                File.Delete(imgPath);

                _ = Task.Run(async () =>
                {
                    double currentMult = 1.0;
                    while (true)
                    {
                        await Task.Delay(2000);
                        // CHECAGEM CRUCIAL: Se o cara sacou, o loop morre na hora sem atualizar nada
                        if (!CrashGamesAtivos.ContainsKey(user.Id) || CrashGamesAtivos[user.Id].Retirou) break;

                        currentMult += 0.20 + (currentMult * 0.05);
                        if (currentMult >= crashPoint)
                        {
                            CrashGamesAtivos.Remove(user.Id);
                            string crashImg = await CasinoImageHelper.GerarImagemCrash(crashPoint, "CRASH");
                            var crashEb = new EmbedBuilder().WithAuthor($"❌ CRASHOU!").WithDescription($"• Perdeu: `{EconomyHelper.FormatarSaldo(aposta)}`").WithColor(Color.Red).WithImageUrl($"attachment://crash.png");
                            await jogoMsg.ModifyAsync(x => { x.Embed = crashEb.Build(); x.Attachments = new FileAttachment[] { new FileAttachment(crashImg, "crash.png") }; x.Components = new ComponentBuilder().WithButton("EXPLODIU", "d", ButtonStyle.Danger, disabled: true).Build(); });
                            break;
                        }

                        CrashGamesAtivos[user.Id] = (currentMult, false, aposta);
                        string updImg = await CasinoImageHelper.GerarImagemCrash(currentMult, "JOGANDO");
                        var updEb = new EmbedBuilder().WithAuthor($"Crash | {user.Username}", user.GetAvatarUrl()).WithDescription($@"• 💰 **Aposta:** `{EconomyHelper.FormatarSaldo(aposta)}`").WithColor(Color.Green).WithImageUrl($"attachment://upd.png");
                        var updCb = new ComponentBuilder().WithButton($"Ganhou {EconomyHelper.FormatarSaldo((long)(aposta * currentMult))}", $"crash_sacar_{user.Id}", ButtonStyle.Success, new Emoji("✅")).WithButton($"{currentMult:F2}x", "f", ButtonStyle.Secondary, disabled: true);
                        
                        await jogoMsg.ModifyAsync(x => { x.Embed = updEb.Build(); x.Attachments = new FileAttachment[] { new FileAttachment(updImg, "upd.png") }; x.Components = updCb.Build(); });
                    }
                });
            }
            // (Outros comandos Roleta, BJ, CF mantidos...)
        }

        private async Task HandleButtons(SocketMessageComponent component)
        {
            if (component.Data.CustomId.StartsWith("crash_sacar_"))
            {
                ulong uid = ulong.Parse(component.Data.CustomId.Split('_')[2]);
                if (component.User.Id != uid) return;

                if (CrashGamesAtivos.TryGetValue(uid, out var game) && !game.Retirou)
                {
                    // 1. TRAVA IMEDIATA
                    CrashGamesAtivos[uid] = (game.MultiplicadorAtual, true, game.Aposta);
                    long ganhou = (long)(game.Aposta * game.MultiplicadorAtual);
                    
                    EconomyHelper.AdicionarBanco(component.GuildId ?? 0, uid, ganhou);
                    
                    // 2. REMOVE DA MEMÓRIA PARA O LOOP PARAR DE ATUALIZAR
                    CrashGamesAtivos.Remove(uid);

                    string winImg = await CasinoImageHelper.GerarImagemCrash(game.MultiplicadorAtual, "WIN");
                    var winEb = new EmbedBuilder()
                        .WithAuthor("✅ Retirada bem sucedida!")
                        .WithDescription($@"• 💰 **Aposta:** {EconomyHelper.FormatarSaldo(game.Aposta)}\n• 💸 **Ganhos:** {EconomyHelper.FormatarSaldo(ganhou)}")
                        .WithColor(Color.Green).WithImageUrl($"attachment://win.png");

                    var winCb = new ComponentBuilder()
                        .WithButton($"Ganhou {EconomyHelper.FormatarSaldo(ganhou)}", "w", ButtonStyle.Success, disabled: true, emote: new Emoji("✅"))
                        .WithButton($"{game.MultiplicadorAtual:F2}x", "f", ButtonStyle.Secondary, disabled: true);

                    await component.UpdateAsync(x => { 
                        x.Embed = winEb.Build(); 
                        x.Attachments = new FileAttachment[] { new FileAttachment(winImg, "win.png") }; 
                        x.Components = winCb.Build(); 
                    });
                }
            }
        }
    }
}
