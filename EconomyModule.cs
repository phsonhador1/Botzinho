using Discord;
using Discord.WebSocket;
using Discord.Rest; // Necessário para a busca profunda
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Net.Http;

namespace Botzinho.Economy
{
    public static class EconomyHelper
    {
        public static string GetConnectionString()
        {
            return Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? throw new Exception("DATABASE_URL nao configurado!");
        }

        public static void InicializarTabelas()
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS economy_users (
                    guild_id TEXT,
                    user_id TEXT,
                    saldo BIGINT DEFAULT 0,
                    ultimo_daily TIMESTAMP DEFAULT '2000-01-01',
                    PRIMARY KEY (guild_id, user_id)
                );";
            cmd.ExecuteNonQuery();
        }

        public static long GetSaldo(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT saldo FROM economy_users WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var result = cmd.ExecuteScalar();
            return result != null ? (long)result : 0;
        }

        public static void SetSaldo(ulong guildId, ulong userId, long novoSaldo)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO economy_users (guild_id, user_id, saldo)
                VALUES (@gid, @uid, @saldo)
                ON CONFLICT (guild_id, user_id)
                DO UPDATE SET saldo = @saldo";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@saldo", novoSaldo);
            cmd.ExecuteNonQuery();
        }

        public static void AdicionarSaldo(ulong guildId, ulong userId, long valor)
        {
            var saldoAtual = GetSaldo(guildId, userId);
            SetSaldo(guildId, userId, saldoAtual + valor);
        }

        public static bool RemoverSaldo(ulong guildId, ulong userId, long valor)
        {
            var saldoAtual = GetSaldo(guildId, userId);
            if (saldoAtual < valor) return false;
            SetSaldo(guildId, userId, saldoAtual - valor);
            return true;
        }

        public static DateTime GetUltimoDaily(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ultimo_daily FROM economy_users WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value) return DateTime.MinValue;
            return (DateTime)result;
        }

        public static void SetUltimoDaily(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO economy_users (guild_id, user_id, ultimo_daily)
                VALUES (@gid, @uid, @data)
                ON CONFLICT (guild_id, user_id)
                DO UPDATE SET ultimo_daily = @data";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@data", DateTime.UtcNow);
            cmd.ExecuteNonQuery();
        }

        public static string FormatarSaldo(long valor)
        {
            if (valor >= 1000000000) return $"{valor / 1000000000.0:F2}B";
            if (valor >= 1000000) return $"{valor / 1000000.0:F2}M";
            if (valor >= 1000) return $"{valor / 1000.0:F2}K";
            return valor.ToString();
        }

        public static List<(ulong UserId, long Saldo)> GetTop10(ulong guildId)
        {
            var list = new List<(ulong, long)>();
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT user_id, saldo FROM economy_users WHERE guild_id = @gid AND saldo > 0 ORDER BY saldo DESC LIMIT 10";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add((ulong.Parse(reader.GetString(0)), reader.GetInt64(1)));
            }
            return list;
        }

        public static (int Rank, long Saldo) GetUserRankInfo(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT rank_pos, saldo FROM (
                    SELECT user_id, saldo, RANK() OVER(ORDER BY saldo DESC) as rank_pos
                    FROM economy_users
                    WHERE guild_id = @gid AND saldo > 0
                ) ranked WHERE user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (reader.GetInt32(0), reader.GetInt64(1));
            }
            return (0, 0);
        }
    }

    public static class EconomyImageHelper
    {
        public static async Task<string> GerarImagemSaldo(SocketGuildUser user, long saldo)
        {
            int width = 400; int height = 320;
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height));
            var canvas = surface.Canvas;

            var fontBold = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans", SkiaSharp.SKFontStyle.Bold) ?? SkiaSharp.SKTypeface.Default;
            var fontNormal = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans") ?? SkiaSharp.SKTypeface.Default;

            var bgPaint = new SkiaSharp.SKPaint();
            bgPaint.Shader = SkiaSharp.SKShader.CreateLinearGradient(new SkiaSharp.SKPoint(0, 0), new SkiaSharp.SKPoint(width, height),
                new[] { new SkiaSharp.SKColor(25, 25, 35), new SkiaSharp.SKColor(35, 30, 55) }, null, SkiaSharp.SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SkiaSharp.SKRect(0, 0, width, height), 20, 20, bgPaint);

            var borderPaint = new SkiaSharp.SKPaint { Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            borderPaint.Shader = SkiaSharp.SKShader.CreateLinearGradient(new SkiaSharp.SKPoint(0, 0), new SkiaSharp.SKPoint(width, height),
                new[] { new SkiaSharp.SKColor(80, 0, 80), new SkiaSharp.SKColor(50, 0, 50) }, null, SkiaSharp.SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SkiaSharp.SKRect(2, 2, width - 2, height - 2), 20, 20, borderPaint);

            try
            {
                var avatarUrl = user.GetAvatarUrl(ImageFormat.Png, 128) ?? user.GetDefaultAvatarUrl();
                using var httpClient = new HttpClient();
                var avatarBytes = await httpClient.GetByteArrayAsync(avatarUrl);
                using var avatarBitmap = SkiaSharp.SKBitmap.Decode(avatarBytes);
                if (avatarBitmap != null)
                {
                    int avatarSize = 80; int avatarX = (width - avatarSize) / 2; int avatarY = 20;
                    var avatarRect = new SkiaSharp.SKRect(avatarX, avatarY, avatarX + avatarSize, avatarY + avatarSize);
                    var clipPath = new SkiaSharp.SKPath(); clipPath.AddOval(avatarRect);
                    canvas.Save(); canvas.ClipPath(clipPath); canvas.DrawBitmap(avatarBitmap, avatarRect); canvas.Restore();
                    var glowPaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(80, 0, 80, 80), Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 6, IsAntialias = true, MaskFilter = SkiaSharp.SKMaskFilter.CreateBlur(SkiaSharp.SKBlurStyle.Normal, 3) };
                    canvas.DrawOval(avatarX + avatarSize / 2, avatarY + avatarSize / 2, avatarSize / 2, avatarSize / 2, glowPaint);
                }
            }
            catch { }

            var namePaint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, TextSize = 20, IsAntialias = true, Typeface = fontBold, TextAlign = SkiaSharp.SKTextAlign.Center };
            canvas.DrawText(user.DisplayName, width / 2, 125, namePaint);

            string saldoFormatado = EconomyHelper.FormatarSaldo(saldo);
            DrawItem(canvas, 40, 160, "Carteira", saldoFormatado + " cpoints", fontBold, fontNormal, new SkiaSharp.SKColor(80, 200, 120), width);
            DrawItem(canvas, 40, 210, "Banco", "0 cpoints", fontBold, fontNormal, new SkiaSharp.SKColor(100, 140, 230), width);
            DrawItem(canvas, 40, 260, "Total", saldoFormatado + " cpoints", fontBold, fontNormal, new SkiaSharp.SKColor(230, 180, 60), width);

            var path = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
            using var image = surface.Snapshot(); using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(path); data.SaveTo(stream);
            return path;
        }

        public static async Task<string> GerarImagemDaily(SocketGuildUser user, long recompensa, long saldoAtual)
        {
            int width = 450; int height = 200;
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height));
            var canvas = surface.Canvas;
            var fontBold = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans", SkiaSharp.SKFontStyle.Bold) ?? SkiaSharp.SKTypeface.Default;
            var fontNormal = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans") ?? SkiaSharp.SKTypeface.Default;

            var bgPaint = new SkiaSharp.SKPaint();
            bgPaint.Shader = SkiaSharp.SKShader.CreateLinearGradient(new SkiaSharp.SKPoint(0, 0), new SkiaSharp.SKPoint(width, height),
                new[] { new SkiaSharp.SKColor(25, 20, 45), new SkiaSharp.SKColor(40, 25, 60) }, null, SkiaSharp.SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SkiaSharp.SKRect(0, 0, width, height), 16, 16, bgPaint);

            var titlePaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(80, 0, 80), TextSize = 20, IsAntialias = true, Typeface = fontBold };
            canvas.DrawText("Daily", 75, 32, titlePaint);

            var recompValor = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(100, 220, 100), TextSize = 28, IsAntialias = true, Typeface = fontBold };
            canvas.DrawText($"+{EconomyHelper.FormatarSaldo(recompensa)} cpoints", 30, 128, recompValor);

            var path = Path.Combine(Path.GetTempPath(), $"daily_{user.Id}_{DateTime.Now.Ticks}.png");
            using var image = surface.Snapshot(); using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(path); data.SaveTo(stream);
            return path;
        }

        public static async Task<string> GerarImagemRank(SocketGuild guild, List<(ulong UserId, long Saldo)> topUsers)
        {
            int width = 850; int height = 680;
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height));
            var canvas = surface.Canvas;
            var fontBold = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans", SkiaSharp.SKFontStyle.Bold) ?? SkiaSharp.SKTypeface.Default;
            var fontNormal = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans") ?? SkiaSharp.SKTypeface.Default;

            var bgPaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(20, 10, 30), IsAntialias = true };
            canvas.DrawRect(new SkiaSharp.SKRect(0, 0, width, height), bgPaint);

            var avatares = new Dictionary<ulong, byte[]>();
            var nomes = new Dictionary<ulong, string>();
            using var httpClient = new HttpClient();

            // BUSCA PROFUNDA: Resolve usuários offline ou fora do cache
            foreach (var u in topUsers)
            {
                try
                {
                    IUser user = guild.GetUser(u.UserId);
                    if (user == null) user = await ((IGuild)guild).GetUserAsync(u.UserId); // API REST

                    nomes[u.UserId] = user?.Username ?? "Desconhecido";
                    var url = user?.GetAvatarUrl(ImageFormat.Png, 128) ?? user?.GetDefaultAvatarUrl();
                    if (url != null) avatares[u.UserId] = await httpClient.GetByteArrayAsync(url);
                }
                catch { nomes[u.UserId] = "Desconhecido"; }
            }

            int startX = 40; int startY = 120; int cardWidth = 370; int cardHeight = 85;
            for (int i = 0; i < topUsers.Count; i++)
            {
                int col = i % 2; int row = i / 2;
                int x = startX + (col * 400); int y = startY + (row * 105);
                var userData = topUsers[i];
                var rank = i + 1;

                SkiaSharp.SKColor bgColor = rank switch { 1 => new SkiaSharp.SKColor(255, 180, 0), 2 => new SkiaSharp.SKColor(220, 220, 230), 3 => new SkiaSharp.SKColor(255, 120, 0), _ => new SkiaSharp.SKColor(80, 0, 80) };
                var cardPaint = new SkiaSharp.SKPaint { Color = bgColor, IsAntialias = true };
                canvas.DrawRoundRect(new SkiaSharp.SKRect(x, y, x + cardWidth, y + cardHeight), 42, 42, cardPaint);

                if (avatares.TryGetValue(userData.UserId, out var bytes))
                {
                    using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);
                    var rect = new SkiaSharp.SKRect(x + 10, y + 10, x + 75, y + 75);
                    canvas.DrawBitmap(bitmap, rect);
                }

                var textPaint = new SkiaSharp.SKPaint { Color = rank <= 3 ? SkiaSharp.SKColors.Black : SkiaSharp.SKColors.White, TextSize = 22, Typeface = fontBold, IsAntialias = true };
                canvas.DrawText(nomes[userData.UserId], x + 90, y + 50, textPaint);
                canvas.DrawText(EconomyHelper.FormatarSaldo(userData.Saldo), x + cardWidth - 25, y + 50, new SkiaSharp.SKPaint { Color = textPaint.Color, TextSize = 22, TextAlign = SkiaSharp.SKTextAlign.Right, IsAntialias = true });
            }

            var path = Path.Combine(Path.GetTempPath(), $"rank_{guild.Id}_{DateTime.Now.Ticks}.png");
            using var image = surface.Snapshot(); using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(path); data.SaveTo(stream);
            return path;
        }

        private static void DrawItem(SkiaSharp.SKCanvas canvas, float x, float y, string label, string valor, SkiaSharp.SKTypeface fontBold, SkiaSharp.SKTypeface fontNormal, SkiaSharp.SKColor accentColor, int width)
        {
            var itemBg = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(38, 38, 48), IsAntialias = true };
            canvas.DrawRoundRect(new SkiaSharp.SKRect(x, y, width - 40, y + 40), 10, 10, itemBg);
            canvas.DrawText(label, x + 45, y + 17, new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, TextSize = 15, IsAntialias = true, Typeface = fontBold });
            canvas.DrawText(valor, x + 45, y + 34, new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(170, 170, 180), TextSize = 13, IsAntialias = true, Typeface = fontNormal });
        }
    }

    public class EconomyHandler
    {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();

        public EconomyHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
        }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () => {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage userMsg) return;
                    var user = msg.Author as SocketGuildUser; if (user == null) return;
                    var content = msg.Content.ToLower().Trim();
                    var guildId = user.Guild.Id;

                    string[] comandos = { "zhelp", "zsaldo", "zdaily", "zpay", "zrank", "ztop coins" };
                    if (!comandos.Any(c => content == c || content.StartsWith(c + " "))) return;

                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 5)
                    {
                        var aviso = await msg.Channel.SendMessageAsync($"⏳ Calma lá, {user.Mention}! Aguarde para usar outro comando.");
                        await Task.Delay(3000); await aviso.DeleteAsync(); return;
                    }
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    if (content == "zsaldo" || content.StartsWith("zsaldo "))
                    {
                        var alvo = msg.MentionedUsers.Count > 0 ? user.Guild.GetUser(msg.MentionedUsers.First().Id) ?? user : user;
                        var saldo = EconomyHelper.GetSaldo(guildId, alvo.Id);
                        var img = await EconomyImageHelper.GerarImagemSaldo(alvo, saldo);
                        await msg.Channel.SendFileAsync(img, ""); File.Delete(img);
                    }
                    else if (content == "zrank" || content == "ztop coins")
                    {
                        var top10 = EconomyHelper.GetTop10(guildId);
                        if (top10.Count == 0) { await msg.Channel.SendMessageAsync("Ranking vazio."); return; }
                        var loading = await msg.Channel.SendMessageAsync("<a:carregandoportal:1492944498605686844> Gerando ranking...");
                        var img = await EconomyImageHelper.GerarImagemRank(user.Guild, top10);
                        await msg.Channel.SendFileAsync(img, "🏆 **Usuários mais ricos!**");
                        await loading.DeleteAsync(); File.Delete(img);
                    }
                    else if (content == "zdaily")
                    {
                        var lastDaily = EconomyHelper.GetUltimoDaily(guildId, user.Id);
                        if ((DateTime.UtcNow - lastDaily).TotalHours < 24)
                        {
                            await msg.Channel.SendMessageAsync("Daily já coletado hoje!"); return;
                        }
                        long win = new Random().Next(500, 2001);
                        EconomyHelper.AdicionarSaldo(guildId, user.Id, win);
                        EconomyHelper.SetUltimoDaily(guildId, user.Id);
                        var img = await EconomyImageHelper.GerarImagemDaily(user, win, EconomyHelper.GetSaldo(guildId, user.Id));
                        await msg.Channel.SendFileAsync(img, ""); File.Delete(img);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[Eco] {ex.Message}"); }
            });
            return Task.CompletedTask;
        }
    }
}
