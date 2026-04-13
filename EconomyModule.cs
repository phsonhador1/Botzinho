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
    // --- PARTE 1: BANCO DE DADOS E LÓGICA ---
    public static class EconomyHelper
    {
        public static string GetConnectionString() => Environment.GetEnvironmentVariable("DATABASE_URL") ?? throw new Exception("DATABASE_URL nao configurado!");

        public static readonly HashSet<ulong> IDsAutorizados = new() { 1472642376970404002 };

        public static void InicializarTabelas()
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS economy_users (
                    guild_id TEXT, user_id TEXT, saldo BIGINT DEFAULT 0,
                    banco BIGINT DEFAULT 0,
                    ultimo_daily TIMESTAMP DEFAULT '2000-01-01',
                    PRIMARY KEY (guild_id, user_id));";
                cmd.ExecuteNonQuery();
            }
            string[] updates = {
                "ALTER TABLE economy_users ADD COLUMN IF NOT EXISTS banco BIGINT DEFAULT 0;",
                "ALTER TABLE economy_users ADD COLUMN IF NOT EXISTS ultimo_semanal TIMESTAMP DEFAULT '2000-01-01';",
                "ALTER TABLE economy_users ADD COLUMN IF NOT EXISTS ultimo_mensal TIMESTAMP DEFAULT '2000-01-01';"
            };
            foreach (var sql in updates)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
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

        public static long GetBanco(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT banco FROM economy_users WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var result = cmd.ExecuteScalar();
            return result != null ? (long)result : 0;
        }

        public static void AdicionarSaldo(ulong guildId, ulong userId, long valor)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO economy_users (guild_id, user_id, saldo) VALUES (@gid, @uid, @valor)
                                ON CONFLICT (guild_id, user_id) DO UPDATE SET saldo = economy_users.saldo + @valor";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@valor", valor);
            cmd.ExecuteNonQuery();
        }

        public static bool RemoverSaldo(ulong guildId, ulong userId, long valor)
        {
            var saldoAtual = GetSaldo(guildId, userId);
            if (saldoAtual < valor) return false;
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE economy_users SET saldo = saldo - @valor WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@valor", valor);
            cmd.ExecuteNonQuery();
            return true;
        }

        public static bool DepositarTudo(ulong guildId, ulong userId)
        {
            long naCarteira = GetSaldo(guildId, userId);
            if (naCarteira <= 0) return false;
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE economy_users SET banco = banco + saldo, saldo = 0 WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            return cmd.ExecuteNonQuery() > 0;
        }

        public static string FormatarSaldo(long valor) => valor >= 1000000 ? $"{valor / 1000000.0:F2}M" : valor >= 1000 ? $"{valor / 1000.0:F2}K" : valor.ToString();

        public static List<(ulong UserId, long Total)> GetTop10(ulong guildId)
        {
            var list = new List<(ulong, long)>();
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT user_id, (saldo + banco) as total FROM economy_users WHERE guild_id = @gid AND (saldo + banco) > 0 ORDER BY total DESC LIMIT 10";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add((ulong.Parse(reader.GetString(0)), reader.GetInt64(1)));
            return list;
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
            return (result == null || result == DBNull.Value) ? DateTime.Parse("2000-01-01") : (DateTime)result;
        }

        public static void SetUltimoTempo(ulong guildId, ulong userId, string coluna)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE economy_users SET {coluna} = @data WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@data", DateTime.UtcNow);
            cmd.ExecuteNonQuery();
        }
    }

    // --- PARTE 2: GERAÇÃO DE IMAGENS (SKIA) ---
    public static class EconomyImageHelper
    {
        public static async Task<string> GerarImagemSaldo(SocketUser user, long wallet, long bank)
        {
            int width = 500; int height = 600;
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            var cardRect = new SKRect(20, 20, width - 20, height - 20);
            canvas.DrawRoundRect(cardRect, 30, 30, new SKPaint { Color = SKColors.White, IsAntialias = true });

            using var http = new HttpClient();
            try {
                var bytes = await http.GetByteArrayAsync(user.GetAvatarUrl(ImageFormat.Png, 256) ?? user.GetDefaultAvatarUrl());
                using var bmp = SKBitmap.Decode(bytes);
                var avRect = new SKRect(width/2-100, 50, width/2+100, 250);
                var path = new SKPath(); path.AddOval(avRect);
                canvas.Save(); canvas.ClipPath(path, antialias: true);
                canvas.DrawBitmap(bmp, avRect); canvas.Restore();
            } catch { }

            var bold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
            canvas.DrawText(user.Username, width/2, 310, new SKPaint { Color = SKColors.Black, TextSize = 35, Typeface = bold, TextAlign = SKTextAlign.Center, IsAntialias = true });

            DrawPill(canvas, 60, 350, "Carteira", wallet, new SKColor(160, 80, 220));
            DrawPill(canvas, 60, 430, "Banco", bank, new SKColor(160, 80, 220));
            DrawPill(canvas, 60, 510, "Total", wallet + bank, new SKColor(230, 180, 60));

            var pathImg = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(pathImg)) data.SaveTo(stream);
            return pathImg;
        }

        private static void DrawPill(SKCanvas canvas, float x, float y, string label, long valor, SKColor color) {
            var rect = new SKRect(x, y, 440, y + 65);
            canvas.DrawRoundRect(rect, 32, 32, new SKPaint { Color = new SKColor(245, 245, 245), IsAntialias = true });
            canvas.DrawRoundRect(new SKRect(x, y, x + 70, y + 65), 32, 32, new SKPaint { Color = color, IsAntialias = true });
            var bold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
            canvas.DrawText(label, x + 85, y + 30, new SKPaint { Color = SKColors.Black, TextSize = 22, Typeface = bold, IsAntialias = true });
            canvas.DrawText(EconomyHelper.FormatarSaldo(valor), x + 85, y + 55, new SKPaint { Color = SKColors.Gray, TextSize = 20, IsAntialias = true });
        }

        public static async Task<string> GerarImagemRank(SocketGuild guild, List<(ulong UserId, long Total)> topUsers)
        {
            int width = 850; int height = 680;
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            var fontBold = SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold) ?? SKTypeface.Default;
            canvas.Clear(new SKColor(20, 10, 30));
            canvas.DrawText("Top Coins do Servidor", 40, 80, new SKPaint { Color = SKColors.White, TextSize = 45, Typeface = fontBold, IsAntialias = true });

            using var httpClient = new HttpClient();
            for (int i = 0; i < topUsers.Count; i++) {
                var userData = topUsers[i];
                IUser member = guild.GetUser(userData.UserId);
                if (member == null) { try { member = await ((IGuild)guild).GetUserAsync(userData.UserId); } catch { } }

                int col = i % 2; int row = i / 2;
                int x = 40 + (col * 405); int y = 120 + (row * 105);
                var pColor = (i == 0) ? new SKColor(255, 215, 0) : new SKColor(80, 0, 80);
                canvas.DrawRoundRect(new SKRect(x, y, x + 380, y + 90), 45, 45, new SKPaint { Color = pColor, IsAntialias = true });
                
                try {
                    var avBytes = await httpClient.GetByteArrayAsync(member?.GetAvatarUrl(ImageFormat.Png, 128) ?? member?.GetDefaultAvatarUrl());
                    using var bmp = SKBitmap.Decode(avBytes);
                    var r = new SKRect(x + 15, y + 15, x + 75, y + 75);
                    var p = new SKPath(); p.AddOval(r);
                    canvas.Save(); canvas.ClipPath(p, antialias: true);
                    canvas.DrawBitmap(bmp, r); canvas.Restore();
                } catch { }

                canvas.DrawText(member?.Username ?? "Desconhecido", x + 90, y + 55, new SKPaint { Color = SKColors.White, TextSize = 22, Typeface = fontBold, IsAntialias = true });
                canvas.DrawText(EconomyHelper.FormatarSaldo(userData.Total), x + 360, y + 55, new SKPaint { Color = SKColors.White, TextSize = 20, TextAlign = SKTextAlign.Right, IsAntialias = true });
            }
            var path = Path.Combine(Path.GetTempPath(), $"rank_{guild.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(path)) data.SaveTo(stream);
            return path;
        }
    }

    // --- PARTE 3: COMANDOS (HANDLER) ---
    public class EconomyHandler
    {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();

        public EconomyHandler(DiscordSocketClient client) { _client = client; _client.MessageReceived += HandleMessage; }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () => {
                try {
                    if (msg.Author.IsBot || msg is not SocketUserMessage) return;
                    var user = msg.Author as SocketGuildUser; if (user == null) return;
                    var content = msg.Content.ToLower().Trim();
                    var guildId = user.Guild.Id;

                    string[] cmds = { "zsaldo", "zdaily", "zrank", "zpay", "zdep", "zaddsaldo" };
                    if (!cmds.Any(c => content.StartsWith(c))) return;
                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 2) return;
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    if (content == "zdaily") {
                        long ganho = new Random().Next(167000, 180001);
                        EconomyHelper.AdicionarSaldo(guildId, user.Id, ganho);
                        await msg.Channel.SendMessageAsync($"✅ {user.Mention}, `{EconomyHelper.FormatarSaldo(ganho)}` cpoints no **Diário**!");
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
                            long v = long.Parse(content.Split(' ').Last().Replace("k","000").Replace("m","000000"));
                            EconomyHelper.AdicionarSaldo(guildId, alvo.Id, v);
                            await msg.Channel.SendMessageAsync($"✅ Adicionado `{EconomyHelper.FormatarSaldo(v)}` para {alvo.Mention}.");
                        }
                    }
                    else if (content.StartsWith("zpay")) {
                        var alvo = msg.MentionedUsers.FirstOrDefault();
                        if (alvo != null && alvo.Id != user.Id && !alvo.IsBot) {
                            long v = long.Parse(content.Split(' ').Last().Replace("k","000").Replace("m","000000"));
                            if (EconomyHelper.RemoverSaldo(guildId, user.Id, v)) {
                                EconomyHelper.AdicionarSaldo(guildId, alvo.Id, v);
                                await msg.Channel.SendMessageAsync($"✅ {user.Mention} enviou `{EconomyHelper.FormatarSaldo(v)}` para {alvo.Mention}.");
                            }
                        }
                    }
                } catch { }
            });
            return Task.CompletedTask;
        }
    }
}
