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

        // --- FUNÇÕES DE LEMBRETE ---
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

    // --- 2. GERAÇÃO DE IMAGENS COM NOVO DESIGN ---
    public static class EconomyImageHelper
    {
        private static readonly SKColor PurpleTheme = new SKColor(160, 80, 220);
        private static readonly SKColor DarkBg = new SKColor(40, 38, 55);
        private static readonly SKColor CardBg = new SKColor(60, 55, 85);

        public static async Task<string> GerarImagemSaldo(SocketUser user, long wallet, long bank)
        {
            int width = 400;
            int height = 500;

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;

            // Fundo
            canvas.Clear(DarkBg);

            // Card principal
            var cardRect = new SKRect(30, 30, width - 30, height - 30);
            using var cardPaint = new SKPaint { Color = CardBg, IsAntialias = true };
            canvas.DrawRoundRect(cardRect, 25, 25, cardPaint);

            // Borda sutil
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                Color = new SKColor(255, 255, 255, 30),
                IsAntialias = true
            };
            canvas.DrawRoundRect(cardRect, 25, 25, borderPaint);

            // Avatar (maior e no topo)
            float avatarRadius = 65;
            float avatarX = width / 2f;
            float avatarY = 110;

            var avatarRect = new SKRect(avatarX - avatarRadius, avatarY - avatarRadius, avatarX + avatarRadius, avatarY + avatarRadius);

            using var http = new HttpClient();
            try
            {
                var bytes = await http.GetByteArrayAsync(user.GetAvatarUrl(ImageFormat.Png, 512) ?? user.GetDefaultAvatarUrl());
                using var bmp = SKBitmap.Decode(bytes);
                var circlePath = new SKPath();
                circlePath.AddOval(avatarRect);

                canvas.Save();
                canvas.ClipPath(circlePath, SKClipOperation.Intersect, true);
                canvas.DrawBitmap(bmp, avatarRect);
                canvas.Restore();
            }
            catch
            {
                canvas.DrawOval(avatarRect, new SKPaint { Color = new SKColor(80, 75, 110), IsAntialias = true });
            }

            // Anel roxo do avatar
            using var ringPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 4,
                Color = PurpleTheme,
                IsAntialias = true
            };
            canvas.DrawOval(avatarRect, ringPaint);

            // Apelido do usuário
            string displayName = (user as SocketGuildUser)?.Nickname ?? user.GlobalName ?? user.Username;
            var fontBold = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);
            
            using var namePaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 26,
                Typeface = fontBold,
                TextAlign = SKTextAlign.Center,
                IsAntialias = true
            };

            canvas.DrawText(displayName, width / 2f, 210, namePaint);

            // Painéis de saldo
            DrawBalancePanel(canvas, "Carteira", wallet, width, 250, PurpleTheme, fontBold);
            DrawBalancePanel(canvas, "Banco", bank, width, 320, PurpleTheme, fontBold);
            DrawBalancePanel(canvas, "Total", wallet + bank, width, 390, new SKColor(255, 180, 0), fontBold);

            // Salvar
            var path = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
            using (var img = surface.Snapshot())
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            using (var str = File.OpenWrite(path)) data.SaveTo(str);

            return path;
        }

        private static void DrawBalancePanel(SKCanvas canvas, string label, long value, int totalWidth, float y, SKColor accentColor, SKTypeface font)
        {
            float panelWidth = totalWidth - 60;
            float panelHeight = 55;
            float panelX = 30;

            var panelRect = new SKRect(panelX, y, panelX + panelWidth, y + panelHeight);

            // Fundo do painel
            using var panelBg = new SKPaint { Color = new SKColor(35, 32, 50), IsAntialias = true };
            canvas.DrawRoundRect(panelRect, 15, 15, panelBg);

            // Ícone colorido à esquerda
            float circleRadius = 20;
            float circleX = panelX + 30;
            float circleY = y + panelHeight / 2f;

            using var circlePaint = new SKPaint { Color = accentColor, IsAntialias = true };
            canvas.DrawCircle(circleX, circleY, circleRadius, circlePaint);

            // Texto do label
            float textStartX = panelX + 70;
            using var labelPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 16,
                Typeface = font,
                IsAntialias = true
            };
            canvas.DrawText(label, textStartX, y + 20, labelPaint);

            // Valor formatado
            string formattedValue = EconomyHelper.FormatarSaldo(value);
            string displayValue = $"{value} ({formattedValue})";

            using var valuePaint = new SKPaint
            {
                Color = new SKColor(200, 200, 220),
                TextSize = 14,
                IsAntialias = true
            };
            canvas.DrawText(displayValue, textStartX, y + 42, valuePaint);
        }

        public static async Task<string> GerarImagemRank(SocketGuild guild, List<(ulong UserId, long Total)> top)
        {
            int w = 850;
            int h = 750;
            using var surface = SKSurface.Create(new SKImageInfo(w, h));
            var canvas = surface.Canvas;
            canvas.Clear(DarkBg);

            var boldFont = SKTypeface.FromFamilyName("Sans-Serif", SKFontStyle.Bold);

            var paintWhite = new SKPaint { Color = SKColors.White, TextSize = 48, Typeface = boldFont, IsAntialias = true };
            var paintPurple = new SKPaint { Color = PurpleTheme, TextSize = 48, Typeface = boldFont, IsAntialias = true };
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
                    _ => new SKColor(50, 48, 70)
                };

                SKColor textColor = (pos <= 3) ? SKColors.Black : SKColors.White;

                var rect = new SKRect(x, y, x + 385, y + 100);
                canvas.DrawRoundRect(rect, 20, 20, new SKPaint { Color = pillColor, IsAntialias = true });

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
            using (var d = img.Encode(SKEncodedImageFormat.Png, 100))
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

                    string[] cmds = { "zsaldo", "zdaily", "zrank", "zpay", "zdep", "zaddsaldo", "zsetsaldo", "ztransacoes", "ztranscoes", "zroubar" };
                    if (!cmds.Any(c => content.StartsWith(c))) return;

                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 3)
                    {
                        var aviso = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> {user.Mention}, calma ai viadinho abusado! Aguarde **3 segundos** para usar outro **comando**.");
                        _ = Task.Delay(3000).ContinueWith(_ => aviso.DeleteAsync());
                        return;
                    }
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    if (content == "zdaily")
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
                        int xpGanho = new Random().Next(15, 45);

                        EconomyHelper.AdicionarSaldo(guildId, user.Id, g);
                        EconomyHelper.AtualizarDaily(guildId, user.Id);
                        EconomyHelper.RegistrarTransacao(guildId, _client.CurrentUser.Id, user.Id, g, "DAILY");

                        var eb = new EmbedBuilder()
                            .WithColor(new Color(160, 80, 220))
                            .WithTitle("<:calendario:1495171666844713173> Daily")
                            .WithDescription($"Você coletou sua **recompensa diária** com sucesso!\n\n" +
                                             $"<a:trofeu:1493063952060387479> **Recompensas:**\n" +
                                             $"• <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(g)}** cpoints\n" +
                                             $"• <:levelup:1495174376885063841> **+{xpGanho}XP**\n\n" +
                                             $"<:seta:1493089125979656385> Você pode ver seu **saldo** utilizando o comando **zsaldo**.\n\n" +
                                             $"<a:teste:1490570407307378712> Utilize o comando **zdep all** para depositar seus coins!")
                            .WithThumbnailUrl("https://media.discordapp.net/attachments/1077714940745502750/1104440347586732082/tempo-e-dinheiro.png?width=460&height=460");

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
                            EconomyHelper.RegistrarTransacao(guildId, user.Id, user.Id, carteira, "DEPOSITO");
                            await ((SocketUserMessage)msg).ReplyAsync($"<a:sucess:1494692628372132013> Seu deposito de **{carteira}** foi concluído com sucesso.");
                        }
                    }
                    else if (content.StartsWith("zdep"))
                    {
                        string[] p = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length < 2)
                        {
                            await msg.Channel.SendMessageAsync("❓ **Modo de uso:** `zdep (valor)` ou `zdep all`.");
                            return;
                        }

                        long carteira = EconomyHelper.GetSaldo(guildId, user.Id);
                        string valTxt = p[1].ToLower();
                        long valor = EconomyHelper.ConverterLetraParaNumero(valTxt);

                        if (valor <= 0 || carteira < valor)
                        {
                            await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Saldo insuficiente na carteira.");
                            return;
                        }

                        if (EconomyHelper.RemoverSaldo(guildId, user.Id, valor))
                        {
                            EconomyHelper.AdicionarBanco(guildId, user.Id, valor);
                            EconomyHelper.RegistrarTransacao(guildId, user.Id, user.Id, valor, "DEPOSITO");
                            await ((SocketUserMessage)msg).ReplyAsync($"<a:sucess:1494692628372132013> Seu depósito de **{valor}** foi concluído com sucesso.");
                        }
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
                        await msg.Channel.SendFileAsync(p, $"<a:trofeu:1493063952060387479> **Top Ricos Do Servidor**\n<:emoji_8:1491910148476899529> Você tem **{EconomyHelper.FormatarSaldo(meuTotal)}** coins e está em **#{minhaPos}**");
                        File.Delete(p);
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
                    else if (content.StartsWith("zpay"))
                    {
                        if (_cooldowns.TryGetValue(user.Id, out var lastZpay) && (DateTime.UtcNow - lastZpay).TotalSeconds < 2)
                        {
                            var aviso = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> {user.Mention}, Da pra esperar Filho da Puta? Aguarde **2 segundos** para usar outro comando.");
                            _ = Task.Delay(2000).ContinueWith(_ => aviso.DeleteAsync());
                            return;
                        }
                        _cooldowns[user.Id] = DateTime.UtcNow;

                        string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                        if (partes.Length < 3)
                        {
                            await msg.Channel.SendMessageAsync("❓ **Uso correto:** `zpay @usuario [valor]`\n*Exemplo: zpay @Zoe 10k*");
                            return;
                        }

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

                        long saldoDoador = EconomyHelper.GetBanco(guildId, user.Id);
                        long valorTransferencia = 0;
                        string vTxt = partes[2].ToLower();

                        if (vTxt == "all") { valorTransferencia = saldoDoador; }
                        else { valorTransferencia = EconomyHelper.ConverterLetraParaNumero(vTxt); }

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

                        try
                        {
                            EconomyHelper.RemoverBanco(guildId, user.Id, valorTransferencia);
                            EconomyHelper.AdicionarBanco(guildId, mencionado.Id, valorTransferencia);
                            EconomyHelper.RegistrarTransacao(guildId, user.Id, mencionado.Id, valorTransferencia, "TRANSFERENCIA_DIRETA");

                            await msg.Channel.SendMessageAsync($"<a:lealdade:1493009439522033735> **Sucesso!** Foram transferidos `{EconomyHelper.FormatarSaldo(valorTransferencia)}` cpoints para <:pessoa:1493010183352483840> {mencionado.Mention}.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Erro zpay]: {ex.Message}");
                            await msg.Channel.SendMessageAsync("<:aviso:1493365148323152034> Ocorreu um erro interno ao processar a transferência.");
                        }
                    }
                    else if (content.StartsWith("zroubar"))
                    {
                        if (_stealCooldowns.TryGetValue(user.Id, out var lastSteal) && (DateTime.UtcNow - lastSteal).TotalMinutes < 25)
                        {
                            var tempoRestante = 25 - (DateTime.UtcNow - lastSteal).TotalMinutes;
                            var ebCooldown = new EmbedBuilder()
                                .WithColor(new Color(255, 71, 87))
                                .WithDescription($"<a:negativo:1492950137587241114> {user.Mention}, Espere Filho da Puta! O cheiro de crime ainda está no ar. Aguarde `{tempoRestante:F0} minutos` para tentar roubar novamente.");

                            var aviso = await msg.Channel.SendMessageAsync(embed: ebCooldown.Build());
                            _ = Task.Delay(5000).ContinueWith(_ => aviso.DeleteAsync());
                            return;
                        }

                        string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                        if (partes.Length < 2)
                        {
                            await msg.Channel.SendMessageAsync("❓ **Uso correto:** `zroubar @usuario`\n*Exemplo: zroubar @Zoe*");
                            return;
                        }

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

                        long saldoCarteiraVitima = EconomyHelper.GetSaldo(guildId, vitima.Id);

                        if (saldoCarteiraVitima <= 0)
                        {
                            var ebFracasso = new EmbedBuilder()
                                .WithColor(new Color(43, 45, 49))
                                .WithAuthor("Assalto Fracassado", "https://cdn-icons-png.flaticon.com/512/4338/4338873.png")
                                .WithDescription($"<:atencao:1493350891749642240> {user.Mention} tentou roubar {vitima.Mention}, mas ele estava duro kkk\n\n**Carteira vazia!** Nenhuma moeda foi levada.")
                                .WithThumbnailUrl(vitima.GetAvatarUrl() ?? vitima.GetDefaultAvatarUrl());

                            await msg.Channel.SendMessageAsync(embed: ebFracasso.Build());
                            _stealCooldowns[user.Id] = DateTime.UtcNow;
                            return;
                        }

                        long valorRoubado = saldoCarteiraVitima / 2;

                        try
                        {
                            if (EconomyHelper.RemoverSaldo(guildId, vitima.Id, valorRoubado))
                            {
                                EconomyHelper.AdicionarSaldo(guildId, user.Id, valorRoubado);
                                EconomyHelper.RegistrarTransacao(guildId, vitima.Id, user.Id, valorRoubado, "ROUBO_CARTEIRA_METADE");
                                _stealCooldowns[user.Id] = DateTime.UtcNow;

                                var ebSucesso = new EmbedBuilder()
                                    .WithColor(new Color(255, 71, 87))
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
                                    linha = $"🏦 Depositou **{formatAmount} coin(s)** da carteira para o banco.";
                                else if (t.Type == "DAILY")
                                    linha = $"🎁 ➕ Recebeu **{formatAmount} coin(s)** do bônus diário.";
                                else if (t.Type == "ROLETA_GANHO")
                                    linha = $"<a:ganhador:1493088070923452599> Ganhou **{formatAmount} coin(s)** na roleta.";
                                else if (t.Type == "ROLETA_PERDA")
                                    linha = $"🎡 <:erro:1493078898462949526> Perdeu **{formatAmount} coin(s)** na roleta.";
                                else if (t.Type == "COINFLIP_GANHO")
                                    linha = $"🪙 ➕ Ganhou **{formatAmount} coin(s)** no coinflip.";
                                else if (t.Type == "COINFLIP_PERDA")
                                    linha = $"🪙 <:erro:1493078898462949526> Perdeu **{formatAmount} coin(s)** no coinflip.";
                                else if (t.Type == "BLACKJACK_GANHO")
                                    linha = $"🃏 ➕ Ganhou **{formatAmount} coin(s)** no blackjack.";
                                else if (t.Type == "BLACKJACK_PERDA")
                                    linha = $"🃏 <:erro:1493078898462949526> Perdeu **{formatAmount} coin(s)** no blackjack.";
                                else if (t.Type == "BLACKJACK_EMPATE")
                                    linha = $"🃏 ⚖️ Recuperou **{formatAmount} coin(s)** (Empate no blackjack).";
                                else if (t.Type == "TRANSFERENCIA" || t.Type == "TRANSFERENCIA_DIRETA")
                                {
                                    if (t.SenderId == usuarioAlvo.Id.ToString())
                                        linha = $"💸 ➖ Enviou **{formatAmount} coin(s)** para <@{t.ReceiverId}>.";
                                    else
                                        linha = $"💸 ➕ Recebeu **{formatAmount} coin(s)** de <@{t.SenderId}>.";
                                }
                                else if (t.Type == "ROUBO_CARTEIRA_METADE")
                                {
                                    if (t.SenderId == usuarioAlvo.Id.ToString())
                                        linha = $"🕵️‍♂️ <:erro:1493078898462949526> Foi roubado em **{formatAmount} coin(s)** por <@{t.ReceiverId}>.";
                                    else
                                        linha = $"🕵️‍♂️ ➕ Roubou **{formatAmount} coin(s)** de <@{t.SenderId}>.";
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
            });
            return Task.CompletedTask;
        }
    }
}
