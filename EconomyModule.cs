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

        // --- FUNÇÕES DO TEMPO DO DAILY ---
        public static DateTime GetUltimoDaily(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ultimo_daily FROM economy_users WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var res = cmd.ExecuteScalar();
            if (res != null && res != DBNull.Value) return Convert.ToDateTime(res);
            return DateTime.MinValue; // Se não existir, libera na hora
        }

        public static void AtualizarDaily(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString()); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO economy_users (guild_id, user_id, ultimo_daily) VALUES (@gid, @uid, @dt)
                                ON CONFLICT (guild_id, user_id) DO UPDATE SET ultimo_daily = @dt";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@dt", DateTime.Now); // Salva a hora exata de agora
            cmd.ExecuteNonQuery();
        }
        // ---------------------------------

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

    // --- 2. GERAÇÃO DE IMAGENS (SKIA DESIGN REFINADO PREMIUM) ---
    public static class EconomyImageHelper
    {
        private static readonly SKColor PurpleTheme = new SKColor(160, 80, 220); // Roxo Zoe
        private static readonly SKColor GoldTheme = new SKColor(255, 180, 0);    // Dourado para o Total
        private static readonly SKColor DarkBg = new SKColor(10, 8, 18);         // Fundo escuro
        private static readonly SKColor CardBg = new SKColor(22, 18, 35);        // Fundo do cartão central

        public static async Task<string> GerarImagemSaldo(SocketUser user, long wallet, long bank)
        {
            int width = 500; 
            int height = 650; 
            
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;

            // 1. Fundo com Gradiente Radial (Brilho sutil no centro)
            using var bgPaint = new SKPaint();
            bgPaint.Shader = SKShader.CreateRadialGradient(
                new SKPoint(width / 2, height / 2),
                Math.Max(width, height),
                new[] { new SKColor(25, 20, 45), DarkBg },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, width, height, bgPaint);

            // 2. Sombra do Cartão Principal
            var cardRect = new SKRect(25, 25, width - 25, height - 25);
            using var shadowPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 150),
                ImageFilter = SKImageFilter.CreateDropShadow(0, 10, 20, 20, new SKColor(0, 0, 0, 200)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(cardRect, 30, 30, shadowPaint);

            // 3. Fundo do Cartão Principal
            using var cardPaint = new SKPaint { Color = CardBg, IsAntialias = true };
            canvas.DrawRoundRect(cardRect, 30, 30, cardPaint);

            // Borda do Cartão (Brilho no topo)
            using var highlightPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                Color = new SKColor(255, 255, 255, 25), // Branco super transparente
                IsAntialias = true
            };
            canvas.DrawRoundRect(cardRect, 30, 30, highlightPaint);

            // 4. Desenhar Avatar (Top Center)
            float avY = 130;
            float avRadius = 70;
            var avRect = new SKRect((width / 2) - avRadius, avY - avRadius, (width / 2) + avRadius, avY + avRadius);
            
            using var http = new HttpClient();
            try
            {
                var bytes = await http.GetByteArrayAsync(user.GetAvatarUrl(ImageFormat.Png, 256) ?? user.GetDefaultAvatarUrl());
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

            // Anel do Avatar (Borda grossa e estilizada)
            using var ringPaint = new SKPaint 
            { 
                Style = SKPaintStyle.Stroke, 
                StrokeWidth = 4, 
                Color = PurpleTheme, 
                IsAntialias = true 
            };
            canvas.DrawOval(avRect, ringPaint);

            // 5. Nome do Usuário
            var fontBold = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);
            using var namePaint = new SKPaint { Color = SKColors.White, TextSize = 28, Typeface = fontBold, TextAlign = SKTextAlign.Center, IsAntialias = true };
            canvas.DrawText(user.Username, width / 2, 245, namePaint);

            // Linha divisória sutil abaixo do nome
            using var linePaint = new SKPaint { Color = new SKColor(255, 255, 255, 20), StrokeWidth = 2, IsAntialias = true };
            canvas.DrawLine(width / 2 - 100, 270, width / 2 + 100, 270, linePaint);

            // 6. Painéis de Saldo
            float startY = 300;
            DrawModernPanel(canvas, "CARTEIRA", wallet, width, startY, PurpleTheme, fontBold);
            DrawModernPanel(canvas, "BANCO", bank, width, startY + 95, PurpleTheme, fontBold);
            DrawModernPanel(canvas, "TOTAL", wallet + bank, width, startY + 190, GoldTheme, fontBold);

            // 7. Salvar e Retornar
            var p = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot()) 
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(p)) data.SaveTo(str);
            
            return p;
        }

        private static void DrawModernPanel(SKCanvas canvas, string label, long valor, int totalWidth, float y, SKColor accent, SKTypeface font)
        {
            float pWidth = totalWidth - 90;
            float pHeight = 75;
            float x = 45;

            var rect = new SKRect(x, y, x + pWidth, y + pHeight);

            using var panelBg = new SKPaint { Color = new SKColor(14, 12, 22), IsAntialias = true };
            canvas.DrawRoundRect(rect, 12, 12, panelBg);

            var indicatorRect = new SKRect(x, y + 15, x + 5, y + pHeight - 15);
            using var indicatorPaint = new SKPaint { Color = accent, IsAntialias = true };
            canvas.DrawRoundRect(indicatorRect, 2, 2, indicatorPaint);

            using var labelPaint = new SKPaint { Color = new SKColor(130, 125, 145), TextSize = 13, Typeface = font, IsAntialias = true };
            canvas.DrawText(label, x + 20, y + 28, labelPaint);

            string valorFormatado = EconomyHelper.FormatarSaldo(valor);
            using var valuePaint = new SKPaint { Color = SKColors.White, TextSize = 34, Typeface = font, IsAntialias = true };
            canvas.DrawText(valorFormatado, x + 20, y + 62, valuePaint);

            float valueWidth = valuePaint.MeasureText(valorFormatado);
            using var cpointsPaint = new SKPaint { Color = accent, TextSize = 16, Typeface = font, IsAntialias = true };
            canvas.DrawText("cpoints", x + 25 + valueWidth, y + 60, cpointsPaint);
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
            _client = client; _client.MessageReceived += HandleMessage;
        }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () => {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage) return;
                    var user = msg.Author as SocketGuildUser; var content = msg.Content.ToLower().Trim(); var guildId = user.Guild.Id;
                    string[] cmds = { "zsaldo", "zdaily", "zrank", "zpay", "zdep", "zaddsaldo", "ztransacoes", "ztranscoes", "zroubar" };
                    if (!cmds.Any(c => content.StartsWith(c))) return;

                    // Cooldown de 2 segundos (Padrão para comandos de economia)
                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 3)
                    {
                        var aviso = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> {user.Mention}, calma ai viadinho abusado! Aguarde **3 segundos** para usar outro **comando**.");
                        _ = Task.Delay(3000).ContinueWith(_ => aviso.DeleteAsync());
                        return;
                    }
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    if (content == "zdaily")
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
                        EconomyHelper.AdicionarSaldo(guildId, user.Id, g);
                        EconomyHelper.AtualizarDaily(guildId, user.Id); // Salva que o usuário acabou de pegar
                        EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, user.Id, g, "DAILY");
                        await msg.Channel.SendMessageAsync($"<:acerto:1493079138783727756> {user.Mention} Zdaily Coletado com Sucesso!, `+ {EconomyHelper.FormatarSaldo(g)}` cpoints no **Diário**!");
                    }
                    else if (content == "zdep all")
                    {
                        long carteira = EconomyHelper.GetSaldo(guildId, user.Id);

                        if (carteira <= 0)
                        {
                            await msg.Channel.SendMessageAsync("<:negativo:1492950137587241114> Você não tem saldo na carteira para depositar.");
                            return;
                        }

                        if (EconomyHelper.DepositarTudo(guildId, user.Id))
                        {
                            // Logando o Depósito All
                            EconomyHelper.RegistrarTransacao(guildId, user.Id, user.Id, carteira, "DEPOSITO");
                            await msg.Channel.SendMessageAsync($"<:acerto:1493079138783727756> {user.Mention}, Sucesso! você depositou `{EconomyHelper.FormatarSaldo(carteira)}` cpoints no banco!");
                        }
                    }
                    else if (content.StartsWith("zdep"))
                    {
                        string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length < 2) { await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zdep (valor)` ou `zdep all`."); return; }
                        long carteira = EconomyHelper.GetSaldo(guildId, user.Id);
                        string valTxt = p[1].ToLower();
                        long valor = valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.TryParse(valTxt, out var v) ? v : 0;
                        if (valor <= 0 || carteira < valor) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Saldo insuficiente na carteira."); return; }
                        if (EconomyHelper.RemoverSaldo(guildId, user.Id, valor))
                        {
                            EconomyHelper.AdicionarBanco(guildId, user.Id, valor);
                            EconomyHelper.RegistrarTransacao(guildId, user.Id, user.Id, valor, "DEPOSITO");
                            await msg.Channel.SendMessageAsync($"<:acerto:1493079138783727756> {user.Mention}, Sucesso! você depositou `+ {EconomyHelper.FormatarSaldo(valor)}` cpoints!");
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
                            long v = valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : valTxt.EndsWith("m") ? (long)(double.Parse(valTxt.Replace("m", "")) * 1000000) : long.Parse(valTxt);
                            EconomyHelper.AdicionarSaldo(guildId, alvo.Id, v);
                            await msg.Channel.SendMessageAsync($"<a:lealdade:1493009439522033735> **Sucesso!** Foram adicionados `{EconomyHelper.FormatarSaldo(v)}` cpoints para <:pessoa:1493010183352483840> {alvo.Mention}.");
                        }
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

                        // 3. Processar o Valor (all, k, m, número)
                        long saldoDoador = EconomyHelper.GetBanco(guildId, user.Id);
                        long valorTransferencia = 0;
                        string vTxt = partes[2].ToLower();

                        if (vTxt == "all") { valorTransferencia = saldoDoador; }
                        else
                        {
                            valorTransferencia = vTxt.EndsWith("k") ? (long)(double.Parse(vTxt.Replace("k", "")) * 1000) :
                                                  vTxt.EndsWith("m") ? (long)(double.Parse(vTxt.Replace("m", "")) * 1000000) :
                                                  long.TryParse(vTxt, out var v) ? v : 0;
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
                        // 1. TIMEOUT DE 30 MINUTOS (Específico para Roubo)
                        if (_stealCooldowns.TryGetValue(user.Id, out var lastSteal) && (DateTime.UtcNow - lastSteal).TotalMinutes < 25)
                        {
                            var tempoRestante = 25 - (DateTime.UtcNow - lastSteal).TotalMinutes;
                            var aviso = await msg.Channel.SendMessageAsync($"<a:negativo:1492950137587241114> {user.Mention}, Espere Filho da Puta! O cheiro de crime ainda está no ar. Aguarde `{tempoRestante:F0} minutos` para tentar roubar novamente.");
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
                            await msg.Channel.SendMessageAsync($"<:atencao:1493350891749642240> {user.Mention} tentou roubar {vitima.Mention}, mas ele estava duro kkk **Carteira vazia!**");

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

                                // 6. Mensagem de Sucesso (Agressiva e sem Embed)
                                await msg.Channel.SendMessageAsync($"<:blackninja:1493348778705424464> **TEMOS UM LADRÃO AQUI NO SERVER!** <:ladrao:1493349791340433479> {user.Mention} acaba de passar a mão em <:dinheiro:1493360319928733838> `{EconomyHelper.FormatarSaldo(valorRoubado)}` cpoints na carteira de {vitima.Mention}!");
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
