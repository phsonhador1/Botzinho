using Discord;
using Discord.WebSocket;
using Npgsql;
using Botzinho.Admins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
            if (content == "zsaldo" || content.StartsWith("zsaldo "))
            {
                SocketGuildUser alvo = user;

                if (msg.MentionedUsers.Count > 0)
                    alvo = user.Guild.GetUser(msg.MentionedUsers.First().Id) ?? user;

                var saldo = EconomyHelper.GetSaldo(guildId, alvo.Id);

                var embed = new EmbedBuilder()
                    .WithAuthor($"{alvo.DisplayName}", alvo.GetAvatarUrl() ?? alvo.GetDefaultAvatarUrl())
                    .WithDescription($"**Saldo:** `{EconomyHelper.FormatarSaldo(saldo)}` cpoints")
                    .WithColor(new Discord.Color(0x2B2D31))
                    .Build();

                await msg.Channel.SendMessageAsync(embed: embed);
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
    }
}
