using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Npgsql;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

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

    // --- 2. GERAÇÃO DE IMAGENS (SKIA DESIGN REFINADO PREMIUM) ---
    public static class EconomyImageHelper
    {
        private static readonly SKColor PurpleTheme = new SKColor(160, 80, 220); // Roxo Zoe
        private static readonly SKColor GoldTheme = new SKColor(255, 180, 0);    // Dourado para o Total
        private static readonly SKColor DarkBg = new SKColor(10, 8, 18);         // Fundo escuro
        private static readonly SKColor CardBg = new SKColor(22, 18, 35);        // Fundo do cartão central

        // --- IMAGEM DO NOVO PERFIL (ESTILO PREMIUM MODERNO - REFERÊNCIA DA IMAGEM) ---
        public static async Task<string> GerarImagemPerfil(SocketUser user, long totalCoins, string bio = "bot roubado", int nivel = 5, int xpAtual = 464, int xpTotal = 600, int reps = 0, string amigo = "lopes")
        {
            int width = 900;
            int height = 560;

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;

            // 0. Fundo Branco Base (canto da imagem do Discord)
            canvas.Clear(new SKColor(245, 243, 248));

            // 1. BANNER SUPERIOR - Nuvens roxas (gradiente ceú + nuvens estilizadas)
            var bannerRect = new SKRect(0, 0, width, 240);

            // Gradiente do céu (roxo escuro em cima -> rosa embaixo)
            using var skyPaint = new SKPaint();
            skyPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, 240),
                new[] {
                    new SKColor(130, 90, 180),  // Roxo topo
                    new SKColor(180, 120, 200), // Roxo-rosa meio
                    new SKColor(240, 180, 200)  // Rosa claro base
                },
                new[] { 0f, 0.55f, 1f },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(bannerRect, skyPaint);

            // Desenhar nuvens estilizadas (elipses sobrepostas para efeito fofinho)
            DrawCloud(canvas, 80, 60, 120, new SKColor(210, 160, 220, 200));
            DrawCloud(canvas, 260, 40, 100, new SKColor(180, 130, 200, 220));
            DrawCloud(canvas, 420, 80, 140, new SKColor(220, 170, 230, 190));
            DrawCloud(canvas, 600, 50, 110, new SKColor(170, 120, 195, 210));
            DrawCloud(canvas, 150, 140, 90, new SKColor(140, 90, 180, 230));
            DrawCloud(canvas, 350, 160, 130, new SKColor(160, 100, 190, 220));
            DrawCloud(canvas, 540, 170, 100, new SKColor(130, 80, 170, 240));
            DrawCloud(canvas, 720, 130, 120, new SKColor(180, 130, 205, 210));

            // 2. AVATAR GIGANTE (Direita) - Circular com borda branca
            float avX = 700;
            float avY = 260;
            float avRadius = 110;
            var avRect = new SKRect(avX - avRadius, avY - avRadius, avX + avRadius, avY + avRadius);

            // Sombra sutil atrás do avatar
            using var avShadow = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 40),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8)
            };
            canvas.DrawCircle(avX, avY + 4, avRadius, avShadow);

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
                canvas.DrawOval(avRect, new SKPaint { Color = new SKColor(80, 80, 80), IsAntialias = true });
            }

            // Borda branca grossa do avatar (estilo da imagem)
            using var avBorder = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 8,
                Color = SKColors.White,
                IsAntialias = true
            };
            canvas.DrawOval(avRect, avBorder);

            // 3. PÍLULA DO NOME (Cinza claro abaixo do avatar)
            var fontBold = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);
            var fontReg = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Normal);

            string displayName = (user as SocketGuildUser)?.Nickname ?? user.GlobalName ?? user.Username;
            if (displayName.Length > 18) displayName = displayName.Substring(0, 16) + "..";

            using var namePaint = new SKPaint
            {
                Color = new SKColor(55, 55, 65),
                TextSize = 28,
                Typeface = fontBold,
                TextAlign = SKTextAlign.Center,
                IsAntialias = true
            };

            float nameWidth = namePaint.MeasureText(displayName);
            float pillW = Math.Max(nameWidth + 70, 180);
            float pillH = 50;
            float pillY = 400;
            var pillRect = new SKRect(avX - pillW / 2, pillY, avX + pillW / 2, pillY + pillH);

            using var pillBg = new SKPaint { Color = new SKColor(225, 222, 232), IsAntialias = true };
            canvas.DrawRoundRect(pillRect, pillH / 2, pillH / 2, pillBg);
            canvas.DrawText(displayName, avX, pillY + 34, namePaint);

            // 4. CARD PRINCIPAL À ESQUERDA (Container cinza claro com bio e pílulas)
            float cardX = 40;
            float cardY = 200;
            float cardW = 560;
            float cardH = 320;
            var mainCardRect = new SKRect(cardX, cardY, cardX + cardW, cardY + cardH);

            // Sombra leve
            using var cardShadow = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 25),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12)
            };
            canvas.DrawRoundRect(mainCardRect, 22, 22, cardShadow);

            using var cardPaint = new SKPaint { Color = new SKColor(235, 232, 240), IsAntialias = true };
            canvas.DrawRoundRect(mainCardRect, 22, 22, cardPaint);

            // 4.1 BIO (texto pequeno no topo do card)
            using var bioPaint = new SKPaint
            {
                Color = new SKColor(35, 35, 45),
                TextSize = 20,
                Typeface = fontBold,
                IsAntialias = true
            };
            canvas.DrawText(bio, cardX + 25, cardY + 40, bioPaint);

            // 5. PÍLULAS DE INFORMAÇÕES (Grid 2x2 + 1 centralizada embaixo)
            float pilStartX = cardX + 25;
            float pilStartY = cardY + 65;
            float pilW = 250;
            float pilH = 70;
            float gapX = 15;
            float gapY = 15;

            // Linha 1 - Coins | Nível
            DrawProfilePill(canvas, pilStartX, pilStartY, pilW, pilH,
                "💰", EconomyHelper.FormatarSaldo(totalCoins), "Coins",
                PurpleTheme, fontBold, fontReg);

            DrawProfilePill(canvas, pilStartX + pilW + gapX, pilStartY, pilW, pilH,
                "⭐", $"Nível: {nivel}", $"{xpAtual}/{xpTotal} XP",
                PurpleTheme, fontBold, fontReg);

            // Linha 2 - Badges | Reps
            DrawProfilePill(canvas, pilStartX, pilStartY + pilH + gapY, pilW, pilH,
                "🏅", "Badges", "",
                PurpleTheme, fontBold, fontReg);

            DrawProfilePill(canvas, pilStartX + pilW + gapX, pilStartY + pilH + gapY, pilW, pilH,
                "👍", "Reps", reps.ToString(),
                PurpleTheme, fontBold, fontReg);

            // Linha 3 - Amigo(a) centralizado à esquerda
            DrawProfilePill(canvas, pilStartX, pilStartY + (pilH + gapY) * 2, pilW, pilH,
                "🎀", "Amigo(a)", amigo,
                PurpleTheme, fontBold, fontReg);

            // Salvar
            var p = Path.Combine(Path.GetTempPath(), $"perfil_{user.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(p)) data.SaveTo(str);

            return p;
        }

        // Desenha uma nuvem estilizada (círculos sobrepostos)
        private static void DrawCloud(SKCanvas canvas, float cx, float cy, float size, SKColor color)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            float r = size / 3;
            canvas.DrawCircle(cx, cy, r, paint);
            canvas.DrawCircle(cx + r * 0.9f, cy + r * 0.2f, r * 0.85f, paint);
            canvas.DrawCircle(cx - r * 0.9f, cy + r * 0.2f, r * 0.85f, paint);
            canvas.DrawCircle(cx + r * 0.4f, cy - r * 0.4f, r * 0.7f, paint);
            canvas.DrawCircle(cx - r * 0.4f, cy - r * 0.3f, r * 0.75f, paint);
            canvas.DrawCircle(cx + r * 1.5f, cy + r * 0.4f, r * 0.6f, paint);
        }

        // Desenha uma pílula de perfil (círculo com ícone à esquerda + textos à direita)
        private static void DrawProfilePill(SKCanvas canvas, float x, float y, float w, float h,
            string emoji, string title, string subtitle, SKColor accent, SKTypeface fontBold, SKTypeface fontReg)
        {
            var rect = new SKRect(x, y, x + w, y + h);

            // Fundo branco da pílula
            using var bg = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawRoundRect(rect, h / 2, h / 2, bg);

            // Círculo roxo com emoji/ícone
            float circleR = (h / 2) - 8;
            float circleX = x + h / 2;
            float circleY = y + h / 2;

            using var circleBg = new SKPaint { Color = accent, IsAntialias = true };
            canvas.DrawCircle(circleX, circleY, circleR, circleBg);

            // Emoji dentro do círculo (usando símbolo simples que funciona bem)
            using var iconPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 28,
                Typeface = fontBold,
                TextAlign = SKTextAlign.Center,
                IsAntialias = true
            };
            canvas.DrawText(emoji, circleX, circleY + 10, iconPaint);

            // Textos à direita do círculo
            float textX = x + h + 10;

            using var titlePaint = new SKPaint
            {
                Color = new SKColor(30, 30, 40),
                TextSize = 20,
                Typeface = fontBold,
                IsAntialias = true
            };

            using var subPaint = new SKPaint
            {
                Color = new SKColor(90, 90, 105),
                TextSize = 15,
                Typeface = fontReg,
                IsAntialias = true
            };

            if (!string.IsNullOrEmpty(subtitle))
            {
                canvas.DrawText(title, textX, y + 30, titlePaint);
                canvas.DrawText(subtitle, textX, y + 52, subPaint);
            }
            else
            {
                // Só um texto (Badges por exemplo) - centralizar vertical
                canvas.DrawText(title, textX, y + h / 2 + 7, titlePaint);
            }
        }

        // --- IMAGEM DO SALDO (ZSALDO EM PÍLULAS IGUAL A SUA FOTO) ---
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
              new[] { new SKColor(25, 20, 45), DarkBg },
              new[] { 0f, 1f },
              SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, width, height, bgPaint);

            var cardRect = new SKRect(25, 25, width - 25, height - 25);
            using var shadowPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 150),
                ImageFilter = SKImageFilter.CreateDropShadow(0, 10, 20, 20, new SKColor(0, 0, 0, 200)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(cardRect, 40, 40, shadowPaint);

            using var cardPaint = new SKPaint { Color = CardBg, IsAntialias = true };
            canvas.DrawRoundRect(cardRect, 40, 40, cardPaint);

            using var highlightPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                Color = new SKColor(255, 255, 255, 25),
                IsAntialias = true
            };
            canvas.DrawRoundRect(cardRect, 40, 40, highlightPaint);

            // Avatar Gigante
            float avY = 160;
            float avRadius = 95;
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
            catch
            {
                canvas.DrawOval(avRect, new SKPaint { Color = new SKColor(40, 40, 40), IsAntialias = true });
            }

            using var ringPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 6,
                Color = PurpleTheme,
                IsAntialias = true
            };
            canvas.DrawOval(avRect, ringPaint);

            // Apelido Inteligente em Pílula
            string displayName = (user as SocketGuildUser)?.Nickname ?? user.GlobalName ?? user.Username;
            var fontBold = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);
            using var namePaint = new SKPaint { Color = SKColors.White, TextSize = 34, Typeface = fontBold, TextAlign = SKTextAlign.Center, IsAntialias = true };

            float nameWidth = namePaint.MeasureText(displayName);
            float pillW = Math.Max(nameWidth + 80, 220);
            float pillH = 55;
            float pillY = 280;
            var pillRect = new SKRect((width / 2) - (pillW / 2), pillY, (width / 2) + (pillW / 2), pillY + pillH);

            using var nameBgPaint = new SKPaint { Color = new SKColor(255, 255, 255, 30), IsAntialias = true };
            canvas.DrawRoundRect(pillRect, pillH / 2, pillH / 2, nameBgPaint);
            canvas.DrawText(displayName, width / 2, pillY + 38, namePaint);

            // Painéis em Pílula
            float startY = 370;
            DrawModernPanel(canvas, "Carteira", wallet, width, startY, PurpleTheme, fontBold, "C");
            DrawModernPanel(canvas, "Banco", bank, width, startY + 95, PurpleTheme, fontBold, "B");
            DrawModernPanel(canvas, "Total", wallet + bank, width, startY + 190, GoldTheme, fontBold, "T");

            var p = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(p)) data.SaveTo(str);

            return p;
        }

        private static void DrawModernPanel(SKCanvas canvas, string label, long valor, int totalWidth, float y, SKColor accent, SKTypeface font, string iconLetter)
        {
            float pWidth = totalWidth - 90;
            float pHeight = 80;
            float x = 45;

            var rect = new SKRect(x, y, x + pWidth, y + pHeight);

            using var panelBg = new SKPaint { Color = new SKColor(14, 12, 22), IsAntialias = true };
            canvas.DrawRoundRect(rect, pHeight / 2, pHeight / 2, panelBg);

            float circleRadius = (pHeight / 2) - 10;
            float circleX = x + pHeight / 2;
            float circleY = y + pHeight / 2;
            using var circleBg = new SKPaint { Color = accent, IsAntialias = true };
            canvas.DrawCircle(circleX, circleY, circleRadius, circleBg);

            using var iconPaint = new SKPaint { Color = SKColors.White, TextSize = 24, Typeface = font, TextAlign = SKTextAlign.Center, IsAntialias = true };
            canvas.DrawText(iconLetter, circleX, circleY + 8, iconPaint);

            float textX = x + pHeight + 15;

            using var labelPaint = new SKPaint { Color = SKColors.White, TextSize = 22, Typeface = font, IsAntialias = true };
            canvas.DrawText(label, textX, y + 32, labelPaint);

            string valorFormatado = EconomyHelper.FormatarSaldo(valor);
            string valorStr = $"{valor} ({valorFormatado})";

            using var valuePaint = new SKPaint { Color = new SKColor(180, 180, 200), TextSize = 18, Typeface = font, IsAntialias = true };
            canvas.DrawText(valorStr, textX, y + 60, valuePaint);
        }

        public static async Task<string> GerarImagemRank(SocketGuild guild, List<(ulong UserId, long Total)> top)
        {
            int w = 850; int h = 750;
            using var surface = SKSurface.Create(new SKImageInfo(w, h));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(12, 10, 20));

            var boldFont = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);

            var paintWhite = new SKPaint { Color = SKColors.White, TextSize = 48, Typeface = boldFont, IsAntialias = true };
            var paintPurple = new SKPaint { Color = PurpleTheme, TextSize = 48, Typeface = boldFont, IsAntialias = true };
            canvas.DrawText("Top", 40, 80, paintWhite);
            canvas.DrawText("Coins", 140, 80, paintPurple);

            using var http = new HttpClient();
            for (int i = 0; i < top.Count; i++)
            {
                IUser m = guild.GetUser(top[i].UserId) ?? await ((IGuild)guild).GetUserAsync(top[i].UserId);
                int col = i % 2; int row = i / 2;
                float x = 40 + (col * 405); float y = 120 + (row * 115);
                int pos = i + 1;

                SKColor pillColor = pos switch
                {
                    1 => new SKColor(255, 215, 0),
                    2 => new SKColor(192, 192, 192),
                    3 => new SKColor(205, 127, 50),
                    _ => new SKColor(35, 32, 55)
                };

                SKColor textColor = (pos <= 3) ? SKColors.Black : SKColors.White;

                var rect = new SKRect(x, y, x + 385, y + 100);
                canvas.DrawRoundRect(rect, 20, 20, new SKPaint { Color = pillColor, IsAntialias = true });

                try
                {
                    var bytes = await http.GetByteArrayAsync(m?.GetAvatarUrl() ?? m?.GetDefaultAvatarUrl());
                    using var bmp = SKBitmap.Decode(bytes);
                    var avRect = new SKRect(x + 15, y + 15, x + 85, y + 85);
                    var path = new SKPath(); path.AddOval(avRect);
                    canvas.Save(); canvas.ClipPath(path, SKClipOperation.Intersect, true);
                    canvas.DrawBitmap(bmp, avRect); canvas.Restore();
                    canvas.DrawOval(avRect, new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = textColor, IsAntialias = true });
                }
                catch { }

                string name = (m?.Username ?? "Usuário").Length > 12 ? (m?.Username ?? "Usuário").Substring(0, 10) + ".." : (m?.Username ?? "Usuário");
                canvas.DrawText($"{pos}. {name}", x + 100, y + 50, new SKPaint { Color = textColor, TextSize = 22, Typeface = boldFont, IsAntialias = true });
                canvas.DrawText(EconomyHelper.FormatarSaldo(top[i].Total), x + 100, y + 80, new SKPaint { Color = (pos <= 3) ? new SKColor(40, 40, 40) : new SKColor(180, 180, 200), TextSize = 18, IsAntialias = true });
            }

            var pathImg = Path.Combine(Path.GetTempPath(), $"rank_{guild.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot()) using (var d = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var s = File.OpenWrite(pathImg)) d.SaveTo(s);
            return pathImg;
        }
    }

    // --- 3. ECONOMY HANDLER ---
    public class EconomyHandler
    {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();
        private static readonly Dictionary<ulong, DateTime> _stealCooldowns = new();

        public EconomyHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;

            // ADICIONADO: Necessário para o botão de lembrete não dar erro de Interação Falhou
            _client.ButtonExecuted += HandleButtonAsync;

            // INICIANDO O VIGILANTE DE LEMBRETES
            _ = Task.Run(() => VigilanteLembretes());
        }

        // NOVO: Loop que checa o banco e envia DM
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
                            catch { /* DM fechada */ }

                            EconomyHelper.RemoverLembrete(guildId, userId);
                        });
                    }
                }
                catch (Exception ex) { Console.WriteLine("Erro no Vigilante: " + ex.Message); }

                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }

        // ADICIONADO: Método que responde ao clique no botão e salva o lembrete
        private async Task HandleButtonAsync(SocketMessageComponent component)
        {
            if (component.Data.CustomId == "btn_lembrete_daily")
            {
                // Salva o lembrete para daqui a exatas 24 horas
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
                    var user = msg.Author as SocketGuildUser; var content = msg.Content.ToLower().Trim(); var guildId = user.Guild.Id;

                    // ADICIONADO ZPERFIL NA LISTA
                    string[] cmds = { "zsaldo", "zperfil", "zdaily", "zrank", "zpay", "zdep", "zaddsaldo", "zsetsaldo", "ztransacoes", "ztranscoes", "zroubar" };
                    if (!cmds.Any(c => content.StartsWith(c))) return;

                    // Cooldown de 2 segundos (Padrão para comandos de economia)
                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 3)
                    {
                        var aviso = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> {user.Mention}, calma ai viadinho abusado! Aguarde **3 segundos** para usar outro **comando**.");
                        _ = Task.Delay(3000).ContinueWith(_ => aviso.DeleteAsync());
                        return;
                    }
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    // --- ZPERFIL ADAPTADO (NOVO DESIGN MODERNO) ---
                    if (content.StartsWith("zperfil"))
                    {
                        var alvo = msg.MentionedUsers.FirstOrDefault() ?? user;
                        long total = EconomyHelper.GetSaldo(guildId, alvo.Id) + EconomyHelper.GetBanco(guildId, alvo.Id);

                        // Dados default do perfil (conforme imagem de referência)
                        string bio = "bot roubado";
                        int nivel = 5;
                        int xpAtual = 464;
                        int xpTotal = 600;
                        int reps = 0;
                        string amigo = "lopes";

                        var p = await EconomyImageHelper.GerarImagemPerfil(alvo, total, bio, nivel, xpAtual, xpTotal, reps, amigo);

                        var cb = new ComponentBuilder()
                          .WithButton("Alterar Bio", "btn_bio_dummy", ButtonStyle.Secondary, new Emoji("📝"))
                          .WithButton("Enviar reputação", "btn_rep_dummy", ButtonStyle.Secondary, new Emoji("🤝"));

                        await msg.Channel.SendFileAsync(p, components: cb.Build());
                        File.Delete(p);
                    }
                    // --- ZDAILY ATUALIZADO (PREMIUM) ---
                    else if (content == "zdaily")
                    {
                        // Trava de 24 horas
                        DateTime ultimoDaily = EconomyHelper.GetUltimoDaily(guildId, user.Id);
                        TimeSpan tempoPassado = DateTime.Now - ultimoDaily;

                        if (tempoPassado.TotalHours < 24)
                        {
                            TimeSpan tempoFalta = TimeSpan.FromHours(24) - tempoPassado;
                            await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, você já coletou seu bônus diário! Volte em `{tempoFalta.Hours}h e {tempoFalta.Minutes}m`.");
                            return;
                        }

                        long g = new Random().Next(167000, 180001);
                        int xpGanho = new Random().Next(15, 45); // XP visual para o Embed

                        EconomyHelper.AdicionarSaldo(guildId, user.Id, g);
                        EconomyHelper.AtualizarDaily(guildId, user.Id); // Salva que o usuário acabou de pegar
                        EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, user.Id, g, "DAILY");

                        // Criando o Embed Profissional
                        var eb = new EmbedBuilder()
              .WithColor(new Color(160, 80, 220)) // Roxo da Zoe
                            .WithTitle("<:calendario:1495171666844713173> Daily")
              .WithDescription($"Você coletou sua **recompensa diária** com sucesso!\n\n" +
                      $"<a:trofeu:1493063952060387479> **Recompensas:**\n" +

                      $"• <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(g)}** cpoints\n" +
                      $"• <:levelup:1495174376885063841> **+{xpGanho}XP**\n\n" +
                      $"<:seta:1493089125979656385> Você pode ver seu **saldo** utilizando o comando **zsaldo**.\n\n" +
                      $"<a:teste:1490570407307378712> Utilize o comando **zdep all** para depositar seus coins!")
              .WithThumbnailUrl("https://media.discordapp.net/attachments/1077714940745502750/1104440347586732082/tempo-e-dinheiro.png?width=460&height=460&ex=69e4feba&is=69e3ad3a&hm=46d03ad8e45a3857341c79bc40ff9243ca9241bd0dcc420ea713123a99104e68&");

                        // Criando o Botão de Lembrete
                        var cb = new ComponentBuilder()
              .WithButton("Definir lembrete", "btn_lembrete_daily", ButtonStyle.Secondary, Emote.Parse("<a:sino:1495172950767173833>"));

                        await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
                    }
                    else if (content == "zdep all")
                    {
                        long carteira = EconomyHelper.GetSaldo(guildId, user.Id);

                        if (carteira <= 0)
                        {
                            await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você não tem saldo na carteira para depositar.");
                            return;
                        }

                        if (EconomyHelper.DepositarTudo(guildId, user.Id))
                        {
                            // Logando o Depósito All
                            EconomyHelper.RegistrarTransacao(guildId, user.Id, user.Id, carteira, "DEPOSITO");

                            // RESPOSTA ADAPTADA AQUI
                            await ((SocketUserMessage)msg).ReplyAsync($"<a:sucess:1494692628372132013>  Seu deposito de **{carteira}** foi concluído com sucesso.");
                        }
                    }
                    else if (content.StartsWith("zdep"))
                    {
                        string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zdep (valor)` ou `zdep all`."); return; }
                        long carteira = EconomyHelper.GetSaldo(guildId, user.Id);
                        string valTxt = p[1].ToLower();
                        long valor = EconomyHelper.ConverterLetraParaNumero(valTxt);
                        if (valor <= 0 || carteira < valor) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Saldo insuficiente na carteira."); return; }
                        if (EconomyHelper.RemoverSaldo(guildId, user.Id, valor))
                        {
                            EconomyHelper.AdicionarBanco(guildId, user.Id, valor);
                            EconomyHelper.RegistrarTransacao(guildId, user.Id, user.Id, valor, "DEPOSITO");

                            // RESPOSTA ADAPTADA AQUI
                            await ((SocketUserMessage)msg).ReplyAsync($"<a:sucess:1494692628372132013> Seu depósito de **{valor}** foi concluído com sucesso.");
                        }
                    }
                    else if (content == "zsaldo")
                    {
                        var p = await EconomyImageHelper.GerarImagemSaldo(user, EconomyHelper.GetSaldo(guildId, user.Id), EconomyHelper.GetBanco(guildId, user.Id));
                        await msg.Channel.SendFileAsync(p, ""); File.Delete(p);
                    }
                    else if (content == "zrank")
                    {
                        long minhaPos = EconomyHelper.GetPosicaoRank(guildId, user.Id);
                        long meuTotal = EconomyHelper.GetSaldo(guildId, user.Id) + EconomyHelper.GetBanco(guildId, user.Id);
                        var p = await EconomyImageHelper.GerarImagemRank(user.Guild, EconomyHelper.GetTop10(guildId));
                        await msg.Channel.SendFileAsync(p, $"<a:trofeu:1493063952060387479> **Top Ricos Do Servidor**\n<:emoji_8:1491910148476899529> Você tem **{EconomyHelper.FormatarSaldo(meuTotal)}** coins e está em **#{minhaPos}**"); File.Delete(p);
                    }
                    else if (content.StartsWith("zaddsaldo") && EconomyHelper.IDsAutorizados.Contains(user.Id))
                    {
                        var alvo = msg.MentionedUsers.FirstOrDefault();
                        if (alvo != null)
                        {
                            string valTxt = content.Split(' ').Last().ToLower();
                            long v = EconomyHelper.ConverterLetraParaNumero(valTxt);
                            EconomyHelper.AdicionarSaldo(guildId, alvo.Id, v);
                            await msg.Channel.SendMessageAsync($"<a:lealdade:1493009439522033735> **Sucesso!** Foram adicionados `{EconomyHelper.FormatarSaldo(v)}` cpoints para <:pessoa:1493010183352483840> {alvo.Mention}.");
                        }
                    }
                    else if (content.StartsWith("zsetsaldo") && EconomyHelper.IDsAutorizados.Contains(user.Id))
                    {
                        var alvo = msg.MentionedUsers.FirstOrDefault();
                        string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                        if (alvo == null || partes.Length < 3)
                        {
                            await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zsetsaldo @usuario [novo_valor]`\n*Exemplo para zerar:* `zsetsaldo @Zoe 0`\n*Exemplo para definir:* `zsetsaldo @Zoe 10b`.");
                            return;
                        }

                        string valTxt = partes[2].ToLower();
                        long novoValor = EconomyHelper.ConverterLetraParaNumero(valTxt);

                        if (novoValor < 0) novoValor = 0;

                        EconomyHelper.SetSaldo(guildId, alvo.Id, novoValor);
                        EconomyHelper.RegistrarTransacao(guildId, user.Id, alvo.Id, novoValor, "SET_SALDO_TOTAL");

                        await msg.Channel.SendMessageAsync($"<a:sucess:1494692628372132013> O saldo de {alvo.Mention} foi redefinido para **{EconomyHelper.FormatarSaldo(novoValor)}**");
                    }
                    if (content.StartsWith("zpay"))
                    {
                        // Cooldown de 2 segundos (Padrão para comandos de economia)
                        if (_cooldowns.TryGetValue(user.Id, out var lastZpay) && (DateTime.UtcNow - lastZpay).TotalSeconds < 2)
                        {
                            var aviso = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> {user.Mention}, Da pra esperar Filho da Puta? Aguarde **2 segundos** para usar outro comando.");
                            _ = Task.Delay(2000).ContinueWith(_ => aviso.DeleteAsync());
                            return;
                        }
                        _cooldowns[user.Id] = DateTime.UtcNow;

                        string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                        // 1. Validação de Uso: zpay @user 5000
                        if (partes.Length < 3)
                        {
                            await msg.Channel.SendMessageAsync("❓ **Uso correto:** `zpay @usuario [valor]`\n*Exemplo: zpay @Zoe 10k*");
                            return;
                        }

                        // 2. Identificar o destinatário (Por menção)
                        var mencionado = msg.MentionedUsers.FirstOrDefault();
                        if (mencionado == null || mencionado.IsBot)
                        {
                            await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você precisa mencionar um usuário real para enviar cpoints.");
                            return;
                        }

                        if (mencionado.Id == user.Id)
                        {
                            await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Ae arrombado, ta querendo transferir cpoints para você mesmo?");
                            return;
                        }

                        // 3. Processar o Valor (all, k, m, b, t, número)
                        long saldoDoador = EconomyHelper.GetBanco(guildId, user.Id);
                        long valorTransferencia = 0;
                        string vTxt = partes[2].ToLower();

                        if (vTxt == "all") { valorTransferencia = saldoDoador; }
                        else
                        {
                            valorTransferencia = EconomyHelper.ConverterLetraParaNumero(vTxt);
                        }

                        // 4. Validações de Saldo
                        if (valorTransferencia <= 0)
                        {
                            await msg.Channel.SendMessageAsync("<:aviso:1493365148323152034> O valor da transferência deve ser maior que 0.");
                            return;
                        }

                        if (saldoDoador < valorTransferencia)
                        {
                            await msg.Channel.SendMessageAsync($"<:aviso:1493365148323152034> Você não tem `{EconomyHelper.FormatarSaldo(valorTransferencia)}` cpoints no banco para transferir.");
                            return;
                        }

                        // 5. EXECUTAR A TRANSFERÊNCIA (Lógica de Banco)
                        try
                        {
                            // Remove de quem envia
                            EconomyHelper.RemoverBanco(guildId, user.Id, valorTransferencia);
                            // Adiciona para quem recebe
                            EconomyHelper.AdicionarBanco(guildId, mencionado.Id, valorTransferencia);

                            // Registrar no Log de Transações (Importante!)
                            EconomyHelper.RegistrarTransacao(guildId, user.Id, mencionado.Id, valorTransferencia, "TRANSFERENCIA_DIRETA");

                            // --- MENSAGEM DE SUCESSO (SEM EMBED, IGUAL À IMAGEM) ---
                            // Aqui está a adaptação fiel ao exemplo
                            await msg.Channel.SendMessageAsync($"<a:lealdade:1493009439522033735> **Sucesso!** Foram transferidos `{EconomyHelper.FormatarSaldo(valorTransferencia)}` cpoints para <:pessoa:1493010183352483840> {mencionado.Mention}.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Erro zpay]: {ex.Message}");
                            await msg.Channel.SendMessageAsync("<:aviso:1493365148323152034> Ocorreu um erro interno ao processar a transferência.");
                        }
                    }

                    // --- COMANDO ZROUBAR (AGRESSIVO - FOCO NA CARTEIRA) ---
                    else if (content.StartsWith("zroubar"))
                    {
                        // 1. TIMEOUT DE 30 MINUTOS (Específico para Roubo em Embed)
                        if (_stealCooldowns.TryGetValue(user.Id, out var lastSteal) && (DateTime.UtcNow - lastSteal).TotalMinutes < 25)
                        {
                            var tempoRestante = 25 - (DateTime.UtcNow - lastSteal).TotalMinutes;
                            var ebCooldown = new EmbedBuilder()
                              .WithColor(new Color(255, 71, 87)) // Vermelho
                                              .WithDescription($"<a:negativo:1492950137587241114> {user.Mention}, Espere Filho da Puta! O cheiro de crime ainda está no ar. Aguarde `{tempoRestante:F0} minutos` para tentar roubar novamente.");

                            var aviso = await msg.Channel.SendMessageAsync(embed: ebCooldown.Build());
                            _ = Task.Delay(5000).ContinueWith(_ => aviso.DeleteAsync());
                            return;
                        }

                        string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                        // 2. Validação de Uso: zroubar @user
                        if (partes.Length < 2)
                        {
                            await msg.Channel.SendMessageAsync("❓ **Uso correto:** `zroubar @usuario`\n*Exemplo: zroubar @Zoe*");
                            return;
                        }

                        // 3. Identificar a vítima (Por menção)
                        var vitima = msg.MentionedUsers.FirstOrDefault();
                        if (vitima == null || vitima.IsBot)
                        {
                            await msg.Channel.SendMessageAsync("<:aviso:1493365148323152034> Você precisa mencionar um usuário real para tentar roubar.");
                            return;
                        }

                        if (vitima.Id == user.Id)
                        {
                            await msg.Channel.SendMessageAsync("<:aviso:1493365148323152034> Ae arrombado, ta querendo roubar de você mesmo? Para de graça e usa direito");
                            return;
                        }

                        // 4. VERIFICAÇÃO DE SALDO NA CARTEIRA DA VÍTIMA (Colona 'saldo')
                        long saldoCarteiraVitima = EconomyHelper.GetSaldo(guildId, vitima.Id);

                        if (saldoCarteiraVitima <= 0)
                        {
                            // --- ROUBO FRACASSADO (EMBED) ---
                            var ebFracasso = new EmbedBuilder()
                .WithColor(new Color(43, 45, 49)) // Cor escura/neutra
                                .WithAuthor("Assalto Fracassado", "https://cdn-icons-png.flaticon.com/512/4338/4338873.png")
                .WithDescription($"<:atencao:1493350891749642240> {user.Mention} tentou roubar {vitima.Mention}, mas ele estava duro kkk\n\n**Carteira vazia!** Nenhuma moeda foi levada.")
                .WithThumbnailUrl(vitima.GetAvatarUrl() ?? vitima.GetDefaultAvatarUrl());

                            await msg.Channel.SendMessageAsync(embed: ebFracasso.Build());

                            // Aplica o timeout mesmo se falhar (para não ficarem spamando)
                            _stealCooldowns[user.Id] = DateTime.UtcNow;
                            return;
                        }

                        // 5. EXECUTAR O ROUBO (Transferir METADE)
                        // Usamos integer division (long / 2) que arredonda para baixo automaticamente
                        long valorRoubado = saldoCarteiraVitima / 2;

                        try
                        {
                            // Remove da Carteira da Vítima (Coluna 'saldo')
                            if (EconomyHelper.RemoverSaldo(guildId, vitima.Id, valorRoubado))
                            {
                                // Adiciona à Carteira do Ladrão (Coluna 'saldo')
                                EconomyHelper.AdicionarSaldo(guildId, user.Id, valorRoubado);

                                // Registrar no Log de Transações (Essencial para segurança)
                                EconomyHelper.RegistrarTransacao(guildId, vitima.Id, user.Id, valorRoubado, "ROUBO_CARTEIRA_METADE");

                                // Aplica o timeout de 30 minutos agora que teve sucesso
                                _stealCooldowns[user.Id] = DateTime.UtcNow;

                                // --- ROUBO BEM SUCEDIDO (EMBED PREMIUM) ---
                                var ebSucesso = new EmbedBuilder()
                  .WithColor(new Color(255, 71, 87)) // Vermelho perigo
                                    .WithAuthor("Assalto Bem Sucedido!", "https://cdn-icons-png.flaticon.com/512/2569/2569198.png")
                  .WithDescription($"<:blackninja:1493348778705424464> **TEMOS UM LADRÃO AQUI NO SERVER!**\n\n" +
                          $"<:ladrao:1493349791340433479> {user.Mention} acaba de passar a mão em <:dinheiro:1493360319928733838> `{EconomyHelper.FormatarSaldo(valorRoubado)}` cpoints na carteira de {vitima.Mention}!")
                  .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

                                await msg.Channel.SendMessageAsync(embed: ebSucesso.Build());
                            }
                            else
                            {
                                await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Falha na execução do roubo. Tente novamente.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Erro zroubar]: {ex.Message}");
                            await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Ocorreu um erro interno ao tentar processar o crime.");
                        }
                    }

                    else if (content.StartsWith("ztransacoes") || content.StartsWith("ztranscoes"))
                    {
                        var usuarioAlvo = msg.MentionedUsers.FirstOrDefault() ?? user;
                        var transacoes = EconomyHelper.GetTransacoes(guildId, usuarioAlvo.Id);

                        var eb = new EmbedBuilder()
                          .WithAuthor($"Transações | {_client.CurrentUser.Username}", _client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                          .WithThumbnailUrl("https://cdn-icons-png.flaticon.com/512/2830/2830284.png")
                          .WithColor(new Color(43, 45, 49));

                        if (transacoes.Count == 0)
                        {
                            eb.WithDescription($"• Aqui estão as ultimas **0 transações** de `{usuarioAlvo.Username}`:\n\nNenhuma transação encontrada no momento.");
                        }
                        else
                        {
                            string listaTexto = $"• Aqui estão as ultimas **{transacoes.Count} transações** de `{usuarioAlvo.Username}`:\n\n";

                            foreach (var t in transacoes)
                            {
                                string dataFormatada = t.Date.AddHours(-3).ToString("dd/MM/yyyy, HH:mm:ss");
                                string formatAmount = EconomyHelper.FormatarSaldo(t.Amount);
                                string linha = "";

                                if (t.Type == "DEPOSITO")
                                {
                                    linha = $"🏦 Depositou **{formatAmount} coin(s)** da carteira para o banco.";
                                }
                                else if (t.Type == "DAILY")
                                {
                                    linha = $"🎁 ➕ Recebeu **{formatAmount} coin(s)** do bônus diário.";
                                }
                                else if (t.Type == "ROLETA_GANHO")
                                {
                                    linha = $"<a:ganhador:1493088070923452599> Ganhou **{formatAmount} coin(s)** na roleta.";
                                }
                                else if (t.Type == "ROLETA_PERDA")
                                {
                                    linha = $"🎡 <:erro:1493078898462949526> Perdeu **{formatAmount} coin(s)** na roleta.";
                                }
                                else if (t.Type == "COINFLIP_GANHO")
                                {
                                    linha = $"🪙 ➕ Ganhou **{formatAmount} coin(s)** no coinflip.";
                                }
                                else if (t.Type == "COINFLIP_PERDA")
                                {
                                    linha = $"🪙 <:erro:1493078898462949526> Perdeu **{formatAmount} coin(s)** no coinflip.";
                                }
                                else if (t.Type == "BLACKJACK_GANHO")
                                {
                                    linha = $"🃏 ➕ Ganhou **{formatAmount} coin(s)** no blackjack.";
                                }
                                else if (t.Type == "BLACKJACK_PERDA")
                                {
                                    linha = $"🃏 <:erro:1493078898462949526> Perdeu **{formatAmount} coin(s)** no blackjack.";
                                }
                                else if (t.Type == "BLACKJACK_EMPATE")
                                {
                                    linha = $"🃏 ⚖️ Recuperou **{formatAmount} coin(s)** (Empate no blackjack).";
                                }
                                else if (t.Type == "TRANSFERENCIA")
                                {
                                    if (t.SenderId == usuarioAlvo.Id.ToString())
                                    {
                                        linha = $"💸 ➖ Enviou **{formatAmount} coin(s)** para <@{t.ReceiverId}>.";
                                    }
                                    else
                                    {
                                        linha = $"💸 ➕ Recebeu **{formatAmount} coin(s)** de <@{t.SenderId}>.";
                                    }
                                }
                                else if (t.Type == "ROUBO_CARTEIRA_METADE")
                                {
                                    if (t.SenderId == usuarioAlvo.Id.ToString())
                                    {
                                        linha = $"🕵️‍♂️ <:erro:1493078898462949526> Foi roubado em **{formatAmount} coin(s)** por <@{t.ReceiverId}>.";
                                    }
                                    else
                                    {
                                        linha = $"🕵️‍♂️ ➕ Roubou **{formatAmount} coin(s)** de <@{t.SenderId}>.";
                                    }
                                }

                                listaTexto += $"• `[{dataFormatada}]` {linha}\n";
                            }
                            eb.WithDescription(listaTexto);
                        }

                        eb.WithFooter($"Página: 1/1 • Hoje às {DateTime.Now.AddHours(-3):HH:mm}");

                        var cb = new ComponentBuilder()
                          .WithButton(null, "extrato_back", ButtonStyle.Secondary, new Emoji("⬅️"), disabled: true)
                          .WithButton(null, "extrato_search", ButtonStyle.Secondary, new Emoji("🔍"), disabled: true)
                          .WithButton(null, "extrato_next", ButtonStyle.Secondary, new Emoji("➡️"), disabled: true);

                        await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
                    }
                }
                catch { }
            }); return Task.CompletedTask;
        }
    }
}
