using Discord;
using Discord.WebSocket;
using Discord.Rest;
using Npgsql;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Net.Http;

namespace Botzinho.Economy
{
    // --- 1. LÓGICA DE BANCO DE DADOS E AUXILIARES ---
    public static class EconomyHelper
    {
        public static string GetConnectionString() => Environment.GetEnvironmentVariable("DATABASE_URL") ?? throw new Exception("DATABASE_URL nao configurado!");
        public static readonly HashSet<ulong> IDsAutorizados = new() { 1472642376970404002 };

        public static void InicializarTabelas()
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS economy_users (
                    guild_id TEXT, user_id TEXT, saldo BIGINT DEFAULT 0,
                    banco BIGINT DEFAULT 0, ultimo_daily TIMESTAMP DEFAULT '2000-01-01',
                    ultimo_semanal TIMESTAMP DEFAULT '2000-01-01',
                    ultimo_mensal TIMESTAMP DEFAULT '2000-01-01',
                    PRIMARY KEY (guild_id, user_id));
                
                CREATE TABLE IF NOT EXISTS economy_transactions (
                    id SERIAL PRIMARY KEY, guild_id TEXT, sender_id TEXT, 
                    receiver_id TEXT, amount BIGINT, type TEXT, data TIMESTAMP DEFAULT CURRENT_TIMESTAMP);";
            cmd.ExecuteNonQuery();
        }

        public static long GetSaldo(ulong guildId, ulong userId) {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT saldo FROM economy_users WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var res = cmd.ExecuteScalar(); return res != null ? (long)res : 0;
        }

        public static long GetBanco(ulong guildId, ulong userId) {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT banco FROM economy_users WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var res = cmd.ExecuteScalar(); return res != null ? (long)res : 0;
        }

        public static void AdicionarSaldo(ulong guildId, ulong userId, long valor) {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO economy_users (guild_id, user_id, saldo) VALUES (@gid, @uid, @valor)
                                ON CONFLICT (guild_id, user_id) DO UPDATE SET saldo = economy_users.saldo + @valor";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@valor", valor); cmd.ExecuteNonQuery();
        }

        public static bool RemoverSaldo(ulong guildId, ulong userId, long valor) {
            if (GetSaldo(guildId, userId) < valor) return false;
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE economy_users SET saldo = saldo - @valor WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@valor", valor); return cmd.ExecuteNonQuery() > 0;
        }

        public static bool DepositarTudo(ulong guildId, ulong userId) {
            long s = GetSaldo(guildId, userId); if (s <= 0) return false;
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE economy_users SET banco = banco + saldo, saldo = 0 WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            return cmd.ExecuteNonQuery() > 0;
        }

        public static void RegistrarTransacao(ulong guildId, ulong sender, ulong receiver, long amount, string type) {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO economy_transactions (guild_id, sender_id, receiver_id, amount, type) VALUES (@gid, @sid, @rid, @amount, @type)";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@sid", sender.ToString());
            cmd.Parameters.AddWithValue("@rid", receiver.ToString()); cmd.Parameters.AddWithValue("@amount", amount);
            cmd.Parameters.AddWithValue("@type", type); cmd.ExecuteNonQuery();
        }

        public static string FormatarSaldo(long valor) => valor >= 1000000 ? $"{valor / 1000000.0:F2}M" : valor >= 1000 ? $"{valor / 1000.0:F2}K" : valor.ToString();

        public static List<(ulong UserId, long Total)> GetTop10(ulong guildId) {
            var list = new List<(ulong, long)>();
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT user_id, (saldo + banco) as total FROM economy_users WHERE guild_id = @gid AND (saldo + banco) > 0 ORDER BY total DESC LIMIT 10";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add((ulong.Parse(reader.GetString(0)), reader.GetInt64(1)));
            return list;
        }
    }

    // --- 2. GERAÇÃO DE IMAGENS (DESIGN CLEAN & SLIM) ---
    public static class EconomyImageHelper {
        public static async Task<string> GerarImagemSaldo(SocketUser user, long wallet, long bank) {
            int width = 450; int height = 550;
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;

            // Fundo Sólido Dark (Mais Profissional)
            canvas.Clear(new SKColor(12, 10, 20));

            var cardRect = new SKRect(20, 20, width - 20, height - 20);
            
            // Desenho do card com borda fina roxa
            var borderPaint = new SKPaint { Color = new SKColor(100, 50, 200), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            canvas.DrawRoundRect(cardRect, 30, 30, new SKPaint { Color = new SKColor(20, 18, 35), IsAntialias = true });
            canvas.DrawRoundRect(cardRect, 30, 30, borderPaint);

            using var http = new HttpClient();
            try {
                var bytes = await http.GetByteArrayAsync(user.GetAvatarUrl(ImageFormat.Png, 256) ?? user.GetDefaultAvatarUrl());
                using var bmp = SKBitmap.Decode(bytes);
                var avRect = new SKRect(width/2-70, 50, width/2+70, 190);
                var path = new SKPath(); path.AddOval(avRect);
                canvas.Save(); 
                canvas.ClipPath(path, SKClipOperation.Intersect, true);
                canvas.DrawBitmap(bmp, avRect); 
                canvas.Restore();
                canvas.DrawOval(avRect, new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 3, Color = new SKColor(140, 90, 255), IsAntialias = true });
            } catch { }

            var font = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Normal);
            var boldFont = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);

            // Nome do usuário (Menor e centralizado)
            var namePaint = new SKPaint { Color = SKColors.White, TextSize = 24, Typeface = boldFont, TextAlign = SKTextAlign.Center, IsAntialias = true };
            string name = user.Username.Length > 15 ? user.Username.Substring(0, 13) + ".." : user.Username;
            canvas.DrawText(name, width/2, 235, namePaint);

            // Desenhar as linhas de saldo de forma slim
            float startY = 280;
            DrawSlimPill(canvas, "Carteira", wallet, width, startY, new SKColor(140, 90, 255));
            DrawSlimPill(canvas, "Banco", bank, width, startY + 80, new SKColor(140, 90, 255));
            DrawSlimPill(canvas, "Total", wallet + bank, width, startY + 160, new SKColor(255, 180, 0));

            var p = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot()) using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(p)) data.SaveTo(str);
            return p;
        }

        private static void DrawSlimPill(SKCanvas canvas, string label, long valor, int width, float y, SKColor accent) {
            var rect = new SKRect(50, y, width - 50, y + 60);
            var bgPaint = new SKPaint { Color = new SKColor(35, 32, 55), IsAntialias = true };
            canvas.DrawRoundRect(rect, 15, 15, bgPaint);

            // Indicador lateral fino
            canvas.DrawRoundRect(new SKRect(50, y + 10, 54, y + 50), 2, 2, new SKPaint { Color = accent, IsAntialias = true });

            var font = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Normal);
            var boldFont = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);

            // Texto Label
            canvas.DrawText(label.ToUpper(), 70, y + 22, new SKPaint { Color = new SKColor(180, 180, 200), TextSize = 12, Typeface = font, IsAntialias = true });
            // Valor
            canvas.DrawText(EconomyHelper.FormatarSaldo(valor) + " cpoints", 70, y + 48, new SKPaint { Color = SKColors.White, TextSize = 18, Typeface = boldFont, IsAntialias = true });
        }

        public static async Task<string> GerarImagemRank(SocketGuild guild, List<(ulong UserId, long Total)> top) {
            int w = 850; int h = 680;
            using var surface = SKSurface.Create(new SKImageInfo(w, h));
            var canvas = surface.Canvas; canvas.Clear(new SKColor(20, 10, 30));
            var bold = SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold);
            canvas.DrawText("Top Coins do Servidor", 40, 80, new SKPaint { Color = SKColors.White, TextSize = 45, Typeface = bold, IsAntialias = true });
            using var http = new HttpClient();
            for (int i = 0; i < top.Count; i++) {
                IUser m = guild.GetUser(top[i].UserId) ?? await ((IGuild)guild).GetUserAsync(top[i].UserId);
                int x = 40 + (i % 2 * 405); int y = 120 + (i / 2 * 105);
                canvas.DrawRoundRect(new SKRect(x, y, x + 380, y + 90), 45, 45, new SKPaint { Color = (i == 0) ? new SKColor(255, 215, 0) : new SKColor(80, 0, 80), IsAntialias = true });
                try {
                    var b = await http.GetByteArrayAsync(m?.GetAvatarUrl() ?? m?.GetDefaultAvatarUrl());
                    using var bmp = SKBitmap.Decode(b);
                    var r = new SKRect(x+15, y+15, x+75, y+75); var p = new SKPath(); p.AddOval(r);
                    canvas.Save(); canvas.ClipPath(p, SKClipOperation.Intersect, true); canvas.DrawBitmap(bmp, r); canvas.Restore();
                } catch { }
                canvas.DrawText(m?.Username ?? "User", x + 90, y + 55, new SKPaint { Color = SKColors.White, TextSize = 22, IsAntialias = true });
                canvas.DrawText(EconomyHelper.FormatarSaldo(top[i].Total), x + 360, y + 55, new SKPaint { Color = SKColors.White, TextSize = 20, TextAlign = SKTextAlign.Right, IsAntialias = true });
            }
            var path = Path.Combine(Path.GetTempPath(), $"rank_{guild.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot()) using (var d = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var s = File.OpenWrite(path)) d.SaveTo(s);
            return path;
        }
    }

    // --- 3. ECONOMY HANDLER (SEM ALTERAÇÃO NA LÓGICA) ---
    public class EconomyHandler {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();
        private static readonly Dictionary<ulong, long> ApostasAtivas = new();
        private static readonly Dictionary<ulong, (List<int> Player, List<int> Dealer, long Bet)> BlackjackAtivo = new();
        private const string IMG_MOEDA = "https://cdn.discordapp.net/attachments/1110495236716773447/1163499638461042831/coin_1540515.png";

        public EconomyHandler(DiscordSocketClient client) { 
            _client = client; _client.MessageReceived += HandleMessage; _client.ButtonExecuted += HandleButtons; 
        }

        private Task HandleMessage(SocketMessage msg) {
            _ = Task.Run(async () => {
                try {
                    if (msg.Author.IsBot || msg is not SocketUserMessage) return;
                    var user = msg.Author as SocketGuildUser; var content = msg.Content.ToLower().Trim(); var guildId = user.Guild.Id;
                    string[] cmds = { "zsaldo", "zdaily", "zrank", "zpay", "zdep", "zaddsaldo", "zcf", "zcoinflip", "zbj", "zblackjack" };
                    if (!cmds.Any(c => content.StartsWith(c))) return;
                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 2) return;
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    if (content == "zdaily") {
                        long g = new Random().Next(167000, 180001); EconomyHelper.AdicionarSaldo(guildId, user.Id, g);
                        await msg.Channel.SendMessageAsync($"✅ {user.Mention}, `{EconomyHelper.FormatarSaldo(g)}` cpoints no **Diário**!");
                    }
                    else if (content == "zdep all") {
                        if (EconomyHelper.DepositarTudo(guildId, user.Id)) await msg.Channel.SendMessageAsync("🏦 Carteira guardada no banco!");
                    }
                    else if (content == "zsaldo") {
                        var p = await EconomyImageHelper.GerarImagemSaldo(user, EconomyHelper.GetSaldo(guildId, user.Id), EconomyHelper.GetBanco(guildId, user.Id));
                        await msg.Channel.SendFileAsync(p, ""); File.Delete(p);
                    }
                    else if (content == "zrank") {
                        var p = await EconomyImageHelper.GerarImagemRank(user.Guild, EconomyHelper.GetTop10(guildId));
                        await msg.Channel.SendFileAsync(p, "🏆 **Top Ricos**"); File.Delete(p);
                    }
                    else if (content.StartsWith("zaddsaldo") && EconomyHelper.IDsAutorizados.Contains(user.Id)) {
                        var alvo = msg.MentionedUsers.FirstOrDefault();
                        if (alvo != null) {
                            string vTxt = content.Split(' ').Last().ToLower();
                            long v = vTxt.EndsWith("k") ? (long)(double.Parse(vTxt.Replace("k",""))*1000) : vTxt.EndsWith("m") ? (long)(double.Parse(vTxt.Replace("m",""))*1000000) : long.Parse(vTxt);
                            EconomyHelper.AdicionarSaldo(guildId, alvo.Id, v);
                            await msg.Channel.SendMessageAsync($"<a:lealdade:1493009439522033735> **Sucesso!** Foram adicionados `{EconomyHelper.FormatarSaldo(v)}` cpoints para <:pessoa:1493010183352483840> {alvo.Mention}.");
                        }
                    }
                    else if (content.StartsWith("zpay")) {
                        var alvo = msg.MentionedUsers.FirstOrDefault();
                        if (alvo != null && alvo.Id != user.Id && !alvo.IsBot) {
                            string vTxt = content.Split(' ').Last().ToLower();
                            long v = vTxt.EndsWith("k") ? (long)(double.Parse(vTxt.Replace("k",""))*1000) : vTxt.EndsWith("m") ? (long)(double.Parse(vTxt.Replace("m",""))*1000000) : long.Parse(vTxt);
                            if (EconomyHelper.RemoverSaldo(guildId, user.Id, v)) { 
                                EconomyHelper.AdicionarSaldo(guildId, alvo.Id, v); 
                                EconomyHelper.RegistrarTransacao(guildId, user.Id, alvo.Id, v, "TRANSFERENCIA");
                                await msg.Channel.SendMessageAsync($"✅ {user.Mention} enviou `{EconomyHelper.FormatarSaldo(v)}` para {alvo.Mention}."); 
                            }
                        }
                    }
                    else if (content.StartsWith("zcf") || content.StartsWith("zcoinflip")) {
                        string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries); 
                        if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zcoinflip (valor)`"); return; }
                        long s = EconomyHelper.GetSaldo(guildId, user.Id);
                        string vT = p[1].ToLower();
                        long val = vT == "all" ? s : (vT.EndsWith("k") ? (long)(double.Parse(vT.Replace("k",""))*1000) : vT.EndsWith("m") ? (long)(double.Parse(vT.Replace("m",""))*1000000) : long.TryParse(vT, out var res) ? res : 0);
                        if (val <= 0 || s < val) { await msg.Channel.SendMessageAsync($@"<:negativo:1492950137587241114> Você não possui **{EconomyHelper.FormatarSaldo(val)} coins** no banco para apostar."); return; }
                        if (ApostasAtivas.ContainsKey(user.Id)) return;
                        ApostasAtivas[user.Id] = val; EconomyHelper.RemoverSaldo(guildId, user.Id, val);
                        var eb = new EmbedBuilder().WithAuthor("Cara ou Coroa", IMG_MOEDA).WithDescription($"🪙 | **Valor em aposta:** `{EconomyHelper.FormatarSaldo(val)}`").WithFooter($"Apostador: {user.Username}").WithColor(new Color(114, 137, 218));
                        var cb = new ComponentBuilder().WithButton("Cara", $"cf_cara_{user.Id}", ButtonStyle.Secondary, new Emoji("🙂")).WithButton("Coroa", $"cf_coroa_{user.Id}", ButtonStyle.Secondary, new Emoji("👑")).WithButton(null, $"cf_cancel_{user.Id}", ButtonStyle.Danger, new Emoji("❌"));
                        await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
                    }
                    else if (content.StartsWith("zbj") || content.StartsWith("zblackjack")) {
                        string[] p = content.Split(' '); if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Uso:** `zbj [valor]`"); return; }
                        long s = EconomyHelper.GetSaldo(guildId, user.Id);
                        long val = p[1] == "all" ? s : (p[1].EndsWith("k") ? (long)(double.Parse(p[1].Replace("k",""))*1000) : p[1].EndsWith("m") ? (long)(double.Parse(p[1].Replace("m",""))*1000000) : long.Parse(p[1]));
                        if (val <= 0 || s < val || BlackjackAtivo.ContainsKey(user.Id)) return;
                        EconomyHelper.RemoverSaldo(guildId, user.Id, val);
                        var deck = new List<int> { 2,3,4,5,6,7,8,9,10,10,10,10,11 };
                        var r = new Random();
                        var pHand = new List<int> { deck[r.Next(deck.Count)], deck[r.Next(deck.Count)] };
                        var dHand = new List<int> { deck[r.Next(deck.Count)] };
                        BlackjackAtivo[user.Id] = (pHand, dHand, val);
                        var eb = new EmbedBuilder().WithAuthor("Blackjack 🃏").WithDescription($"**Suas:** {string.Join(", ", pHand)} (Total: {pHand.Sum()})\n**Dealer:** {dHand[0]} e [?]\n💰 **Aposta:** `{EconomyHelper.FormatarSaldo(val)}`").WithColor(Color.Blue);
                        var cb = new ComponentBuilder().WithButton("Comprar", $"bj_hit_{user.Id}").WithButton("Parar", $"bj_stand_{user.Id}", ButtonStyle.Secondary);
                        await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
                    }
                } catch { }
            }); return Task.CompletedTask;
        }

        private async Task HandleButtons(SocketMessageComponent comp) {
            var parts = comp.Data.CustomId.Split('_');
            if (parts[0] == "cf") {
                var uid = ulong.Parse(parts[2]); if (comp.User.Id != uid || !ApostasAtivas.TryGetValue(uid, out long val)) return;
                var user = (SocketGuildUser)comp.User; ApostasAtivas.Remove(uid);
                if (parts[1] == "cancel") { EconomyHelper.AdicionarSaldo(user.Guild.Id, uid, val); await comp.UpdateAsync(x => { x.Content = $"✅ {user.Mention} desistiu."; x.Embed = null; x.Components = null; }); return; }
                string res = new Random().Next(0, 2) == 0 ? "cara" : "coroa"; bool win = parts[1] == res;
                var eb = new EmbedBuilder().WithAuthor("Cara ou Coroa", IMG_MOEDA).WithThumbnailUrl(IMG_MOEDA);
                if (win) { EconomyHelper.AdicionarSaldo(user.Guild.Id, uid, val * 2); eb.WithColor(Color.Green).WithDescription($"Ganhou! Deu **{res}**.\n💰 +{EconomyHelper.FormatarSaldo(val * 2)}"); }
                else { eb.WithColor(Color.Red).WithDescription($"Perdeu! Deu **{res}**.\n❌ -{EconomyHelper.FormatarSaldo(val)}"); }
                await comp.UpdateAsync(x => { x.Embed = eb.Build(); x.Components = null; x.Content = user.Mention; });
            }
            else if (parts[0] == "bj") {
                var action = parts[1]; var uid = ulong.Parse(parts[2]);
                if (comp.User.Id != uid || !BlackjackAtivo.TryGetValue(uid, out var game)) return;
                var user = (SocketGuildUser)comp.User; var deck = new List<int> { 2,3,4,5,6,7,8,9,10,10,10,10,11 };
                var r = new Random();
                if (action == "hit") {
                    game.Player.Add(deck[r.Next(deck.Count)]);
                    if (game.Player.Sum() > 21) {
                        BlackjackAtivo.Remove(uid);
                        await comp.UpdateAsync(x => { x.Content = $"💥 **Estourou!** Total: {game.Player.Sum()}. Perdeu `{EconomyHelper.FormatarSaldo(game.Bet)}`."; x.Embed = null; x.Components = null; });
                        return;
                    }
                } else {
                    BlackjackAtivo.Remove(uid);
                    while (game.Dealer.Sum() < 17) game.Dealer.Add(deck[r.Next(deck.Count)]);
                    int pS = game.Player.Sum(); int dS = game.Dealer.Sum();
                    string resText = ""; Color col;
                    if (dS > 21 || pS > dS) { resText = $"🏆 **Ganhou!** Dealer: {dS}. Prêmio: `{EconomyHelper.FormatarSaldo(game.Bet * 2)}`"; EconomyHelper.AdicionarSaldo(user.Guild.Id, uid, game.Bet * 2); col = Color.Green; }
                    else if (pS == dS) { resText = "⚖️ **Empate!** Valor devolvido."; EconomyHelper.AdicionarSaldo(user.Guild.Id, uid, game.Bet); col = Color.LightGrey; }
                    else { resText = $"❌ **Perdeu!** Dealer: {dS}."; col = Color.Red; }
                    await comp.UpdateAsync(x => { x.Embed = new EmbedBuilder().WithTitle("Resultado Blackjack").WithDescription($"{resText}\nSuas: {pS} | Dealer: {dS}").WithColor(col).Build(); x.Components = null; });
                    return;
                }
                await comp.UpdateAsync(x => x.Embed = new EmbedBuilder().WithAuthor("Blackjack 🃏").WithDescription($"**Suas:** {string.Join(", ", game.Player)} (Total: {game.Player.Sum()})\n**Dealer:** {game.Dealer[0]} e [?]").WithColor(Color.Blue).Build());
            }
        }
    }
}
