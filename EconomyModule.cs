using Discord;
using Discord.WebSocket;
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
                );
            ";
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
            int width = 400;
            int height = 320;

            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height));
            var canvas = surface.Canvas;

            var fontBold = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans", SkiaSharp.SKFontStyle.Bold)
                ?? SkiaSharp.SKTypeface.Default;
            var fontNormal = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans")
                ?? SkiaSharp.SKTypeface.Default;

            var bgPaint = new SkiaSharp.SKPaint();
            bgPaint.Shader = SkiaSharp.SKShader.CreateLinearGradient(
                new SkiaSharp.SKPoint(0, 0),
                new SkiaSharp.SKPoint(width, height),
                new[] { new SkiaSharp.SKColor(25, 25, 35), new SkiaSharp.SKColor(35, 30, 55) },
                null,
                SkiaSharp.SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SkiaSharp.SKRect(0, 0, width, height), 20, 20, bgPaint);

            var borderPaint = new SkiaSharp.SKPaint
            {
                Style = SkiaSharp.SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true
            };
            borderPaint.Shader = SkiaSharp.SKShader.CreateLinearGradient(
                new SkiaSharp.SKPoint(0, 0),
                new SkiaSharp.SKPoint(width, height),
                new[] { new SkiaSharp.SKColor(80, 0, 80), new SkiaSharp.SKColor(50, 0, 50) }, // Nova cor roxa escuro
                null,
                SkiaSharp.SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SkiaSharp.SKRect(2, 2, width - 2, height - 2), 20, 20, borderPaint);

            try
            {
                var avatarUrl = user.GetAvatarUrl(ImageFormat.Png, 128) ?? user.GetDefaultAvatarUrl();
                using var httpClient = new System.Net.Http.HttpClient();
                var avatarBytes = await httpClient.GetByteArrayAsync(avatarUrl);
                using var avatarBitmap = SkiaSharp.SKBitmap.Decode(avatarBytes);

                if (avatarBitmap != null)
                {
                    int avatarSize = 80;
                    int avatarX = (width - avatarSize) / 2;
                    int avatarY = 20;

                    var avatarRect = new SkiaSharp.SKRect(avatarX, avatarY, avatarX + avatarSize, avatarY + avatarSize);
                    var clipPath = new SkiaSharp.SKPath();
                    clipPath.AddOval(avatarRect);

                    canvas.Save();
                    canvas.ClipPath(clipPath);
                    canvas.DrawBitmap(avatarBitmap, avatarRect);
                    canvas.Restore();

                    var glowPaint = new SkiaSharp.SKPaint
                    {
                        Color = new SkiaSharp.SKColor(80, 0, 80, 80), // Glow roxo escuro
                        Style = SkiaSharp.SKPaintStyle.Stroke,
                        StrokeWidth = 6,
                        IsAntialias = true,
                        MaskFilter = SkiaSharp.SKMaskFilter.CreateBlur(SkiaSharp.SKBlurStyle.Normal, 3)
                    };
                    canvas.DrawOval(avatarX + avatarSize / 2, avatarY + avatarSize / 2, avatarSize / 2, avatarSize / 2, glowPaint);

                    var avatarBorderPaint = new SkiaSharp.SKPaint
                    {
                        Color = new SkiaSharp.SKColor(80, 0, 80), // Borda do avatar
                        Style = SkiaSharp.SKPaintStyle.Stroke,
                        StrokeWidth = 3,
                        IsAntialias = true
                    };
                    canvas.DrawOval(avatarX + avatarSize / 2, avatarY + avatarSize / 2, avatarSize / 2, avatarSize / 2, avatarBorderPaint);
                }
            }
            catch { }

            var namePaint = new SkiaSharp.SKPaint
            {
                Color = SkiaSharp.SKColors.White,
                TextSize = 20,
                IsAntialias = true,
                Typeface = fontBold,
                TextAlign = SkiaSharp.SKTextAlign.Center
            };
            canvas.DrawText(user.DisplayName, width / 2, 125, namePaint);

            var sepPaint = new SkiaSharp.SKPaint { StrokeWidth = 1, IsAntialias = true };
            sepPaint.Shader = SkiaSharp.SKShader.CreateLinearGradient(
                new SkiaSharp.SKPoint(30, 0),
                new SkiaSharp.SKPoint(width - 30, 0),
                new[] { new SkiaSharp.SKColor(60, 60, 70, 0), new SkiaSharp.SKColor(80, 0, 80), new SkiaSharp.SKColor(60, 60, 70, 0) }, // Linha divisória
                null,
                SkiaSharp.SKShaderTileMode.Clamp);
            canvas.DrawLine(30, 140, width - 30, 140, sepPaint);

            string saldoFormatado = EconomyHelper.FormatarSaldo(saldo);

            DrawItem(canvas, 40, 160, "Carteira", saldoFormatado + " cpoints", fontBold, fontNormal,
                new SkiaSharp.SKColor(80, 200, 120), new SkiaSharp.SKColor(40, 80, 50), width, DrawWalletIcon);

            DrawItem(canvas, 40, 210, "Banco", "0 cpoints", fontBold, fontNormal,
                new SkiaSharp.SKColor(100, 140, 230), new SkiaSharp.SKColor(40, 50, 80), width, DrawBankIcon);

            DrawItem(canvas, 40, 260, "Total", saldoFormatado + " cpoints", fontBold, fontNormal,
                new SkiaSharp.SKColor(230, 180, 60), new SkiaSharp.SKColor(80, 65, 30), width, DrawCoinIcon);

            var path = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
            using var image = surface.Snapshot();
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);

            return path;
        }

        public static async Task<string> GerarImagemDaily(SocketGuildUser user, long recompensa, long saldoAtual)
        {
            int width = 450;
            int height = 200;

            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height));
            var canvas = surface.Canvas;

            var fontBold = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans", SkiaSharp.SKFontStyle.Bold)
                ?? SkiaSharp.SKTypeface.Default;
            var fontNormal = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans")
                ?? SkiaSharp.SKTypeface.Default;

            var bgPaint = new SkiaSharp.SKPaint();
            bgPaint.Shader = SkiaSharp.SKShader.CreateLinearGradient(
                new SkiaSharp.SKPoint(0, 0),
                new SkiaSharp.SKPoint(width, height),
                new[] { new SkiaSharp.SKColor(25, 20, 45), new SkiaSharp.SKColor(40, 25, 60) },
                null,
                SkiaSharp.SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SkiaSharp.SKRect(0, 0, width, height), 16, 16, bgPaint);

            var borderPaint = new SkiaSharp.SKPaint
            {
                Style = SkiaSharp.SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true
            };
            borderPaint.Shader = SkiaSharp.SKShader.CreateLinearGradient(
                new SkiaSharp.SKPoint(0, 0),
                new SkiaSharp.SKPoint(width, 0),
                new[] { new SkiaSharp.SKColor(80, 0, 80), new SkiaSharp.SKColor(50, 0, 50) }, // Nova cor
                null,
                SkiaSharp.SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SkiaSharp.SKRect(2, 2, width - 2, height - 2), 16, 16, borderPaint);

            try
            {
                var avatarUrl = user.GetAvatarUrl(ImageFormat.Png, 64) ?? user.GetDefaultAvatarUrl();
                using var httpClient = new System.Net.Http.HttpClient();
                var avatarBytes = await httpClient.GetByteArrayAsync(avatarUrl);
                using var avatarBitmap = SkiaSharp.SKBitmap.Decode(avatarBytes);

                if (avatarBitmap != null)
                {
                    int avatarSize = 45;
                    int avatarX = 20;
                    int avatarY = 15;

                    var avatarRect = new SkiaSharp.SKRect(avatarX, avatarY, avatarX + avatarSize, avatarY + avatarSize);
                    var clipPath = new SkiaSharp.SKPath();
                    clipPath.AddOval(avatarRect);

                    canvas.Save();
                    canvas.ClipPath(clipPath);
                    canvas.DrawBitmap(avatarBitmap, avatarRect);
                    canvas.Restore();

                    var avatarBorderPaint = new SkiaSharp.SKPaint
                    {
                        Color = new SkiaSharp.SKColor(80, 0, 80), // Borda do avatar
                        Style = SkiaSharp.SKPaintStyle.Stroke,
                        StrokeWidth = 2,
                        IsAntialias = true
                    };
                    canvas.DrawOval(avatarX + avatarSize / 2, avatarY + avatarSize / 2, avatarSize / 2, avatarSize / 2, avatarBorderPaint);
                }
            }
            catch { }

            var titlePaint = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(80, 0, 80), // Título Daily
                TextSize = 20,
                IsAntialias = true,
                Typeface = fontBold
            };
            canvas.DrawText("Daily", 75, 32, titlePaint);

            var namePaint = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(170, 170, 180),
                TextSize = 14,
                IsAntialias = true,
                Typeface = fontNormal
            };
            canvas.DrawText(user.DisplayName, 75, 52, namePaint);

            var sepPaint = new SkiaSharp.SKPaint { StrokeWidth = 1, IsAntialias = true };
            sepPaint.Shader = SkiaSharp.SKShader.CreateLinearGradient(
                new SkiaSharp.SKPoint(20, 0),
                new SkiaSharp.SKPoint(width - 20, 0),
                new[] { new SkiaSharp.SKColor(60, 60, 70, 0), new SkiaSharp.SKColor(80, 0, 80), new SkiaSharp.SKColor(60, 60, 70, 0) },
                null,
                SkiaSharp.SKShaderTileMode.Clamp);
            canvas.DrawLine(20, 70, width - 20, 70, sepPaint);

            var coinGlow = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(230, 180, 60, 40),
                IsAntialias = true,
                MaskFilter = SkiaSharp.SKMaskFilter.CreateBlur(SkiaSharp.SKBlurStyle.Normal, 8)
            };
            canvas.DrawCircle(width - 60, 130, 35, coinGlow);

            var coinOuter = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(230, 180, 60),
                Style = SkiaSharp.SKPaintStyle.Stroke,
                StrokeWidth = 3,
                IsAntialias = true
            };
            canvas.DrawCircle(width - 60, 130, 25, coinOuter);

            var coinInner = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(230, 180, 60),
                TextSize = 22,
                IsAntialias = true,
                Typeface = fontBold,
                TextAlign = SkiaSharp.SKTextAlign.Center
            };
            canvas.DrawText("$", width - 60, 138, coinInner);

            var recompLabel = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(170, 170, 180),
                TextSize = 14,
                IsAntialias = true,
                Typeface = fontNormal
            };
            canvas.DrawText("Recompensa coletada:", 30, 95, recompLabel);

            var recompValor = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(100, 220, 100),
                TextSize = 28,
                IsAntialias = true,
                Typeface = fontBold
            };
            canvas.DrawText($"+{EconomyHelper.FormatarSaldo(recompensa)} cpoints", 30, 128, recompValor);

            var saldoLabel = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(140, 140, 150),
                TextSize = 13,
                IsAntialias = true,
                Typeface = fontNormal
            };
            canvas.DrawText($"Saldo atual: {EconomyHelper.FormatarSaldo(saldoAtual)} cpoints", 30, 155, saldoLabel);

            var cooldownPaint = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(100, 100, 110),
                TextSize = 11,
                IsAntialias = true,
                Typeface = fontNormal
            };
            canvas.DrawText("Volte em 24h para coletar novamente", 30, 180, cooldownPaint);

            var path = Path.Combine(Path.GetTempPath(), $"daily_{user.Id}_{DateTime.Now.Ticks}.png");
            using var image = surface.Snapshot();
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);

            return path;
        }

        public static async Task<string> GerarImagemRank(SocketGuild guild, List<(ulong UserId, long Saldo)> topUsers)
        {
            int width = 850;
            int height = 680;

            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height));
            var canvas = surface.Canvas;

            var fontBold = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans", SkiaSharp.SKFontStyle.Bold) ?? SkiaSharp.SKTypeface.Default;
            var fontNormal = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans") ?? SkiaSharp.SKTypeface.Default;

            var bgPaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(20, 10, 30), IsAntialias = true };
            canvas.DrawRect(new SkiaSharp.SKRect(0, 0, width, height), bgPaint);

            var titleWhite = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, TextSize = 40, Typeface = fontBold, IsAntialias = true };
            var titleGold = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(255, 215, 0), TextSize = 40, Typeface = fontBold, IsAntialias = true };
            canvas.DrawText("Top", 40, 80, titleWhite);
            canvas.DrawText("Coins", 125, 80, titleGold);

            var avatares = new Dictionary<ulong, byte[]>();
            using var httpClient = new HttpClient();
            var tasks = topUsers.Select(async u =>
            {
                try
                {
                    var user = guild.GetUser(u.UserId);
                    var url = user?.GetAvatarUrl(ImageFormat.Png, 128) ?? user?.GetDefaultAvatarUrl();
                    if (url != null)
                    {
                        var bytes = await httpClient.GetByteArrayAsync(url);
                        avatares[u.UserId] = bytes;
                    }
                }
                catch { }
            });
            await Task.WhenAll(tasks);

            int startX = 40;
            int startY = 120;
            int cardWidth = 370;
            int cardHeight = 85;
            int marginX = 400;
            int marginY = 105;

            for (int i = 0; i < topUsers.Count; i++)
            {
                int col = i % 2;
                int row = i / 2;

                int x = startX + (col * marginX);
                int y = startY + (row * marginY);

                var rank = i + 1;
                var user = topUsers[i];
                var member = guild.GetUser(user.UserId);
                string username = member?.Username ?? "Desconhecido";

                if (username.Length > 12) username = username.Substring(0, 10) + "...";

                SkiaSharp.SKColor bgColor;
                SkiaSharp.SKColor textColor;

                if (rank == 1) { bgColor = new SkiaSharp.SKColor(255, 180, 0); textColor = SkiaSharp.SKColors.Black; }
                else if (rank == 2) { bgColor = new SkiaSharp.SKColor(220, 220, 230); textColor = SkiaSharp.SKColors.Black; }
                else if (rank == 3) { bgColor = new SkiaSharp.SKColor(255, 120, 0); textColor = SkiaSharp.SKColors.Black; }
                else { bgColor = new SkiaSharp.SKColor(80, 0, 80); textColor = SkiaSharp.SKColors.White; } // Pílulas 4-10 em roxo 80,0,80

                var cardPaint = new SkiaSharp.SKPaint { Color = bgColor, IsAntialias = true };
                canvas.DrawRoundRect(new SkiaSharp.SKRect(x, y, x + cardWidth, y + cardHeight), cardHeight / 2, cardHeight / 2, cardPaint);

                int avatarSize = 65;
                int avatarX = x + 10;
                int avatarY = y + 10;

                if (avatares.TryGetValue(user.UserId, out var avatarBytes))
                {
                    try
                    {
                        using var avatarBitmap = SkiaSharp.SKBitmap.Decode(avatarBytes);
                        var avatarRect = new SkiaSharp.SKRect(avatarX, avatarY, avatarX + avatarSize, avatarY + avatarSize);
                        var clipPath = new SkiaSharp.SKPath();
                        clipPath.AddOval(avatarRect);

                        canvas.Save();
                        canvas.ClipPath(clipPath);
                        canvas.DrawBitmap(avatarBitmap, avatarRect);
                        canvas.Restore();

                        var borderPaint = new SkiaSharp.SKPaint { Color = textColor, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
                        canvas.DrawOval(avatarX + avatarSize / 2, avatarY + avatarSize / 2, avatarSize / 2, avatarSize / 2, borderPaint);
                    }
                    catch { }
                }

                var namePaint = new SkiaSharp.SKPaint { Color = textColor, TextSize = 22, Typeface = fontBold, IsAntialias = true };
                canvas.DrawText(username, x + 90, y + 50, namePaint);

                var valuePaint = new SkiaSharp.SKPaint { Color = textColor, TextSize = 22, Typeface = fontNormal, IsAntialias = true, TextAlign = SkiaSharp.SKTextAlign.Right };
                canvas.DrawText(EconomyHelper.FormatarSaldo(user.Saldo), x + cardWidth - 25, y + 50, valuePaint);

                var rankNumPaint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.LightGray, TextSize = 14, Typeface = fontBold, IsAntialias = true };
                canvas.DrawText(rank.ToString(), x, y - 5, rankNumPaint);
            }

            var path = Path.Combine(Path.GetTempPath(), $"rank_{guild.Id}_{DateTime.Now.Ticks}.png");
            using var image = surface.Snapshot();
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);

            return path;
        }

        private static void DrawItem(SkiaSharp.SKCanvas canvas, float x, float y, string label, string valor,
            SkiaSharp.SKTypeface fontBold, SkiaSharp.SKTypeface fontNormal,
            SkiaSharp.SKColor accentColor, SkiaSharp.SKColor bgTint, int width,
            Action<SkiaSharp.SKCanvas, float, float, SkiaSharp.SKColor> drawIcon)
        {
            var itemBg = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(38, 38, 48), IsAntialias = true };
            canvas.DrawRoundRect(new SkiaSharp.SKRect(x, y, width - 40, y + 40), 10, 10, itemBg);

            var barPaint = new SkiaSharp.SKPaint { Color = accentColor, IsAntialias = true };
            canvas.DrawRoundRect(new SkiaSharp.SKRect(x, y, x + 4, y + 40), 2, 2, barPaint);

            drawIcon(canvas, x + 22, y + 20, accentColor);

            var labelPaint = new SkiaSharp.SKPaint
            {
                Color = SkiaSharp.SKColors.White,
                TextSize = 15,
                IsAntialias = true,
                Typeface = fontBold
            };
            canvas.DrawText(label, x + 45, y + 17, labelPaint);

            var valorPaint = new SkiaSharp.SKPaint
            {
                Color = new SkiaSharp.SKColor(170, 170, 180),
                TextSize = 13,
                IsAntialias = true,
                Typeface = fontNormal
            };
            canvas.DrawText(valor, x + 45, y + 34, valorPaint);
        }

        private static void DrawWalletIcon(SkiaSharp.SKCanvas canvas, float cx, float cy, SkiaSharp.SKColor color)
        {
            var paint = new SkiaSharp.SKPaint { Color = color, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawRoundRect(new SkiaSharp.SKRect(cx - 8, cy - 6, cx + 8, cy + 6), 2, 2, paint);
            canvas.DrawLine(cx - 8, cy - 3, cx + 8, cy - 3, paint);
            var fillPaint = new SkiaSharp.SKPaint { Color = color, IsAntialias = true };
            canvas.DrawCircle(cx + 4, cy + 1, 2, fillPaint);
        }

        private static void DrawBankIcon(SkiaSharp.SKCanvas canvas, float cx, float cy, SkiaSharp.SKColor color)
        {
            var paint = new SkiaSharp.SKPaint { Color = color, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 2 };
            var path = new SkiaSharp.SKPath();
            path.MoveTo(cx, cy - 8);
            path.LineTo(cx - 10, cy - 2);
            path.LineTo(cx + 10, cy - 2);
            path.Close();
            canvas.DrawPath(path, paint);
            canvas.DrawLine(cx - 9, cy + 7, cx + 9, cy + 7, paint);
            canvas.DrawLine(cx - 6, cy - 1, cx - 6, cy + 6, paint);
            canvas.DrawLine(cx, cy - 1, cx, cy + 6, paint);
            canvas.DrawLine(cx + 6, cy - 1, cx + 6, cy + 6, paint);
        }

        private static void DrawCoinIcon(SkiaSharp.SKCanvas canvas, float cx, float cy, SkiaSharp.SKColor color)
        {
            var paint = new SkiaSharp.SKPaint { Color = color, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawCircle(cx, cy, 8, paint);
            var textPaint = new SkiaSharp.SKPaint
            {
                Color = color,
                TextSize = 12,
                IsAntialias = true,
                Typeface = SkiaSharp.SKTypeface.Default,
                TextAlign = SkiaSharp.SKTextAlign.Center
            };
            canvas.DrawText("$", cx, cy + 4, textPaint);
        }
    }

    public class EconomyHandler
    {
        private readonly DiscordSocketClient _client;

        // ADICIONADO: Dicionário para guardar o tempo de cooldown de cada usuário
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();

        public EconomyHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
        }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessarMensagem(msg);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Economy] Erro: {ex.Message}");
                }
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

            // 1. Verifica se a mensagem é um comando do bot (para não dar cooldown no chat normal)
            string[] comandos = { "zhelp", "zsaldo", "zdaily", "zpay", "zrank", "ztop coins" };
            bool isComando = comandos.Any(c => content == c || content.StartsWith(c + " "));

            if (!isComando) return;

            // 2. SISTEMA DE COOLDOWN (5 Segundos)
            if (_cooldowns.TryGetValue(user.Id, out var ultimaVez))
            {
                var tempoPassado = (DateTime.UtcNow - ultimaVez).TotalSeconds;
                if (tempoPassado < 5) // Se passou menos de 5 segundos
                {
                    int restante = 5 - (int)tempoPassado;
                    var aviso = await msg.Channel.SendMessageAsync($"⏳ Calma lá, {user.Mention}! Aguarde `{restante}s` para usar outro comando.");

                    // Apaga a mensagem de aviso depois de 3 segundos para não poluir o chat
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        try { await aviso.DeleteAsync(); } catch { }
                    });

                    return; // Bloqueia a execução do comando
                }
            }

            // Atualiza o tempo da última vez que o usuário usou um comando
            _cooldowns[user.Id] = DateTime.UtcNow;

            // --- DAQUI PARA BAIXO É O SEU CÓDIGO ORIGINAL DOS COMANDOS ---

            if (content == "zhelp")
            {
                var emojiAnimado = "<a:teste:1490570407307378712>";

                var embed = new EmbedBuilder()
                    .WithAuthor($"Ajuda | {_client.CurrentUser.Username}", _client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                    .WithDescription($"{emojiAnimado} Bem-vindo(a) {user.Mention}, esse é o **painel de comandos/ajuda** - {_client.CurrentUser.Username}\n\n" +
                                     "↪ **Selecione uma categoria abaixo** para ver os comandos disponíveis até o momento.")
                    .WithThumbnailUrl(_client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                    .WithFooter($"Comando executado por: {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .WithColor(new Discord.Color(80, 0, 80))
                    .Build();

                var menu = new SelectMenuBuilder()
                    .WithCustomId("help_menu")
                    .WithPlaceholder("Selecione uma categoria")
                    .AddOption("Moderação", "help_mod", "Comandos de moderação", Emote.Parse("<:suporte:1492662681130373201>"))
                    .AddOption("Economia", "help_eco", "Comandos de cpoints", Emote.Parse("<:botportal:1492661012682248212>"))
                    .AddOption("Administração", "help_admin", "Configuração do bot", Emote.Parse("<:botportal:1492661012682248212>"));

                var components = new ComponentBuilder().WithSelectMenu(menu).Build();

                await msg.Channel.SendMessageAsync(embed: embed, components: components);
                return;
            }

            if (content == "zsaldo" || content.StartsWith("zsaldo "))
            {
                SocketGuildUser alvo = user;
                if (msg.MentionedUsers.Count > 0)
                    alvo = user.Guild.GetUser(msg.MentionedUsers.First().Id) ?? user;

                var saldo = EconomyHelper.GetSaldo(guildId, alvo.Id);
                var imagemPath = await EconomyImageHelper.GerarImagemSaldo(alvo, saldo);
                await msg.Channel.SendFileAsync(imagemPath, "");
                File.Delete(imagemPath);
            }
            else if (content == "zdaily")
            {
                var ultimoDaily = EconomyHelper.GetUltimoDaily(guildId, user.Id);
                var agora = DateTime.UtcNow;
                var diferenca = agora - ultimoDaily;

                if (diferenca.TotalHours < 24)
                {
                    var restante = TimeSpan.FromHours(24) - diferenca;
                    var horas = (int)restante.TotalHours;
                    var minutos = restante.Minutes;
                    var segundos = restante.Seconds;

                    await msg.Channel.SendMessageAsync($"voce ja coletou seu daily hoje. volte em `{horas}h {minutos}m {segundos}s`.");
                    return;
                }

                var random = new Random();
                long recompensa = random.Next(500, 2001);

                EconomyHelper.AdicionarSaldo(guildId, user.Id, recompensa);
                EconomyHelper.SetUltimoDaily(guildId, user.Id);

                var saldoAtual = EconomyHelper.GetSaldo(guildId, user.Id);

                var imagemPath = await EconomyImageHelper.GerarImagemDaily(user, recompensa, saldoAtual);
                await msg.Channel.SendFileAsync(imagemPath, "");
                File.Delete(imagemPath);
            }
            else if (content.StartsWith("zpay"))
            {
                if (msg.MentionedUsers.Count == 0)
                { await msg.Channel.SendMessageAsync("use: `zpay @usuario valor`"); return; }

                var alvo = user.Guild.GetUser(msg.MentionedUsers.First().Id);
                if (alvo == null) { await msg.Channel.SendMessageAsync("usuario nao encontrado."); return; }
                if (alvo.Id == user.Id) { await msg.Channel.SendMessageAsync("voce nao pode pagar a si mesmo."); return; }
                if (alvo.IsBot) { await msg.Channel.SendMessageAsync("voce nao pode pagar um bot."); return; }

                long valor = 0;
                foreach (var parte in content.Split(' '))
                { if (long.TryParse(parte, out var v)) { valor = v; break; } }

                if (valor <= 0) { await msg.Channel.SendMessageAsync("valor invalido."); return; }
                if (!EconomyHelper.RemoverSaldo(guildId, user.Id, valor))
                { await msg.Channel.SendMessageAsync("saldo insuficiente."); return; }

                EconomyHelper.AdicionarSaldo(guildId, alvo.Id, valor);
                var saldoRemetente = EconomyHelper.GetSaldo(guildId, user.Id);

                await msg.Channel.SendMessageAsync(
                    $"{user.Mention} transferiu `{EconomyHelper.FormatarSaldo(valor)}` cpoints para {alvo.Mention}\n" +
                    $"**Seu saldo:** `{EconomyHelper.FormatarSaldo(saldoRemetente)}` cpoints");
            }
            else if (content == "zrank" || content == "ztop coins")
            {
                var top10 = EconomyHelper.GetTop10(guildId);

                if (top10.Count == 0)
                {
                    await msg.Channel.SendMessageAsync("Ainda não há ninguém no ranking de cpoints.");
                    return;
                }

                var carregando = "<a:carregandoportal:1492944498605686844>";
                var loadingMsg = await msg.Channel.SendMessageAsync($"{carregando} Gerando o ranking, aguarde um instante...");

                var rankInfo = EconomyHelper.GetUserRankInfo(guildId, user.Id);
                var emojiRoxo = "<:emoji_8:1491910148476899529>";

                string textoPosicao = rankInfo.Rank > 0
                    ? $"{emojiRoxo} **Posição #{rankInfo.Rank}** - **{EconomyHelper.FormatarSaldo(rankInfo.Saldo)}** cpoints"
                    : "💡 Você ainda não possui cpoints para aparecer no ranking.";

                string textoMensagem = $"{emojiRoxo} Os usuários mais **ricos** do servidor! 💰\n{textoPosicao}";

                var imagemPath = await EconomyImageHelper.GerarImagemRank(user.Guild, top10);

                await msg.Channel.SendFileAsync(imagemPath, textoMensagem);
                await loadingMsg.DeleteAsync();

                File.Delete(imagemPath);
            }
        }
    }
}
