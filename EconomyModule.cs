using Discord;
using Discord.Interactions;
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

    public class SaldoModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("zsaldo", "Mostra seu saldo ou de outro usuario")]
        public async Task SaldoAsync(
            [Summary("usuario", "Usuario para ver o saldo")] SocketGuildUser alvo = null)
        {
            var target = alvo ?? (SocketGuildUser)Context.User;
            var saldo = EconomyHelper.GetSaldo(Context.Guild.Id, target.Id);

            var embed = new EmbedBuilder()
                .WithAuthor($"{target.DisplayName}", target.GetAvatarUrl() ?? target.GetDefaultAvatarUrl())
                .WithDescription($"**Saldo:** `{EconomyHelper.FormatarSaldo(saldo)}` cpoints")
                .WithColor(new Discord.Color(0x2B2D31))
                .Build();

            await RespondAsync(embed: embed);
        }
    }

    public class DailyModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("zdaily", "Coleta sua recompensa diaria")]
        public async Task DailyAsync()
        {
            var user = (SocketGuildUser)Context.User;
            var guildId = Context.Guild.Id;

            var ultimoDaily = EconomyHelper.GetUltimoDaily(guildId, user.Id);
            var agora = DateTime.UtcNow;
            var diferenca = agora - ultimoDaily;

            if (diferenca.TotalHours < 24)
            {
                var restante = TimeSpan.FromHours(24) - diferenca;
                var horas = (int)restante.TotalHours;
                var minutos = restante.Minutes;
                await RespondAsync($"voce ja coletou seu daily hoje. volte em `{horas}h {minutos}m`.", ephemeral: true);
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

            await RespondAsync(embed: embed);
        }
    }

    public class PagarModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("zpagar", "Transfere cpoints para outro usuario")]
        public async Task PagarAsync(
            [Summary("usuario", "Usuario para transferir")] SocketGuildUser alvo,
            [Summary("valor", "Quantidade de cpoints")] long valor)
        {
            var user = (SocketGuildUser)Context.User;

            if (alvo.Id == user.Id)
            { await RespondAsync("voce nao pode pagar a si mesmo.", ephemeral: true); return; }

            if (alvo.IsBot)
            { await RespondAsync("voce nao pode pagar um bot.", ephemeral: true); return; }

            if (valor <= 0)
            { await RespondAsync("valor invalido.", ephemeral: true); return; }

            if (!EconomyHelper.RemoverSaldo(Context.Guild.Id, user.Id, valor))
            { await RespondAsync("saldo insuficiente.", ephemeral: true); return; }

            EconomyHelper.AdicionarSaldo(Context.Guild.Id, alvo.Id, valor);

            var saldoRemetente = EconomyHelper.GetSaldo(Context.Guild.Id, user.Id);
            var saldoDestino = EconomyHelper.GetSaldo(Context.Guild.Id, alvo.Id);

            await RespondAsync(
                $"{user.Mention} transferiu `{EconomyHelper.FormatarSaldo(valor)}` cpoints para {alvo.Mention}\n" +
                $"**Seu saldo:** `{EconomyHelper.FormatarSaldo(saldoRemetente)}` cpoints");
        }
    }

    public class RankingModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("zranking", "Mostra os mais ricos do servidor")]
        public async Task RankingAsync()
        {
            using var conn = new NpgsqlConnection(EconomyHelper.GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT user_id, saldo FROM economy_users WHERE guild_id = @gid AND saldo > 0 ORDER BY saldo DESC LIMIT 10";
            cmd.Parameters.AddWithValue("@gid", Context.Guild.Id.ToString());
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
                await RespondAsync("ninguem tem cpoints ainda.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithAuthor($"Ranking | {Context.Guild.Name}")
                .WithDescription(string.Join("\n", ranking))
                .WithFooter($"Top {ranking.Count} mais ricos")
                .WithColor(new Discord.Color(0x2B2D31))
                .Build();

            await RespondAsync(embed: embed);
        }
    }

    public class AdicionarSaldoModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("zaddsaldo", "Adiciona cpoints a um usuario (admin)")]
        public async Task AddSaldoAsync(
            [Summary("usuario", "Usuario")] SocketGuildUser alvo,
            [Summary("valor", "Quantidade de cpoints")] long valor)
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.PodeUsarEconfigStatic(user))
            { await RespondAsync("voce nao tem permissao.", ephemeral: true); return; }

            if (valor <= 0)
            { await RespondAsync("valor invalido.", ephemeral: true); return; }

            EconomyHelper.AdicionarSaldo(Context.Guild.Id, alvo.Id, valor);
            var saldo = EconomyHelper.GetSaldo(Context.Guild.Id, alvo.Id);

            await RespondAsync($"adicionado `{EconomyHelper.FormatarSaldo(valor)}` cpoints para {alvo.Mention}\n**Saldo atual:** `{EconomyHelper.FormatarSaldo(saldo)}` cpoints");
        }
    }

    public class RemoverSaldoModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("zremovesaldo", "Remove cpoints de um usuario (admin)")]
        public async Task RemoveSaldoAsync(
            [Summary("usuario", "Usuario")] SocketGuildUser alvo,
            [Summary("valor", "Quantidade de cpoints")] long valor)
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.PodeUsarEconfigStatic(user))
            { await RespondAsync("voce nao tem permissao.", ephemeral: true); return; }

            if (valor <= 0)
            { await RespondAsync("valor invalido.", ephemeral: true); return; }

            var saldoAtual = EconomyHelper.GetSaldo(Context.Guild.Id, alvo.Id);
            var novoSaldo = Math.Max(0, saldoAtual - valor);
            EconomyHelper.SetSaldo(Context.Guild.Id, alvo.Id, novoSaldo);

            await RespondAsync($"removido `{EconomyHelper.FormatarSaldo(valor)}` cpoints de {alvo.Mention}\n**Saldo atual:** `{EconomyHelper.FormatarSaldo(novoSaldo)}` cpoints");
        }
    }

    public class SetSaldoModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("zsetsaldo", "Define o saldo de um usuario (admin)")]
        public async Task SetSaldoAsync(
            [Summary("usuario", "Usuario")] SocketGuildUser alvo,
            [Summary("valor", "Novo saldo")] long valor)
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.PodeUsarEconfigStatic(user))
            { await RespondAsync("voce nao tem permissao.", ephemeral: true); return; }

            if (valor < 0)
            { await RespondAsync("valor invalido.", ephemeral: true); return; }

            EconomyHelper.SetSaldo(Context.Guild.Id, alvo.Id, valor);

            await RespondAsync($"saldo de {alvo.Mention} definido para `{EconomyHelper.FormatarSaldo(valor)}` cpoints");
        }
    }
}
