using Discord;
using Discord.WebSocket;
using Discord.Rest;
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

            // Primeiro garante que a tabela base existe
            cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS economy_users (
            guild_id TEXT,
            user_id TEXT,
            saldo BIGINT DEFAULT 0,
            ultimo_daily TIMESTAMP DEFAULT '2000-01-01',
            PRIMARY KEY (guild_id, user_id)
        );

        -- ESSAS LINHAS ABAIXO RESOLVEM O ERRO:
        -- Elas verificam se a coluna NÃO existe e adicionam ela na tabela antiga
        ALTER TABLE economy_users ADD COLUMN IF NOT EXISTS ultimo_semanal TIMESTAMP DEFAULT '2000-01-01';
        ALTER TABLE economy_users ADD COLUMN IF NOT EXISTS ultimo_mensal TIMESTAMP DEFAULT '2000-01-01';
    ";
            cmd.ExecuteNonQuery();
            Console.WriteLine("✅ [Banco] Tabelas e Colunas de Economia atualizadas.");
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

        public static DateTime GetUltimoTempo(ulong guildId, ulong userId, string coluna)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {coluna} FROM economy_users WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var result = cmd.ExecuteScalar();
            if (result == null || result == DBNull.Value) return DateTime.MinValue;
            return (DateTime)result;
        }

        public static void SetUltimoTempo(ulong guildId, ulong userId, string coluna)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO economy_users (guild_id, user_id, {coluna})
                VALUES (@gid, @uid, @data)
                ON CONFLICT (guild_id, user_id)
                DO UPDATE SET {coluna} = @data";
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
                new[] { new SkiaSharp.SKColor(80, 0, 80), new SkiaSharp.SKColor(50, 0, 50) },
                null,
                SkiaSharp.SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(new SkiaSharp.SKRect(2, 2, width - 2, height - 2), 20, 20, borderPaint);

            try
            {
                var avatarUrl = user.GetAvatarUrl(ImageFormat.Png, 128) ?? user.GetDefaultAvatarUrl();
                using var httpClient = new HttpClient();
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
                        Color = new SkiaSharp.SKColor(80, 0, 80, 80),
                        Style = SkiaSharp.SKPaintStyle.Stroke,
                        StrokeWidth = 6,
                        IsAntialias = true,
                        MaskFilter = SkiaSharp.SKMaskFilter.CreateBlur(SkiaSharp.SKBlurStyle.Normal, 3)
                    };
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

            var bgPaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(20, 10, 30), IsAntialias = true };
            canvas.DrawRect(new SkiaSharp.SKRect(0, 0, width, height), bgPaint);

            using var httpClient = new HttpClient();
            for (int i = 0; i < topUsers.Count; i++)
            {
                var userData = topUsers[i];
                IUser member = guild.GetUser(userData.UserId);
                if (member == null) member = await ((IGuild)guild).GetUserAsync(userData.UserId);

                string username = member?.Username ?? "Desconhecido";
                int col = i % 2; int row = i / 2;
                int x = 40 + (col * 400); int y = 120 + (row * 105);

                var cardPaint = new SkiaSharp.SKPaint { Color = i < 3 ? new SkiaSharp.SKColor(255, 180, 0) : new SkiaSharp.SKColor(80, 0, 80), IsAntialias = true };
                canvas.DrawRoundRect(new SkiaSharp.SKRect(x, y, x + 370, y + 85), 42, 42, cardPaint);

                var namePaint = new SkiaSharp.SKPaint { Color = i < 3 ? SkiaSharp.SKColors.Black : SkiaSharp.SKColors.White, TextSize = 22, Typeface = fontBold, IsAntialias = true };
                canvas.DrawText(username, x + 90, y + 50, namePaint);
            }

            var path = Path.Combine(Path.GetTempPath(), $"rank_{guild.Id}_{DateTime.Now.Ticks}.png");
            using var image = surface.Snapshot();
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);
            return path;
        }

        private static void DrawItem(SkiaSharp.SKCanvas canvas, float x, float y, string label, string valor, SkiaSharp.SKTypeface fontBold, SkiaSharp.SKTypeface fontNormal, SkiaSharp.SKColor accentColor, int width)
        {
            var itemBg = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(38, 38, 48), IsAntialias = true };
            canvas.DrawRoundRect(new SkiaSharp.SKRect(x, y, width - 40, y + 40), 10, 10, itemBg);
            canvas.DrawText(label, x + 45, y + 17, new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, TextSize = 15, IsAntialias = true, Typeface = fontBold });
            canvas.DrawText(valor, x + 45, y + 34, new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(170, 170, 180), TextSize = 13, IsAntialias = true });
        }
    }

    public class EconomyHandler
    {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();

        public EconomyHandler(DiscordSocketClient client) { _client = client; _client.MessageReceived += HandleMessage; }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage) return;
                    var user = msg.Author as SocketGuildUser; if (user == null) return;
                    var content = msg.Content.ToLower().Trim();
                    var guildId = user.Guild.Id;

                    string[] comandos = { "zhelp", "zsaldo", "zdaily", "zdiario", "zsemanal", "zmensal", "zpay", "zrank", "ztop coins" };
                    if (!comandos.Any(c => content == c || content.StartsWith(c + " "))) return;

                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 5) return;
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    // --- RECOMPENSAS ---
                    if (content == "zdaily" || content == "zdiario")
                    {
                        await ExecutarRecompensa(msg, user, guildId, "ultimo_daily", 24, 167000, 180000, "Diário");
                    }
                    else if (content == "zsemanal")
                    {
                        await ExecutarRecompensa(msg, user, guildId, "ultimo_semanal", 168, 220000, 450000, "Semanal");
                    }
                    else if (content == "zmensal")
                    {
                        await ExecutarRecompensa(msg, user, guildId, "ultimo_mensal", 720, 100000, 550000, "Mensal");
                    }
                    // --- SALDO E RANK ---
                    else if (content == "zsaldo")
                    {
                        var saldo = EconomyHelper.GetSaldo(guildId, user.Id);
                        var path = await EconomyImageHelper.GerarImagemSaldo(user, saldo);
                        await msg.Channel.SendFileAsync(path, ""); File.Delete(path);
                    }
                    else if (content == "zrank" || content == "ztop coins")
                    {
                        var top = EconomyHelper.GetTop10(guildId);
                        if (top.Count == 0) { await msg.Channel.SendMessageAsync("Ranking vazio."); return; }
                        var path = await EconomyImageHelper.GerarImagemRank(user.Guild, top);
                        await msg.Channel.SendFileAsync(path, "🏆 **Top Ricos do Servidor**"); File.Delete(path);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[Eco] {ex.Message}"); }
            });
            return Task.CompletedTask;
        }

        private async Task ExecutarRecompensa(SocketMessage msg, SocketGuildUser user, ulong guildId, string coluna, int horas, int min, int max, string nome)
        {
            var ultimo = EconomyHelper.GetUltimoTempo(guildId, user.Id, coluna);
            var resto = DateTime.UtcNow - ultimo;
            if (resto.TotalHours < horas)
            {
                var faltam = TimeSpan.FromHours(horas) - resto;
                await msg.Channel.SendMessageAsync($"❌ {user.Mention}, você já coletou seu {nome}! Volte em `{faltam.Days}d {faltam.Hours}h {faltam.Minutes}m`.");
                return;
            }
            long ganho = new Random().Next(min, max + 1);
            EconomyHelper.AdicionarSaldo(guildId, user.Id, ganho);
            EconomyHelper.SetUltimoTempo(guildId, user.Id, coluna);
            await msg.Channel.SendMessageAsync($"✅ {user.Mention}, você coletou seu **{nome}** e ganhou `{EconomyHelper.FormatarSaldo(ganho)}` cpoints!");
        }
    }
}
