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
        public static string GetConnectionString() => Environment.GetEnvironmentVariable("DATABASE_URL") ?? throw new Exception("DATABASE_URL nao configurado!");

        public static void InicializarTabelas()
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS economy_users (
                    guild_id TEXT, user_id TEXT, saldo BIGINT DEFAULT 0,
                    ultimo_daily TIMESTAMP DEFAULT '2000-01-01',
                    PRIMARY KEY (guild_id, user_id));";
                cmd.ExecuteNonQuery();
            }
            string[] updates = {
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

        public static string FormatarSaldo(long valor)
        {
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
            while (reader.Read()) list.Add((ulong.Parse(reader.GetString(0)), reader.GetInt64(1)));
            return list;
        }
    }

    public static class EconomyImageHelper
    {
        public static async Task<string> GerarImagemRank(SocketGuild guild, List<(ulong UserId, long Saldo)> topUsers)
        {
            int width = 850; int height = 680;
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height));
            var canvas = surface.Canvas;
            var fontBold = SkiaSharp.SKTypeface.FromFamilyName("DejaVu Sans", SkiaSharp.SKFontStyle.Bold) ?? SkiaSharp.SKTypeface.Default;

            canvas.Clear(new SkiaSharp.SKColor(20, 10, 30));
            var titleWhite = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, TextSize = 45, Typeface = fontBold, IsAntialias = true };
            var titleGold = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(255, 215, 0), TextSize = 45, Typeface = fontBold, IsAntialias = true };
            canvas.DrawText("Top", 40, 80, titleWhite);
            canvas.DrawText("Coins", 135, 80, titleGold);

            using var httpClient = new HttpClient();
            for (int i = 0; i < topUsers.Count; i++)
            {
                var userData = topUsers[i];
                IUser member = guild.GetUser(userData.UserId);
                if (member == null) { try { member = await ((IGuild)guild).GetUserAsync(userData.UserId); } catch { } }

                string username = member?.Username ?? "Desconhecido";
                if (username.Length > 12) username = username.Substring(0, 10) + "..";

                int col = i % 2; int row = i / 2;
                int x = 40 + (col * 405); int y = 120 + (row * 105);
                int rank = i + 1;

                SkiaSharp.SKColor pillColor = rank switch { 1 => new SkiaSharp.SKColor(255, 180, 0), 2 => new SkiaSharp.SKColor(220, 220, 230), 3 => new SkiaSharp.SKColor(255, 120, 0), _ => new SkiaSharp.SKColor(80, 0, 80) };
                var pillPaint = new SkiaSharp.SKPaint { Color = pillColor, IsAntialias = true };
                canvas.DrawRoundRect(new SkiaSharp.SKRect(x, y, x + 380, y + 90), 45, 45, pillPaint);

                try
                {
                    var url = member?.GetAvatarUrl(ImageFormat.Png, 128) ?? member?.GetDefaultAvatarUrl();
                    if (url != null)
                    {
                        var bytes = await httpClient.GetByteArrayAsync(url);
                        using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);
                        var rect = new SkiaSharp.SKRect(x + 12, y + 12, x + 78, y + 78);
                        var pathClip = new SkiaSharp.SKPath(); pathClip.AddOval(rect);
                        canvas.Save(); canvas.ClipPath(pathClip);
                        canvas.DrawBitmap(bitmap, rect); canvas.Restore();
                        var border = new SkiaSharp.SKPaint { Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 3, Color = i < 3 ? SkiaSharp.SKColors.Black : SkiaSharp.SKColors.White, IsAntialias = true };
                        canvas.DrawOval(x + 45f, y + 45f, 33f, 33f, border);
                    }
                }
                catch { }

                var textPaint = new SkiaSharp.SKPaint { Color = i < 3 ? SkiaSharp.SKColors.Black : SkiaSharp.SKColors.White, TextSize = 24, Typeface = fontBold, IsAntialias = true };
                canvas.DrawText(username, x + 95, y + 55, textPaint);
                canvas.DrawText(EconomyHelper.FormatarSaldo(userData.Saldo), x + 355, y + 55, new SkiaSharp.SKPaint { Color = textPaint.Color, TextSize = 22, TextAlign = SkiaSharp.SKTextAlign.Right, IsAntialias = true, Typeface = fontBold });
                canvas.DrawText(rank.ToString(), x + 5, y - 5, new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Gray, TextSize = 16, IsAntialias = true });
            }

            var path = Path.Combine(Path.GetTempPath(), $"rank_{guild.Id}_{DateTime.Now.Ticks}.png");
            using (var image = surface.Snapshot())
            using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(path)) data.SaveTo(stream);
            return path;
        }
    }

    public class EconomyHandler
    {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();

        public EconomyHandler(DiscordSocketClient client) { _client = client; _client.MessageReceived += HandleMessage; }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () => {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage) return;
                    var user = msg.Author as SocketGuildUser; if (user == null) return;
                    var content = msg.Content.ToLower().Trim();
                    var guildId = user.Guild.Id;

                    string[] cmds = { "zhelp", "zsaldo", "zdaily", "zdiario", "zsemanal", "zmensal", "zrank" };
                    if (!cmds.Any(c => content == c || content.StartsWith(c + " "))) return;
                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 3) return;
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    if (content == "zdaily" || content == "zdiario")
                        await ExecutarRecompensa(msg, user, guildId, "ultimo_daily", 24, 167000, 180000, "Diário");
                    else if (content == "zsemanal")
                        await ExecutarRecompensa(msg, user, guildId, "ultimo_semanal", 168, 220000, 450000, "Semanal");
                    else if (content == "zmensal")
                        await ExecutarRecompensa(msg, user, guildId, "ultimo_mensal", 720, 100000, 550000, "Mensal");
                    else if (content == "zrank")
                    {
                        await Task.Delay(3000);
                        // --- MENSAGEM DE CARREGAMENTO ---
                        var loading = await msg.Channel.SendMessageAsync("<a:carregandoportal:1492944498605686844> **Gerando o ranking, aguarde um instante...**");

                        var top = EconomyHelper.GetTop10(guildId);
                        if (top.Count == 0)
                        {
                            await loading.ModifyAsync(x => x.Content = "❌ O ranking está vazio.");
                            return;
                        }

                        var path = await EconomyImageHelper.GerarImagemRank(user.Guild, top);
                        await msg.Channel.SendFileAsync(path, "🏆 **Top Ricos do Servidor**");

                        try { await loading.DeleteAsync(); } catch { } // Deleta após enviar a imagem
                        File.Delete(path);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[Eco] {ex.Message}"); }
            });
            return Task.CompletedTask;
        }

        private async Task ExecutarRecompensa(SocketMessage msg, SocketGuildUser user, ulong guildId, string coluna, int horas, int min, int max, string nome)
        {
            var ultimo = EconomyHelper.GetUltimoTempo(guildId, user.Id, coluna);
            var tempoPassado = DateTime.UtcNow - ultimo;
            if (tempoPassado.TotalHours < horas)
            {
                var faltam = TimeSpan.FromHours(horas) - tempoPassado;
                await msg.Channel.SendMessageAsync($"❌ {user.Mention}, volte em `{faltam.Days}d {faltam.Hours}h {faltam.Minutes}m` para o {nome}.");
                return;
            }
            long ganho = new Random().Next(min, max + 1);
            EconomyHelper.AdicionarSaldo(guildId, user.Id, ganho);
            EconomyHelper.SetUltimoTempo(guildId, user.Id, coluna);
            await msg.Channel.SendMessageAsync($"✅ {user.Mention}, você ganhou `{EconomyHelper.FormatarSaldo(ganho)}` cpoints no **{nome}**!");
        }
    }
}
