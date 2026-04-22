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
                    receiver_id TEXT, amount BIGINT, type TEXT, data TIMESTAMP DEFAULT CURRENT_TIMESTAMP);

                CREATE TABLE IF NOT EXISTS daily_reminders (
                    user_id TEXT, 
                    guild_id TEXT, 
                    remind_at TIMESTAMP, 
                    PRIMARY KEY (user_id, guild_id));";
            cmd.ExecuteNonQuery();
        }

        public static void SalvarLembrete(ulong guildId, ulong userId, DateTime dataAviso)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO daily_reminders (guild_id, user_id, remind_at) VALUES (@gid, @uid, @dt)
                                ON CONFLICT (user_id, guild_id) DO UPDATE SET remind_at = @dt";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@dt", dataAviso);
            cmd.ExecuteNonQuery();
        }

        public static void RemoverLembrete(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM daily_reminders WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.ExecuteNonQuery();
        }

        public static DateTime GetUltimoDaily(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ultimo_daily FROM economy_users WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var res = cmd.ExecuteScalar();
            if (res != null && res != DBNull.Value) return Convert.ToDateTime(res);
            return DateTime.MinValue;
        }

        public static void AtualizarDaily(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO economy_users (guild_id, user_id, ultimo_daily) VALUES (@gid, @uid, @dt)
                                ON CONFLICT (guild_id, user_id) DO UPDATE SET ultimo_daily = @dt";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@dt", DateTime.Now);
            cmd.ExecuteNonQuery();
        }

        public static List<(string SenderId, string ReceiverId, long Amount, string Type, DateTime Date)> GetTransacoes(ulong guildId, ulong userId)
        {
            var list = new List<(string, string, long, string, DateTime)>();
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT sender_id, receiver_id, amount, type, data 
                        FROM economy_transactions 
                        WHERE guild_id = @gid AND (sender_id = @uid OR receiver_id = @uid) 
                        ORDER BY data DESC LIMIT 10";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3), reader.GetDateTime(4)));
            }
            return list;
        }

        public static long GetSaldo(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT saldo FROM economy_users WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var res = cmd.ExecuteScalar(); return res != null ? (long)res : 0;
        }

        public static long GetBanco(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT banco FROM economy_users WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var res = cmd.ExecuteScalar(); return res != null ? (long)res : 0;
        }

        public static void AdicionarSaldo(ulong guildId, ulong userId, long valor)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO economy_users (guild_id, user_id, saldo) VALUES (@gid, @uid, @valor)
                                ON CONFLICT (guild_id, user_id) DO UPDATE SET saldo = economy_users.saldo + @valor";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@valor", valor); cmd.ExecuteNonQuery();
        }

        public static bool RemoverSaldo(ulong guildId, ulong userId, long valor)
        {
            if (GetSaldo(guildId, userId) < valor) return false;
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE economy_users SET saldo = saldo - @valor WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@valor", valor); return cmd.ExecuteNonQuery() > 0;
        }

        public static void AdicionarBanco(ulong guildId, ulong userId, long valor)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO economy_users (guild_id, user_id, banco) VALUES (@gid, @uid, @valor)
                                ON CONFLICT (guild_id, user_id) DO UPDATE SET banco = economy_users.banco + @valor";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@valor", valor); cmd.ExecuteNonQuery();
        }

        public static bool RemoverBanco(ulong guildId, ulong userId, long valor)
        {
            if (GetBanco(guildId, userId) < valor) return false;
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE economy_users SET banco = banco - @valor WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@valor", valor); return cmd.ExecuteNonQuery() > 0;
        }

        public static bool DepositarTudo(ulong guildId, ulong userId)
        {
            long s = GetSaldo(guildId, userId); if (s <= 0) return false;
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE economy_users SET banco = banco + saldo, saldo = 0 WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            return cmd.ExecuteNonQuery() > 0;
        }

        public static void SetSaldo(ulong guildId, ulong userId, long valor)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO economy_users (guild_id, user_id, saldo, banco) VALUES (@gid, @uid, @valor, 0)
                                ON CONFLICT (guild_id, user_id) DO UPDATE SET saldo = @valor, banco = 0";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@valor", valor);
            cmd.ExecuteNonQuery();
        }

        public static void RegistrarTransacao(ulong guildId, ulong sender, ulong receiver, long amount, string type)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO economy_transactions (guild_id, sender_id, receiver_id, amount, type) VALUES (@gid, @sid, @rid, @amount, @type)";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@sid", sender.ToString());
            cmd.Parameters.AddWithValue("@rid", receiver.ToString()); cmd.Parameters.AddWithValue("@amount", amount);
            cmd.Parameters.AddWithValue("@type", type); cmd.ExecuteNonQuery();
        }

        public static string FormatarSaldo(long valor)
        {
            if (valor >= 1_000_000_000_000) return $"{valor / 1_000_000_000_000.0:F2}T";
            if (valor >= 1_000_000_000) return $"{valor / 1_000_000_000.0:F2}B";
            if (valor >= 1_000_000) return $"{valor / 1_000_000.0:F2}M";
            if (valor >= 1_000) return $"{valor / 1_000.0:F2}K";
            return valor.ToString();
        }

        public static long ConverterLetraParaNumero(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0;
            string valTxt = input.ToLower().Trim();
            try
            {
                if (valTxt.EndsWith("t")) return (long)(double.Parse(valTxt.Replace("t", "")) * 1_000_000_000_000);
                if (valTxt.EndsWith("b")) return (long)(double.Parse(valTxt.Replace("b", "")) * 1_000_000_000);
                if (valTxt.EndsWith("m")) return (long)(double.Parse(valTxt.Replace("m", "")) * 1_000_000);
                if (valTxt.EndsWith("k")) return (long)(double.Parse(valTxt.Replace("k", "")) * 1_000);
                return long.TryParse(valTxt, out var v) ? v : 0;
            }
            catch { return 0; }
        }

        public static long GetPosicaoRank(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(*) + 1 FROM economy_users WHERE guild_id = @gid AND (saldo + banco) > (SELECT COALESCE(saldo + banco, 0) FROM economy_users WHERE guild_id = @gid AND user_id = @uid)";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@uid", userId.ToString());
            return (long)(cmd.ExecuteScalar() ?? 1L);
        }

        public static List<(ulong UserId, long Total)> GetTop10(ulong guildId)
        {
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

    public static class EconomyImageHelper
    {
        private static readonly SKColor PurpleGradientStart = new SKColor(200, 100, 220);
        private static readonly SKColor PurpleGradientEnd = new SKColor(150, 60, 200);
        private static readonly SKColor PinkAccent = new SKColor(255, 150, 200);
        private static readonly SKColor WhiteBg = new SKColor(248, 248, 252);
        private static readonly SKColor CardBg = new SKColor(255, 255, 255);

        public static async Task<string> GerarImagemPerfil(SocketUser user, long wallet, long bank)
        {
            int width = 900;
            int height = 520;
            long total = wallet + bank;

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(WhiteBg);

            // BANNER GRADIENTE NO TOPO
            var bannerRect = new SKRect(0, 0, width, 200);
            using var bannerPaint = new SKPaint();
            bannerPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(width, 200),
                new[] { PurpleGradientStart, PurpleGradientEnd },
                new[] { 0f, 1f }, SKShaderTileMode.Clamp);
            canvas.DrawRect(bannerRect, bannerPaint);

            // Decoração de nuvem/onda no banner
            DrawWavePattern(canvas, width, 200);

            // CARD BRANCO PRINCIPAL
            var cardRect = new SKRect(40, 140, width - 40, height - 30);
            canvas.DrawRoundRect(cardRect, 25, 25, new SKPaint { Color = CardBg, IsAntialias = true });

            // AVATAR GIGANTE (direita, semi-sobreposto)
            float avX = width - 150; // Centralizando melhor no espaço direito
            float avY = 160;
            float avRadius = 100;
            var avRect = new SKRect(avX - avRadius, avY - avRadius, avX + avRadius, avY + avRadius);

            using var http = new HttpClient();
            try
            {
                var bytes = await http.GetByteArrayAsync(user.GetAvatarUrl(ImageFormat.Png, 512) ?? user.GetDefaultAvatarUrl());
                using var bmp = SKBitmap.Decode(bytes);
                var path = new SKPath();
                path.AddOval(avRect);
                canvas.Save();
                canvas.ClipPath(path, SKClipOperation.Intersect, true);
                canvas.DrawBitmap(bmp, avRect);
                canvas.Restore();
            }
            catch
            {
                canvas.DrawOval(avRect, new SKPaint { Color = new SKColor(220, 220, 220), IsAntialias = true });
            }

            canvas.DrawOval(avRect, new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 6, Color = CardBg, IsAntialias = true });

            // USERNAME EM PÍLULA PREMIUM
            string displayName = (user as SocketGuildUser)?.Nickname ?? user.GlobalName ?? user.Username;
            var fontBold = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);
            var fontNormal = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Normal);

            using var namePaint = new SKPaint { Color = new SKColor(30, 30, 45), TextSize = 36, Typeface = fontBold, TextAlign = SKTextAlign.Center, IsAntialias = true };
            float nameWidth = namePaint.MeasureText(displayName);

            float pillW = Math.Max(nameWidth + 100, 240); // Espaçamento e tamanho premium
            float pillH = 55;
            float pillX = avX;
            float pillY = avY + avRadius + 25; // Distância correta do avatar

            var pillRect = new SKRect(pillX - pillW / 2, pillY, pillX + pillW / 2, pillY + pillH);

            // Sombra sutil para a pílula do nome
            using var pillShadow = new SKPaint { Color = new SKColor(0, 0, 0, 15), ImageFilter = SKImageFilter.CreateDropShadow(0, 5, 8, 8, new SKColor(0, 0, 0, 20)), IsAntialias = true };
            canvas.DrawRoundRect(pillRect, pillH / 2, pillH / 2, pillShadow);

            // Fundo da pílula do nome
            canvas.DrawRoundRect(pillRect, pillH / 2, pillH / 2, new SKPaint { Color = SKColors.White, IsAntialias = true });

            // Texto centralizado
            canvas.DrawText(displayName, pillX, pillY + 38, namePaint);

            // GRID DE CARDS (2x2)
            float col1X = 80;
            float col2X = 480;
            float row1Y = 190;
            float row2Y = 350;
            float cardW = 360;
            float cardH = 120;

            DrawStatCard(canvas, "Patrimônio Total", $"{EconomyHelper.FormatarSaldo(total)}", $"Carteira: {EconomyHelper.FormatarSaldo(wallet)} | Banco: {EconomyHelper.FormatarSaldo(bank)}", col1X, row1Y, cardW, cardH, fontBold, fontNormal, PurpleGradientStart);
            DrawStatCard(canvas, "Nível", "1", "0 / 100 XP", col2X, row1Y, cardW, cardH, fontBold, fontNormal, PinkAccent);
            DrawStatCard(canvas, "Reputação", "0 Reps", "", col1X, row2Y, cardW, cardH, fontBold, fontNormal, PurpleGradientEnd);
            DrawStatCard(canvas, "Status", "Online", "", col2X, row2Y, cardW, cardH, fontBold, fontNormal, new SKColor(100, 200, 100));

            var p = Path.Combine(Path.GetTempPath(), $"perfil_{user.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(p)) data.SaveTo(str);

            return p;
        }

        private static void DrawWavePattern(SKCanvas canvas, int width, int height)
        {
            using var wavePaint = new SKPaint { Color = new SKColor(255, 255, 255, 15), IsAntialias = true };
            var path = new SKPath();
            path.MoveTo(0, height - 30);
            for (int i = 0; i <= width; i += 50)
            {
                path.LineTo(i, height - 30 + (i % 100 == 0 ? 20 : -20));
            }
            path.LineTo(width, height);
            path.LineTo(0, height);
            path.Close();
            canvas.DrawPath(path, wavePaint);
        }

        private static void DrawStatCard(SKCanvas canvas, string title, string value, string subtext, float x, float y, float w, float h, SKTypeface fontBold, SKTypeface fontNormal, SKColor accentColor)
        {
            canvas.DrawRoundRect(new SKRect(x, y, x + w, y + h), 15, 15,
                new SKPaint { Color = new SKColor(250, 250, 255), IsAntialias = true });

            canvas.DrawRoundRect(new SKRect(x, y, x + 8, y + h), 4, 4,
                new SKPaint { Color = accentColor, IsAntialias = true });

            using var titlePaint = new SKPaint { Color = new SKColor(130, 130, 150), TextSize = 16, Typeface = fontNormal, IsAntialias = true };
            canvas.DrawText(title, x + 25, y + 30, titlePaint);

            using var valuePaint = new SKPaint { Color = new SKColor(40, 40, 60), TextSize = 28, Typeface = fontBold, IsAntialias = true };
            canvas.DrawText(value, x + 25, y + 70, valuePaint);

            if (!string.IsNullOrEmpty(subtext))
            {
                using var subPaint = new SKPaint { Color = new SKColor(160, 160, 170), TextSize = 14, Typeface = fontNormal, IsAntialias = true };
                canvas.DrawText(subtext, x + 25, y + 100, subPaint);
            }
        }

        public static async Task<string> GerarImagemSaldo(SocketUser user, long wallet, long bank)
        {
            int width = 500;
            int height = 700;

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;

            using var bgPaint = new SKPaint();
            bgPaint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(width / 2, height / 2),
                Math.Max(width, height),
                new[] { new SKColor(25, 20, 45), new SKColor(10, 8, 18) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, width, height, bgPaint);

            var cardRect = new SKRect(25, 25, width - 25, height - 25);
            using var cardPaint = new SKPaint { Color = new SKColor(22, 18, 35), IsAntialias = true };
            canvas.DrawRoundRect(cardRect, 40, 40, cardPaint);

            using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(255, 255, 255, 25), IsAntialias = true };
            canvas.DrawRoundRect(cardRect, 40, 40, borderPaint);

            float avRadius = 95;
            float avY = 160;
            var avRect = new SKRect((width / 2) - avRadius, avY - avRadius, (width / 2) + avRadius, avY + avRadius);

            using var http = new HttpClient();
            try
            {
                var bytes = await http.GetByteArrayAsync(user.GetAvatarUrl(ImageFormat.Png, 512) ?? user.GetDefaultAvatarUrl());
                using var bmp = SKBitmap.Decode(bytes);
                var path = new SKPath();
                path.AddOval(avRect);
                canvas.Save();
                canvas.ClipPath(path, SKClipOperation.Intersect, true);
                canvas.DrawBitmap(bmp, avRect);
                canvas.Restore();
            }
            catch { }

            canvas.DrawOval(avRect, new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 6, Color = PurpleGradientStart, IsAntialias = true });

            string displayName = (user as SocketGuildUser)?.Nickname ?? user.GlobalName ?? user.Username;
            var fontBold = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);

            // USERNAME EM PÍLULA PREMIUM ESCURA
            using var namePaint = new SKPaint { Color = SKColors.White, TextSize = 36, Typeface = fontBold, TextAlign = SKTextAlign.Center, IsAntialias = true };
            float nameWidth = namePaint.MeasureText(displayName);

            float pillW = Math.Max(nameWidth + 100, 240);
            float pillH = 55;
            float pillX = width / 2;
            float pillY = avY + avRadius + 25;

            var pillRect = new SKRect(pillX - pillW / 2, pillY, pillX + pillW / 2, pillY + pillH);

            using var nameBgPaint = new SKPaint { Color = new SKColor(255, 255, 255, 20), IsAntialias = true };
            canvas.DrawRoundRect(pillRect, pillH / 2, pillH / 2, nameBgPaint);
            canvas.DrawText(displayName, pillX, pillY + 38, namePaint);

            DrawModernPanel(canvas, "Carteira", wallet, width, 370, PurpleGradientStart, fontBold);
            DrawModernPanel(canvas, "Banco", bank, width, 465, PurpleGradientEnd, fontBold);
            DrawModernPanel(canvas, "Total", wallet + bank, width, 560, PinkAccent, fontBold);

            var p = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(p)) data.SaveTo(str);

            return p;
        }

        private static void DrawModernPanel(SKCanvas canvas, string label, long valor, int totalWidth, float y, SKColor accent, SKTypeface font)
        {
            float pWidth = totalWidth - 90;
            float pHeight = 70;
            float x = 45;

            var rect = new SKRect(x, y, x + pWidth, y + pHeight);
            canvas.DrawRoundRect(rect, pHeight / 2, pHeight / 2, new SKPaint { Color = new SKColor(14, 12, 22), IsAntialias = true });

            float circleX = x + pHeight / 2;
            float circleY = y + pHeight / 2;
            canvas.DrawCircle(circleX, circleY, 22, new SKPaint { Color = accent, IsAntialias = true });

            float textX = x + pHeight + 15;
            using var labelPaint = new SKPaint { Color = SKColors.White, TextSize = 20, Typeface = font, IsAntialias = true };
            canvas.DrawText(label, textX, y + 25, labelPaint);

            string valorFormatado = EconomyHelper.FormatarSaldo(valor);
            string valorStr = $"{valor} ({valorFormatado})";
            using var valuePaint = new SKPaint { Color = new SKColor(180, 180, 200), TextSize = 16, IsAntialias = true };
            canvas.DrawText(valorStr, textX, y + 50, valuePaint);
        }

        public static async Task<string> GerarImagemRank(SocketGuild guild, List<(ulong UserId, long Total)> top)
        {
            int w = 850;
            int h = 750;
            using var surface = SKSurface.Create(new SKImageInfo(w, h));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(12, 10, 20));

            var boldFont = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);
            var paintWhite = new SKPaint { Color = SKColors.White, TextSize = 48, Typeface = boldFont, IsAntialias = true };
            var paintPurple = new SKPaint { Color = PurpleGradientStart, TextSize = 48, Typeface = boldFont, IsAntialias = true };

            canvas.DrawText("Top", 40, 80, paintWhite);
            canvas.DrawText("Coins", 140, 80, paintPurple);

            using var http = new HttpClient();
            for (int i = 0; i < top.Count; i++)
            {
                IUser m = guild.GetUser(top[i].UserId) ?? await ((IGuild)guild).GetUserAsync(top[i].UserId);
                int col = i % 2;
                int row = i / 2;
                float x = 40 + (col * 405);
                float y = 120 + (row * 115);
                int pos = i + 1;

                SKColor pillColor = pos switch
                {
                    1 => new SKColor(255, 215, 0),
                    2 => new SKColor(192, 192, 192),
                    3 => new SKColor(205, 127, 50),
                    _ => new SKColor(35, 32, 55)
                };

                SKColor textColor = (pos <= 3) ? SKColors.Black : SKColors.White;

                canvas.DrawRoundRect(new SKRect(x, y, x + 385, y + 100), 20, 20, new SKPaint { Color = pillColor, IsAntialias = true });

                try
                {
                    var bytes = await http.GetByteArrayAsync(m?.GetAvatarUrl() ?? m?.GetDefaultAvatarUrl());
                    using var bmp = SKBitmap.Decode(bytes);
                    var avRect = new SKRect(x + 15, y + 15, x + 85, y + 85);
                    var path = new SKPath();
                    path.AddOval(avRect);
                    canvas.Save();
                    canvas.ClipPath(path, SKClipOperation.Intersect, true);
                    canvas.DrawBitmap(bmp, avRect);
                    canvas.Restore();
                    canvas.DrawOval(avRect, new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = textColor, IsAntialias = true });
                }
                catch { }

                string name = (m?.Username ?? "Usuário").Length > 12 ? (m?.Username ?? "Usuário").Substring(0, 10) + ".." : (m?.Username ?? "Usuário");
                canvas.DrawText($"{pos}. {name}", x + 100, y + 50, new SKPaint { Color = textColor, TextSize = 22, Typeface = boldFont, IsAntialias = true });
                canvas.DrawText(EconomyHelper.FormatarSaldo(top[i].Total), x + 100, y + 80, new SKPaint { Color = (pos <= 3) ? new SKColor(40, 40, 40) : new SKColor(180, 180, 200), TextSize = 18, IsAntialias = true });
            }

            var pathImg = Path.Combine(Path.GetTempPath(), $"rank_{guild.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(pathImg)) data.SaveTo(str);

            return pathImg;
        }
    }

    public class EconomyHandler
    {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();
        private static readonly Dictionary<ulong, DateTime> _stealCooldowns = new();

        public EconomyHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
            _client.ButtonExecuted += HandleButtonAsync;
            _ = Task.Run(() => VigilanteLembretes());
        }

        private async Task VigilanteLembretes()
        {
            while (true)
            {
                try
                {
                    using var conn = new NpgsqlConnection(EconomyHelper.GetConnectionString());
                    await conn.OpenAsync();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT user_id, guild_id FROM daily_reminders WHERE remind_at <= @agora";
                    cmd.Parameters.AddWithValue("@agora", DateTime.Now);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        ulong userId = ulong.Parse(reader.GetString(0));
                        ulong guildId = ulong.Parse(reader.GetString(1));

                        _ = Task.Run(async () => {
                            try
                            {
                                var user = await _client.GetUserAsync(userId);
                                if (user != null)
                                {
                                    await user.SendMessageAsync($"<a:sino:1495172950767173833> **O seu Daily está pronto!**\nJá pode voltar ao servidor e usar o comando `zdaily` para coletar as suas moedas de hoje!");
                                }
                            }
                            catch { }
                            EconomyHelper.RemoverLembrete(guildId, userId);
                        });
                    }
                }
                catch (Exception ex) { Console.WriteLine("Erro no Vigilante: " + ex.Message); }

                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }

        private async Task HandleButtonAsync(SocketMessageComponent component)
        {
            if (component.Data.CustomId == "btn_lembrete_daily")
            {
                DateTime horaAviso = DateTime.Now.AddHours(24);
                EconomyHelper.SalvarLembrete(component.GuildId ?? 0, component.User.Id, horaAviso);
                await component.RespondAsync($"<a:sino:1495172950767173833> **Lembrete ativado!** Em breve a Zoe vai te chamar na DM quando seu **Daily** estiver pronto.", ephemeral: true);
            }
        }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () => {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage) return;
                    var user = msg.Author as SocketGuildUser;
                    var content = msg.Content.ToLower().Trim();
                    var guildId = user.Guild.Id;

                    string[] cmds = { "zsaldo", "zperfil", "zdaily", "zrank", "zpay", "zdep", "zaddsaldo", "zsetsaldo", "ztransacoes", "ztranscoes", "zroubar" };
                    if (!cmds.Any(c => content.StartsWith(c))) return;

                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 3)
                    {
                        var aviso = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> {user.Mention}, aguarde **3 segundos** para usar outro comando.");
                        _ = Task.Delay(3000).ContinueWith(_ => aviso.DeleteAsync());
                        return;
                    }
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    if (content.StartsWith("zperfil"))
                    {
                        var alvo = msg.MentionedUsers.FirstOrDefault() ?? user;
                        long carteira = EconomyHelper.GetSaldo(guildId, alvo.Id);
                        long banco = EconomyHelper.GetBanco(guildId, alvo.Id);

                        var p = await EconomyImageHelper.GerarImagemPerfil(alvo, carteira, banco);
                        var cb = new ComponentBuilder()
                            .WithButton("Alterar Bio", "btn_bio_dummy", ButtonStyle.Secondary)
                            .WithButton("Enviar Reputação", "btn_rep_dummy", ButtonStyle.Secondary);

                        await msg.Channel.SendFileAsync(p, components: cb.Build());
                        File.Delete(p);
                    }
                    else if (content == "zdaily")
                    {
                        DateTime ultimoDaily = EconomyHelper.GetUltimoDaily(guildId, user.Id);
                        TimeSpan tempoPassado = DateTime.Now - ultimoDaily;

                        if (tempoPassado.TotalHours < 24)
                        {
                            TimeSpan tempoFalta = TimeSpan.FromHours(24) - tempoPassado;
                            await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, você já coletou seu bônus diário! Volte em `{tempoFalta.Hours}h e {tempoFalta.Minutes}m`.");
                            return;
                        }

                        long g = new Random().Next(167000, 180001);
                        EconomyHelper.AdicionarSaldo(guildId, user.Id, g);
                        EconomyHelper.AtualizarDaily(guildId, user.Id);
                        EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, user.Id, g, "DAILY");

                        var eb = new EmbedBuilder()
                            .WithColor(new Color(160, 80, 220))
                            .WithTitle("Daily")
                            .WithDescription($"Você coletou **{EconomyHelper.FormatarSaldo(g)}** cpoints!");

                        var cb = new ComponentBuilder()
                            .WithButton("Definir lembrete", "btn_lembrete_daily", ButtonStyle.Secondary);

                        await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
                    }
                    else if (content == "zsaldo")
                    {
                        var p = await EconomyImageHelper.GerarImagemSaldo(user, EconomyHelper.GetSaldo(guildId, user.Id), EconomyHelper.GetBanco(guildId, user.Id));
                        await msg.Channel.SendFileAsync(p, "");
                        File.Delete(p);
                    }
                    else if (content == "zrank")
                    {
                        long minhaPos = EconomyHelper.GetPosicaoRank(guildId, user.Id);
                        long meuTotal = EconomyHelper.GetSaldo(guildId, user.Id) + EconomyHelper.GetBanco(guildId, user.Id);
                        var p = await EconomyImageHelper.GerarImagemRank(user.Guild, EconomyHelper.GetTop10(guildId));
                        await msg.Channel.SendFileAsync(p, $"Você tem **{EconomyHelper.FormatarSaldo(meuTotal)}** coins e está em **#{minhaPos}**");
                        File.Delete(p);
                    }
                    else if (content.StartsWith("zpay"))
                    {
                        string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                        if (partes.Length < 3)
                        {
                            await msg.Channel.SendMessageAsync("❓ Uso: `zpay @usuario [valor]`");
                            return;
                        }

                        var mencionado = msg.MentionedUsers.FirstOrDefault();
                        if (mencionado == null || mencionado.IsBot)
                        {
                            await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Mencione um usuário válido.");
                            return;
                        }

                        if (mencionado.Id == user.Id)
                        {
                            await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você não pode transferir para si mesmo.");
                            return;
                        }

                        long saldoDoador = EconomyHelper.GetBanco(guildId, user.Id);
                        long valorTransferencia = 0;
                        string vTxt = partes[2].ToLower();

                        if (vTxt == "all") { valorTransferencia = saldoDoador; }
                        else { valorTransferencia = EconomyHelper.ConverterLetraParaNumero(vTxt); }

                        if (valorTransferencia <= 0 || saldoDoador < valorTransferencia)
                        {
                            await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Saldo insuficiente.");
                            return;
                        }

                        EconomyHelper.RemoverBanco(guildId, user.Id, valorTransferencia);
                        EconomyHelper.AdicionarBanco(guildId, mencionado.Id, valorTransferencia);
                        EconomyHelper.RegistrarTransacao(guildId, user.Id, mencionado.Id, valorTransferencia, "TRANSFERENCIA_DIRETA");

                        await msg.Channel.SendMessageAsync($"✅ Transferência de **{EconomyHelper.FormatarSaldo(valorTransferencia)}** para {mencionado.Mention} concluída!");
                    }
                    else if (content.StartsWith("zdep"))
                    {
                        string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length < 2)
                        {
                            await msg.Channel.SendMessageAsync("❓ Uso: `zdep [valor]` ou `zdep all`");
                            return;
                        }

                        long carteira = EconomyHelper.GetSaldo(guildId, user.Id);
                        string valTxt = p[1].ToLower();
                        long valor = EconomyHelper.ConverterLetraParaNumero(valTxt);

                        if (valor <= 0 || carteira < valor)
                        {
                            await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Saldo insuficiente.");
                            return;
                        }

                        EconomyHelper.RemoverSaldo(guildId, user.Id, valor);
                        EconomyHelper.AdicionarBanco(guildId, user.Id, valor);
                        EconomyHelper.RegistrarTransacao(guildId, user.Id, user.Id, valor, "DEPOSITO");

                        await msg.Channel.SendMessageAsync($"✅ Depósito de **{valor}** concluído!");
                    }
                }
                catch { }
            });
            return Task.CompletedTask;
        }
    }
}
