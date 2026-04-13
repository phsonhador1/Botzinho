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
        // BUSCA AS 10 TRANSACOES MAIS RECENTES ONDE O USUÁRIO É REMETENTE OU DESTINATÁRIO
        public static List<(string SenderId, string ReceiverId, long Amount, string Type, DateTime Date)> GetTransacoes(ulong guildId, ulong userId)
        {
            var list = new List<(string, string, long, string, DateTime)>();
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            // Puxa transações onde o cara é o remetente OU o recebedor
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

        public static void RegistrarTransacao(ulong guildId, ulong sender, ulong receiver, long amount, string type)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO economy_transactions (guild_id, sender_id, receiver_id, amount, type) VALUES (@gid, @sid, @rid, @amount, @type)";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString()); cmd.Parameters.AddWithValue("@sid", sender.ToString());
            cmd.Parameters.AddWithValue("@rid", receiver.ToString()); cmd.Parameters.AddWithValue("@amount", amount);
            cmd.Parameters.AddWithValue("@type", type); cmd.ExecuteNonQuery();
        }

        public static string FormatarSaldo(long valor) => valor >= 1000000 ? $"{valor / 1000000.0:F2}M" : valor >= 1000 ? $"{valor / 1000.0:F2}K" : valor.ToString();

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

    // --- 2. GERAÇÃO DE IMAGENS (SKIA DESIGN REFINADO) ---
    public static class EconomyImageHelper
    {
        private static readonly SKColor PurpleTheme = new SKColor(160, 80, 220);

        public static async Task<string> GerarImagemSaldo(SocketUser user, long wallet, long bank)
        {
            int width = 450; int height = 550;
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(12, 10, 20));

            var cardRect = new SKRect(20, 20, width - 20, height - 20);
            canvas.DrawRoundRect(cardRect, 30, 30, new SKPaint { Color = new SKColor(20, 18, 35), IsAntialias = true });
            canvas.DrawRoundRect(cardRect, 30, 30, new SKPaint { Color = PurpleTheme, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true });

            using var http = new HttpClient();
            try
            {
                var bytes = await http.GetByteArrayAsync(user.GetAvatarUrl(ImageFormat.Png, 256) ?? user.GetDefaultAvatarUrl());
                using var bmp = SKBitmap.Decode(bytes);
                var avRect = new SKRect(width / 2 - 70, 50, width / 2 + 70, 190);
                var path = new SKPath(); path.AddOval(avRect);
                canvas.Save(); canvas.ClipPath(path, SKClipOperation.Intersect, true);
                canvas.DrawBitmap(bmp, avRect); canvas.Restore();
                canvas.DrawOval(avRect, new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 3, Color = PurpleTheme, IsAntialias = true });
            }
            catch { }

            var boldFont = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);
            canvas.DrawText(user.Username, width / 2, 235, new SKPaint { Color = SKColors.White, TextSize = 24, Typeface = boldFont, TextAlign = SKTextAlign.Center, IsAntialias = true });

            float startY = 280;
            DrawSlimPill(canvas, "Carteira", wallet, width, startY, PurpleTheme);
            DrawSlimPill(canvas, "Banco", bank, width, startY + 80, PurpleTheme);
            DrawSlimPill(canvas, "Total", wallet + bank, width, startY + 160, new SKColor(255, 180, 0));

            var p = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot()) using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(p)) data.SaveTo(str);
            return p;
        }

        private static void DrawSlimPill(SKCanvas canvas, string label, long valor, int width, float y, SKColor accent)
        {
            var rect = new SKRect(50, y, width - 50, y + 60);
            canvas.DrawRoundRect(rect, 15, 15, new SKPaint { Color = new SKColor(35, 32, 55), IsAntialias = true });
            canvas.DrawRoundRect(new SKRect(50, y + 10, 54, y + 50), 2, 2, new SKPaint { Color = accent, IsAntialias = true });
            canvas.DrawText(label.ToUpper(), 70, y + 22, new SKPaint { Color = new SKColor(180, 180, 200), TextSize = 12, IsAntialias = true });
            canvas.DrawText(EconomyHelper.FormatarSaldo(valor) + " cpoints", 70, y + 48, new SKPaint { Color = SKColors.White, TextSize = 18, Typeface = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold), IsAntialias = true });
        }

        // --- RANKING REFORMULADO PROFISSIONAL ---
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
                    1 => new SKColor(255, 215, 0), // Dourado real
                    2 => new SKColor(192, 192, 192), // Prata
                    3 => new SKColor(205, 127, 50), // Bronze
                    _ => new SKColor(35, 32, 55)    // Dark Purple para o resto
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

        public EconomyHandler(DiscordSocketClient client)
        {
            _client = client; _client.MessageReceived += HandleMessage;
        }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () => {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage) return;
                    var user = msg.Author as SocketGuildUser; var content = msg.Content.ToLower().Trim(); var guildId = user.Guild.Id;
                    string[] cmds = { "zsaldo", "zdaily", "zrank", "zpay", "zdep", "zaddsaldo" };
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
                    else if (content.StartsWith("zdep"))
                    {
                        string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zdep (valor)` ou `zdep all`."); return; }
                        long carteira = EconomyHelper.GetSaldo(guildId, user.Id);
                        string valTxt = p[1].ToLower();
                        long valor = valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.TryParse(valTxt, out var v) ? v : 0;
                        if (valor <= 0 || carteira < valor) { await msg.Channel.SendMessageAsync("<:negativo:1492950137587241114> Saldo insuficiente na carteira."); return; }
                        if (EconomyHelper.RemoverSaldo(guildId, user.Id, valor)) { EconomyHelper.AdicionarBanco(guildId, user.Id, valor); await msg.Channel.SendMessageAsync($"🏦 {user.Mention}, você depositou `{EconomyHelper.FormatarSaldo(valor)}` cpoints!"); }
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
                            long v = valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.Parse(valTxt);
                            EconomyHelper.AdicionarSaldo(guildId, alvo.Id, v);
                            await msg.Channel.SendMessageAsync($"<a:lealdade:1493009439522033735> **Sucesso!** Foram adicionados `{EconomyHelper.FormatarSaldo(v)}` cpoints para <:pessoa:1493010183352483840> {alvo.Mention}.");
                        }
                    }
                    else if (content.StartsWith("zpay"))
                    {
                        var alvo = msg.MentionedUsers.FirstOrDefault();
                        if (alvo != null && alvo.Id != user.Id && !alvo.IsBot)
                        {
                            string valTxt = content.Split(' ').Last().ToLower();
                            long v = valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.Parse(valTxt);
                            if (EconomyHelper.RemoverSaldo(guildId, user.Id, v))
                            {
                                EconomyHelper.AdicionarSaldo(guildId, alvo.Id, v);
                                EconomyHelper.RegistrarTransacao(guildId, user.Id, alvo.Id, v, "TRANSFERENCIA");
                                await msg.Channel.SendMessageAsync($"✅ {user.Mention} enviou `{EconomyHelper.FormatarSaldo(v)}` para {alvo.Mention}.");
                            }
                        } 
                    }
                    else if (content.StartsWith("ztransacoes") || content.StartsWith("ztranscoes"))
                    {
                        // Mudamos de "alvo" para "usuarioAlvo" para evitar conflito de escopo
                        var usuarioAlvo = msg.MentionedUsers.FirstOrDefault() ?? user;
                        var transacoes = EconomyHelper.GetTransacoes(guildId, usuarioAlvo.Id);

                        var eb = new EmbedBuilder()
                            .WithAuthor($"Extrato Bancário | {usuarioAlvo.Username}", usuarioAlvo.GetAvatarUrl() ?? usuarioAlvo.GetDefaultAvatarUrl())
                            .WithColor(new Color(160, 80, 220))
                            .WithFooter("Mostrando as últimas 10 transações");

                        if (transacoes.Count == 0)
                        {
                            eb.WithDescription("<:negativo:1492950137587241114> Nenhuma transação encontrada para este usuário.");
                        }
                        else
                        {
                            string listaTexto = "";
                            foreach (var t in transacoes)
                            {
                                string dataFormatada = t.Date.AddHours(-3).ToString("dd/MM HH:mm"); // Ajuste de fuso horário (Brasília)
                                string valor = EconomyHelper.FormatarSaldo(t.Amount);

                                // Lógica para saber se ele enviou ou recebeu
                                if (t.SenderId == usuarioAlvo.Id.ToString())
                                {
                                    listaTexto += $"`[{dataFormatada}]` 🔴 **Envio de** `{valor}` para <@{t.ReceiverId}>\n";
                                }
                                else
                                {
                                    listaTexto += $"`[{dataFormatada}]` 🟢 **Recebeu** `{valor}` de <@{t.SenderId}>\n";
                                }
                            }
                            eb.WithDescription(listaTexto);
                        }

                        await msg.Channel.SendMessageAsync(embed: eb.Build());
                    }
                }
                catch { }
            }); return Task.CompletedTask;
        }
    }
}
