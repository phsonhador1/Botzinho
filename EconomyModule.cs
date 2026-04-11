using Discord;
using Discord.WebSocket;
using Npgsql;
using Botzinho.Admins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

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
            if (valor >= 1000000) return $"{valor / 1000000.0:F1}M";
            if (valor >= 1000) return $"{valor / 1000.0:F1}K";
            return valor.ToString();
        }
    }

    public class EconomyHandler
    {
        private readonly DiscordSocketClient _client;

        public EconomyHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
        }

        private async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;
            if (msg is not SocketUserMessage userMsg) return;
            var user = msg.Author as SocketGuildUser;
            if (user == null) return;

            var content = msg.Content.ToLower().Trim();
            var guildId = user.Guild.Id;

            // zsaldo
            // zsaldo
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

            // zdaily
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
                    await msg.Channel.SendMessageAsync($"voce ja coletou seu daily hoje. volte em `{horas}h {minutos}m`.");
                    return;
                }

                var random = new Random();
                long recompensa = random.Next(500, 2001);

                EconomyHelper.AdicionarSaldo(guildId, user.Id, recompensa);
                EconomyHelper.SetUltimoDaily(guildId, user.Id);

                var saldoAtual = EconomyHelper.GetSaldo(guildId, user.Id);

                var embed = new EmbedBuilder()
                    .WithAuthor($"Daily | {user.DisplayName}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .WithDescription(
                        $"voce coletou seu daily e recebeu `{EconomyHelper.FormatarSaldo(recompensa)}` cpoints\n" +
                        $"**Saldo atual:** `{EconomyHelper.FormatarSaldo(saldoAtual)}` cpoints")
                    .WithColor(new Discord.Color(0x2B2D31))
                    .Build();

                await msg.Channel.SendMessageAsync(embed: embed);
            }

            // zpagar @usuario valor
            else if (content.StartsWith("zpagar"))
            {
                if (msg.MentionedUsers.Count == 0)
                {
                    await msg.Channel.SendMessageAsync("use: `zpagar @usuario valor`");
                    return;
                }

                var alvo = user.Guild.GetUser(msg.MentionedUsers.First().Id);
                if (alvo == null) { await msg.Channel.SendMessageAsync("usuario nao encontrado."); return; }
                if (alvo.Id == user.Id) { await msg.Channel.SendMessageAsync("voce nao pode pagar a si mesmo."); return; }
                if (alvo.IsBot) { await msg.Channel.SendMessageAsync("voce nao pode pagar um bot."); return; }

                var partes = content.Split(' ');
                long valor = 0;
                foreach (var parte in partes)
                {
                    if (long.TryParse(parte, out var v)) { valor = v; break; }
                }

                if (valor <= 0) { await msg.Channel.SendMessageAsync("valor invalido."); return; }

                if (!EconomyHelper.RemoverSaldo(guildId, user.Id, valor))
                { await msg.Channel.SendMessageAsync("saldo insuficiente."); return; }

                EconomyHelper.AdicionarSaldo(guildId, alvo.Id, valor);
                var saldoRemetente = EconomyHelper.GetSaldo(guildId, user.Id);

                await msg.Channel.SendMessageAsync(
                    $"{user.Mention} transferiu `{EconomyHelper.FormatarSaldo(valor)}` cpoints para {alvo.Mention}\n" +
                    $"**Seu saldo:** `{EconomyHelper.FormatarSaldo(saldoRemetente)}` cpoints");
            }

            // zranking
            else if (content == "zranking")
            {
                using var conn = new NpgsqlConnection(EconomyHelper.GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT user_id, saldo FROM economy_users WHERE guild_id = @gid AND saldo > 0 ORDER BY saldo DESC LIMIT 10";
                cmd.Parameters.AddWithValue("@gid", guildId.ToString());
                using var reader = cmd.ExecuteReader();

                var ranking = new List<string>();
                int pos = 1;
                while (reader.Read())
                {
                    var userId = reader.GetString(0);
                    var saldo = reader.GetInt64(1);
                    ranking.Add($"`#{pos}` <@{userId}> - `{EconomyHelper.FormatarSaldo(saldo)}` cpoints");
                    pos++;
                }

                if (ranking.Count == 0)
                {
                    await msg.Channel.SendMessageAsync("ninguem tem cpoints ainda.");
                    return;
                }

                var embed = new EmbedBuilder()
                    .WithAuthor($"Ranking | {user.Guild.Name}")
                    .WithDescription(string.Join("\n", ranking))
                    .WithFooter($"Top {ranking.Count} mais ricos")
                    .WithColor(new Discord.Color(0x2B2D31))
                    .Build();

                await msg.Channel.SendMessageAsync(embed: embed);
            }

            // zaddsaldo @usuario valor (admin)
            else if (content.StartsWith("zaddsaldo"))
            {
                if (!AdminModule.PodeUsarEconfigStatic(user))
                { await msg.Channel.SendMessageAsync("voce nao tem permissao."); return; }

                if (msg.MentionedUsers.Count == 0)
                { await msg.Channel.SendMessageAsync("use: `zaddsaldo @usuario valor`"); return; }

                var alvo = user.Guild.GetUser(msg.MentionedUsers.First().Id);
                if (alvo == null) { await msg.Channel.SendMessageAsync("usuario nao encontrado."); return; }

                long valor = 0;
                foreach (var parte in content.Split(' '))
                {
                    if (long.TryParse(parte, out var v)) { valor = v; break; }
                }

                if (valor <= 0) { await msg.Channel.SendMessageAsync("valor invalido."); return; }

                EconomyHelper.AdicionarSaldo(guildId, alvo.Id, valor);
                var saldo = EconomyHelper.GetSaldo(guildId, alvo.Id);

                await msg.Channel.SendMessageAsync($"adicionado `{EconomyHelper.FormatarSaldo(valor)}` cpoints para {alvo.Mention}\n**Saldo atual:** `{EconomyHelper.FormatarSaldo(saldo)}` cpoints");
            }

            // zremovesaldo @usuario valor (admin)
            else if (content.StartsWith("zremovesaldo"))
            {
                if (!AdminModule.PodeUsarEconfigStatic(user))
                { await msg.Channel.SendMessageAsync("voce nao tem permissao."); return; }

                if (msg.MentionedUsers.Count == 0)
                { await msg.Channel.SendMessageAsync("use: `zremovesaldo @usuario valor`"); return; }

                var alvo = user.Guild.GetUser(msg.MentionedUsers.First().Id);
                if (alvo == null) { await msg.Channel.SendMessageAsync("usuario nao encontrado."); return; }

                long valor = 0;
                foreach (var parte in content.Split(' '))
                {
                    if (long.TryParse(parte, out var v)) { valor = v; break; }
                }

                if (valor <= 0) { await msg.Channel.SendMessageAsync("valor invalido."); return; }

                var saldoAtual = EconomyHelper.GetSaldo(guildId, alvo.Id);
                var novoSaldo = Math.Max(0, saldoAtual - valor);
                EconomyHelper.SetSaldo(guildId, alvo.Id, novoSaldo);

                await msg.Channel.SendMessageAsync($"removido `{EconomyHelper.FormatarSaldo(valor)}` cpoints de {alvo.Mention}\n**Saldo atual:** `{EconomyHelper.FormatarSaldo(novoSaldo)}` cpoints");
            }

            // zsetsaldo @usuario valor (admin)
            else if (content.StartsWith("zsetsaldo"))
            {
                if (!AdminModule.PodeUsarEconfigStatic(user))
                { await msg.Channel.SendMessageAsync("voce nao tem permissao."); return; }

                if (msg.MentionedUsers.Count == 0)
                { await msg.Channel.SendMessageAsync("use: `zsetsaldo @usuario valor`"); return; }

                var alvo = user.Guild.GetUser(msg.MentionedUsers.First().Id);
                if (alvo == null) { await msg.Channel.SendMessageAsync("usuario nao encontrado."); return; }

                long valor = 0;
                foreach (var parte in content.Split(' '))
                {
                    if (long.TryParse(parte, out var v)) { valor = v; break; }
                }

                if (valor < 0) { await msg.Channel.SendMessageAsync("valor invalido."); return; }

                EconomyHelper.SetSaldo(guildId, alvo.Id, valor);

                await msg.Channel.SendMessageAsync($"saldo de {alvo.Mention} definido para `{EconomyHelper.FormatarSaldo(valor)}` cpoints");
            }
        }
        public static class EconomyImageHelper
        {
            public static async Task<string> GerarImagemSaldo(SocketGuildUser user, long saldo)
            {
                int width = 400;
                int height = 300;

                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(width, height));
                var canvas = surface.Canvas;

                // Fundo
                var bgPaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(30, 30, 35) };
                canvas.DrawRoundRect(new SkiaSharp.SKRect(0, 0, width, height), 20, 20, bgPaint);

                // Borda
                var borderPaint = new SkiaSharp.SKPaint
                {
                    Color = new SkiaSharp.SKColor(80, 60, 180),
                    Style = SkiaSharp.SKPaintStyle.Stroke,
                    StrokeWidth = 3
                };
                canvas.DrawRoundRect(new SkiaSharp.SKRect(2, 2, width - 2, height - 2), 20, 20, borderPaint);

                // Avatar
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
                        int avatarY = 25;

                        // Circulo do avatar
                        var avatarRect = new SkiaSharp.SKRect(avatarX, avatarY, avatarX + avatarSize, avatarY + avatarSize);
                        var clipPath = new SkiaSharp.SKPath();
                        clipPath.AddOval(avatarRect);

                        canvas.Save();
                        canvas.ClipPath(clipPath);
                        canvas.DrawBitmap(avatarBitmap, avatarRect);
                        canvas.Restore();

                        // Borda do avatar
                        var avatarBorderPaint = new SkiaSharp.SKPaint
                        {
                            Color = new SkiaSharp.SKColor(80, 60, 180),
                            Style = SkiaSharp.SKPaintStyle.Stroke,
                            StrokeWidth = 3,
                            IsAntialias = true
                        };
                        canvas.DrawOval(avatarX + avatarSize / 2, avatarY + avatarSize / 2, avatarSize / 2, avatarSize / 2, avatarBorderPaint);
                    }
                }
                catch { }

                // Nome
                var namePaint = new SkiaSharp.SKPaint
                {
                    Color = SkiaSharp.SKColors.White,
                    TextSize = 22,
                    IsAntialias = true,
                    Typeface = SkiaSharp.SKTypeface.FromFamilyName("Arial", SkiaSharp.SKFontStyle.Bold),
                    TextAlign = SkiaSharp.SKTextAlign.Center
                };
                canvas.DrawText(user.DisplayName, width / 2, 130, namePaint);

                // Separador
                var sepPaint = new SkiaSharp.SKPaint
                {
                    Color = new SkiaSharp.SKColor(60, 60, 70),
                    StrokeWidth = 1
                };
                canvas.DrawLine(30, 145, width - 30, 145, sepPaint);

                // Items
                var labelPaint = new SkiaSharp.SKPaint
                {
                    Color = SkiaSharp.SKColors.White,
                    TextSize = 18,
                    IsAntialias = true,
                    Typeface = SkiaSharp.SKTypeface.FromFamilyName("Arial", SkiaSharp.SKFontStyle.Bold)
                };

                var valorPaint = new SkiaSharp.SKPaint
                {
                    Color = new SkiaSharp.SKColor(160, 160, 170),
                    TextSize = 16,
                    IsAntialias = true,
                    Typeface = SkiaSharp.SKTypeface.FromFamilyName("Arial")
                };

                var iconPaint = new SkiaSharp.SKPaint
                {
                    Color = new SkiaSharp.SKColor(180, 130, 255),
                    TextSize = 20,
                    IsAntialias = true
                };

                string saldoFormatado = EconomyHelper.FormatarSaldo(saldo);

                // Carteira
                DrawItem(canvas, 50, 170, "Carteira", saldoFormatado, labelPaint, valorPaint, new SkiaSharp.SKColor(100, 200, 100), width);

                // Banco
                DrawItem(canvas, 50, 210, "Banco", "0", labelPaint, valorPaint, new SkiaSharp.SKColor(200, 100, 100), width);

                // Total
                DrawItem(canvas, 50, 250, "Total", saldoFormatado, labelPaint, valorPaint, new SkiaSharp.SKColor(200, 150, 50), width);

                // Salvar
                var path = Path.Combine(Path.GetTempPath(), $"saldo_{user.Id}_{DateTime.Now.Ticks}.png");
                using var image = surface.Snapshot();
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                using var stream = File.OpenWrite(path);
                data.SaveTo(stream);

                return path;
            }

            private static void DrawItem(SkiaSharp.SKCanvas canvas, float x, float y, string label, string valor, SkiaSharp.SKPaint labelPaint, SkiaSharp.SKPaint valorPaint, SkiaSharp.SKColor dotColor, int width)
            {
                // Fundo do item
                var itemBg = new SkiaSharp.SKPaint
                {
                    Color = new SkiaSharp.SKColor(40, 40, 48)
                };
                canvas.DrawRoundRect(new SkiaSharp.SKRect(x, y - 15, width - 50, y + 20), 8, 8, itemBg);

                // Bolinha colorida
                var dotPaint = new SkiaSharp.SKPaint
                {
                    Color = dotColor,
                    IsAntialias = true
                };
                canvas.DrawCircle(x + 15, y + 2, 8, dotPaint);

                // Label
                canvas.DrawText(label, x + 35, y + 7, labelPaint);

                // Valor
                canvas.DrawText(valor, x + 35, y + 25, valorPaint);
            }
        }
    }
}
