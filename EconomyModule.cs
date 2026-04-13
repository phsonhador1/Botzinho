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
    // --- 1. LÓGICA E BANCO DE DADOS ---
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
                    banco BIGINT DEFAULT 0, ultimo_daily TIMESTAMP DEFAULT '2000-01-01',
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
            if (GetSaldo(guildId, userId) < valor) return false;
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
            long saldo = GetSaldo(guildId, userId);
            if (saldo <= 0) return false;
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
    }

    // --- 2. GERAÇÃO DE IMAGENS ---
    public static class EconomyImageHelper
    {
        public static async Task<string> GerarImagemSaldo(SocketUser user, long wallet, long bank)
        {
            int width = 500; int height = 600;
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.DrawRoundRect(new SKRect(20, 20, width - 20, height - 20), 30, 30, new SKPaint { Color = SKColors.White, IsAntialias = true });

            using var http = new HttpClient();
            try
            {
                var bytes = await http.GetByteArrayAsync(user.GetAvatarUrl(ImageFormat.Png, 256) ?? user.GetDefaultAvatarUrl());
                using var bmp = SKBitmap.Decode(bytes);
                var avRect = new SKRect(width / 2 - 100, 50, width / 2 + 100, 250);
                var path = new SKPath(); path.AddOval(avRect);
                canvas.Save(); canvas.ClipPath(path, antialias: true);
                canvas.DrawBitmap(bmp, avRect); canvas.Restore();
            }
            catch { }

            var bold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
            canvas.DrawText(user.Username, width / 2, 310, new SKPaint { Color = SKColors.Black, TextSize = 35, Typeface = bold, TextAlign = SKTextAlign.Center, IsAntialias = true });

            DrawPill(canvas, wallet, 350, "Carteira", new SKColor(160, 80, 220));
            DrawPill(canvas, bank, 430, "Banco", new SKColor(160, 80, 220));
            DrawPill(canvas, wallet + bank, 510, "Total", new SKColor(230, 180, 60));

            var p = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(p)) data.SaveTo(stream);
            return p;
        }

        private static void DrawPill(SKCanvas canvas, long valor, float y, string label, SKColor color)
        {
            canvas.DrawRoundRect(new SKRect(60, y, 440, y + 65), 32, 32, new SKPaint { Color = new SKColor(245, 245, 245), IsAntialias = true });
            canvas.DrawRoundRect(new SKRect(60, y, 130, y + 65), 32, 32, new SKPaint { Color = color, IsAntialias = true });
            canvas.DrawText(label, 145, y + 30, new SKPaint { Color = SKColors.Black, TextSize = 22, IsAntialias = true });
            canvas.DrawText(EconomyHelper.FormatarSaldo(valor), 145, y + 55, new SKPaint { Color = SKColors.Gray, TextSize = 20, IsAntialias = true });
        }

        public static async Task<string> GerarImagemRank(SocketGuild guild, List<(ulong UserId, long Total)> topUsers)
        {
            int width = 850; int height = 680;
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            var fontBold = SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold) ?? SKTypeface.Default;
            canvas.Clear(new SKColor(20, 10, 30));
            canvas.DrawText("Top Coins", 40, 80, new SKPaint { Color = SKColors.White, TextSize = 45, Typeface = fontBold, IsAntialias = true });

            using var httpClient = new HttpClient();
            for (int i = 0; i < topUsers.Count; i++)
            {
                var userData = topUsers[i];
                IUser member = guild.GetUser(userData.UserId) ?? await ((IGuild)guild).GetUserAsync(userData.UserId);

                int col = i % 2; int row = i / 2;
                int x = 40 + (col * 405); int y = 120 + (row * 105);
                var pColor = (i == 0) ? new SKColor(255, 215, 0) : new SKColor(80, 0, 80);
                canvas.DrawRoundRect(new SKRect(x, y, x + 380, y + 90), 45, 45, new SKPaint { Color = pColor, IsAntialias = true });

                try
                {
                    var avBytes = await httpClient.GetByteArrayAsync(member?.GetAvatarUrl() ?? member?.GetDefaultAvatarUrl());
                    using var bmp = SKBitmap.Decode(avBytes);
                    var r = new SKRect(x + 15, y + 15, x + 75, y + 75);
                    var p = new SKPath(); p.AddOval(r);
                    canvas.Save(); canvas.ClipPath(p, antialias: true);
                    canvas.DrawBitmap(bmp, r); canvas.Restore();
                }
                catch { }

                canvas.DrawText(member?.Username ?? "Desconhecido", x + 90, y + 55, new SKPaint { Color = SKColors.White, TextSize = 22, IsAntialias = true });
                canvas.DrawText(EconomyHelper.FormatarSaldo(userData.Total), x + 360, y + 55, new SKPaint { Color = SKColors.White, TextSize = 20, TextAlign = SKTextAlign.Right, IsAntialias = true });
            }
            var path = Path.Combine(Path.GetTempPath(), $"rank_{guild.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(path)) data.SaveTo(stream);
            return path;
        }
    }

    // --- 3. COMANDOS (HANDLER) ---
    public class EconomyHandler
    {
        private readonly DiscordSocketClient _client;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();
        private static readonly Dictionary<ulong, long> ApostasAtivas = new();
        private const string IMG_MOEDA = "https://cdn.discordapp.net/attachments/1110495236716773447/1163499638461042831/coin_1540515.png";

        public EconomyHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
            _client.ButtonExecuted += HandleButtons;
        }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () => {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage) return;
                    var user = msg.Author as SocketGuildUser; var content = msg.Content.ToLower().Trim(); var guildId = user.Guild.Id;

                    string[] cmds = { "zsaldo", "zdaily", "zrank", "zpay", "zdep", "zaddsaldo", "zcf", "zcoinflip" };
                    if (!cmds.Any(c => content.StartsWith(c))) return;
                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 2) return;
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    if (content == "zdaily")
                    {
                        long g = new Random().Next(167000, 180001); EconomyHelper.AdicionarSaldo(guildId, user.Id, g);
                        await msg.Channel.SendMessageAsync($"✅ {user.Mention}, `{EconomyHelper.FormatarSaldo(g)}` cpoints no **Diário**!");
                    }
                    else if (content == "zdep all")
                    {
                        if (EconomyHelper.DepositarTudo(guildId, user.Id)) await msg.Channel.SendMessageAsync("🏦 Carteira guardada no banco!");
                    }
                    else if (content == "zsaldo")
                    {
                        var p = await EconomyImageHelper.GerarImagemSaldo(user, EconomyHelper.GetSaldo(guildId, user.Id), EconomyHelper.GetBanco(guildId, user.Id));
                        await msg.Channel.SendFileAsync(p, ""); File.Delete(p);
                    }
                    else if (content == "zrank")
                    {
                        var p = await EconomyImageHelper.GerarImagemRank(user.Guild, EconomyHelper.GetTop10(guildId));
                        await msg.Channel.SendFileAsync(p, "🏆 **Top Ricos**"); File.Delete(p);
                    }
                    else if (content.StartsWith("zaddsaldo") && EconomyHelper.IDsAutorizados.Contains(user.Id))
                    {
                        var alvo = msg.MentionedUsers.FirstOrDefault();
                        if (alvo != null)
                        {
                            string valTxt = content.Split(' ').Last().ToLower();
                            long v = valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.Parse(valTxt);
                            EconomyHelper.AdicionarSaldo(guildId, alvo.Id, v);
                            await msg.Channel.SendMessageAsync($"<a:lealdade:1493009439522033735> **Sucesso!** Foram adicionados `{EconomyHelper.FormatarSaldo(v)}` cpoints para <:pessoa:1493010183352483840> {alvo.Mention}.");
                        }
                    }
                    // --- ZCOINFLIP COM MENSAGEM DE USO CORRIGIDA ---
                    else if (content.StartsWith("zcf") || content.StartsWith("zcoinflip"))
                    {
                        string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length < 2)
                        {
                            await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zcoinflip (valor)`");
                            return;
                        }

                        long s = EconomyHelper.GetSaldo(guildId, user.Id);
                        string vT = p[1].ToLower();
                        long val = vT == "all" ? s : (vT.EndsWith("k") ? (long)(double.Parse(vT.Replace("k", "")) * 1000) : vT.EndsWith("m") ? (long)(double.Parse(vT.Replace("m", "")) * 1000000) : long.TryParse(vT, out var res) ? res : 0);

                        if (val <= 0 || s < val) { await msg.Channel.SendMessageAsync("❌ Saldo insuficiente na carteira."); return; }
                        if (ApostasAtivas.ContainsKey(user.Id)) { await msg.Channel.SendMessageAsync("⚠️ Você já tem uma aposta ativa!"); return; }

                        ApostasAtivas[user.Id] = val; EconomyHelper.RemoverSaldo(guildId, user.Id, val);

                        var eb = new EmbedBuilder().WithAuthor("Cara ou Coroa", IMG_MOEDA)
                            .WithDescription($@"• **Olá, {user.Mention}! Bem-vindo(a) ao jogo Cara ou Coroa.**

🪙 | **Valor em aposta:** `{EconomyHelper.FormatarSaldo(val)}`

💡 | **Como funciona:**
Escolha entre **Cara** ou **Coroa** e aposte. Se acertar, você ganha o dobro da aposta; se errar, você perde o valor apostado.

🧧 | **Desistir da aposta:**
Se decidir não continuar, clique no ❌ para desistir da aposta.")
                            .WithFooter($"Apostador: {user.Username}", user.GetAvatarUrl()).WithColor(new Color(114, 137, 218));

                        var cb = new ComponentBuilder()
                            .WithButton("Cara", $"cf_cara_{user.Id}", ButtonStyle.Secondary, new Emoji("🙂"))
                            .WithButton("Coroa", $"cf_coroa_{user.Id}", ButtonStyle.Secondary, new Emoji("👑"))
                            .WithButton(null, $"cf_cancel_{user.Id}", ButtonStyle.Danger, new Emoji("❌"));

                        await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
                    }
                    else if (content.StartsWith("zpay"))
                    {
                        var alvo = msg.MentionedUsers.FirstOrDefault();
                        if (alvo != null && alvo.Id != user.Id && !alvo.IsBot)
                        {
                            string valTxt = content.Split(' ').Last().ToLower();
                            long v = valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.Parse(valTxt);
                            if (EconomyHelper.RemoverSaldo(guildId, user.Id, v)) { EconomyHelper.AdicionarSaldo(guildId, alvo.Id, v); await msg.Channel.SendMessageAsync($"✅ {user.Mention} enviou `{EconomyHelper.FormatarSaldo(v)}` para {alvo.Mention}."); }
                        }
                    }
                }
                catch { }
            }); return Task.CompletedTask;
        }

        private async Task HandleButtons(SocketMessageComponent comp)
        {
            if (!comp.Data.CustomId.StartsWith("cf_")) return;
            var parts = comp.Data.CustomId.Split('_'); var choice = parts[1]; var uid = ulong.Parse(parts[2]);
            if (comp.User.Id != uid || !ApostasAtivas.TryGetValue(uid, out long val)) return;
            var user = (SocketGuildUser)comp.User; ApostasAtivas.Remove(uid);
            if (choice == "cancel")
            {
                EconomyHelper.AdicionarSaldo(user.Guild.Id, uid, val);
                await comp.UpdateAsync(x => { x.Content = $"✅ {user.Mention} desistiu e recuperou seu saldo."; x.Embed = null; x.Components = null; });
                return;
            }
            string res = new Random().Next(0, 2) == 0 ? "cara" : "coroa"; bool win = choice == res;
            var eb = new EmbedBuilder().WithAuthor("Cara ou Coroa", IMG_MOEDA).WithFooter($"Apostador: {user.Username}").WithThumbnailUrl(IMG_MOEDA);
            if (win)
            {
                EconomyHelper.AdicionarSaldo(user.Guild.Id, uid, val * 2);
                eb.WithColor(Color.Green).WithDescription($@"Você escolheu **{choice}** e o resultado foi **{res}**!

**Você ganhou:**
💰 +{EconomyHelper.FormatarSaldo(val * 2)}

🎊 **Parabéns! A sorte estava do seu lado desta vez!**");
            }
            else
            {
                eb.WithColor(Color.Red).WithDescription($@"Você escolheu **{choice}**, mas o resultado foi o **contrário**.

**Você perdeu:**
❌ -{EconomyHelper.FormatarSaldo(val)}

**Infelizmente, a sorte não estava do seu lado desta vez!**");
            }
            await comp.UpdateAsync(x => { x.Embed = eb.Build(); x.Components = null; x.Content = user.Mention; });
        }
    }
}
