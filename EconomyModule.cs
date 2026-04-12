using Discord;
using Discord.WebSocket;
using Npgsql;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Botzinho.Economy
{
    public static class EconomyHelper
    {
        public static string GetConnectionString() => Environment.GetEnvironmentVariable("DATABASE_URL") ?? throw new Exception("DATABASE_URL nao configurado!");

        // Lista de IDs que podem usar o comando zaddsaldo
        public static readonly HashSet<ulong> IDsAutorizados = new() { 1161794729462214779 };

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
                    banco BIGINT DEFAULT 0,
                    ultimo_daily TIMESTAMP DEFAULT '2000-01-01',
                    ultimo_roubo TIMESTAMP DEFAULT '2000-01-01',
                    PRIMARY KEY (guild_id, user_id)
                );
                ALTER TABLE economy_users ADD COLUMN IF NOT EXISTS banco BIGINT DEFAULT 0;
                ALTER TABLE economy_users ADD COLUMN IF NOT EXISTS ultimo_semanal TIMESTAMP DEFAULT '2000-01-01';
                ALTER TABLE economy_users ADD COLUMN IF NOT EXISTS ultimo_mensal TIMESTAMP DEFAULT '2000-01-01';
                ALTER TABLE economy_users ADD COLUMN IF NOT EXISTS ultimo_roubo TIMESTAMP DEFAULT '2000-01-01';";
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
            return cmd.ExecuteNonQuery() > 0;
        }

        public static bool DepositarTudo(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE economy_users SET banco = banco + saldo, saldo = 0 WHERE guild_id = @gid AND user_id = @uid AND saldo > 0";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            return cmd.ExecuteNonQuery() > 0;
        }

        public static bool SacarDinheiro(ulong guildId, ulong userId, long valor)
        {
            if (GetBanco(guildId, userId) < valor) return false;
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE economy_users SET banco = banco - @valor, saldo = saldo + @valor WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@valor", valor);
            return cmd.ExecuteNonQuery() > 0;
        }

        public static string FormatarSaldo(long valor)
        {
            if (valor >= 1000000) return $"{valor / 1000000.0:F2}M";
            if (valor >= 1000) return $"{valor / 1000.0:F2}K";
            return valor.ToString();
        }

        public static List<(ulong UserId, long Total)> GetTop10(ulong guildId)
        {
            var list = new List<(ulong, long)>();
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT user_id, (saldo + banco) as total FROM economy_users WHERE guild_id = @gid ORDER BY total DESC LIMIT 10";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add((ulong.Parse(reader.GetString(0)), reader.GetInt64(1)));
            return list;
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

                    string[] cmds = { "zsaldo", "zdaily", "zrank", "zpay", "zdep", "zsac", "zroubar", "zaddsaldo" };
                    if (!cmds.Any(c => content.StartsWith(c))) return;

                    // --- SALDO ---
                    if (content == "zsaldo")
                    {
                        long s = EconomyHelper.GetSaldo(guildId, user.Id);
                        long b = EconomyHelper.GetBanco(guildId, user.Id);
                        await msg.Channel.SendMessageAsync($"💰 **Saldos de {user.Mention}:**\n💵 **Carteira:** `{EconomyHelper.FormatarSaldo(s)}` cpoints\n🏦 **Banco:** `{EconomyHelper.FormatarSaldo(b)}` cpoints");
                    }

                    // --- TRANSFERIR (ZPAY) ---
                    else if (content.StartsWith("zpay"))
                    {
                        var mencionado = msg.MentionedUsers.FirstOrDefault();
                        if (mencionado == null || mencionado.Id == user.Id) { await msg.Channel.SendMessageAsync("❌ Mencione alguém válido."); return; }

                        string valTxt = content.Split(' ').Last();
                        long valor = valTxt.EndsWith("k") ? (long)(double.Parse(valTxt.Replace("k", "")) * 1000) : long.Parse(valTxt);

                        if (EconomyHelper.RemoverSaldo(guildId, user.Id, valor))
                        {
                            EconomyHelper.AdicionarSaldo(guildId, mencionado.Id, valor);
                            await msg.Channel.SendMessageAsync($"✅ **Transferência!** {user.Mention} enviou `{EconomyHelper.FormatarSaldo(valor)}` para {mencionado.Mention}.");
                        }
                        else await msg.Channel.SendMessageAsync("❌ Saldo insuficiente na carteira.");
                    }

                    // --- ROUBAR (ZROUBAR) ---
                    else if (content.StartsWith("zroubar"))
                    {
                        var vitima = msg.MentionedUsers.FirstOrDefault();
                        if (vitima == null || vitima.Id == user.Id || vitima.IsBot) { await msg.Channel.SendMessageAsync("❌ Mencione uma vítima válida."); return; }

                        long saldoVitima = EconomyHelper.GetSaldo(guildId, vitima.Id);
                        if (saldoVitima < 500) { await msg.Channel.SendMessageAsync("❌ A vítima é muito pobre, não vale a pena o risco."); return; }

                        Random rand = new();
                        if (rand.Next(1, 101) <= 40)
                        { // 40% Sucesso
                            long roubado = (long)(saldoVitima * rand.Next(10, 30) / 100.0);
                            EconomyHelper.RemoverSaldo(guildId, vitima.Id, roubado);
                            EconomyHelper.AdicionarSaldo(guildId, user.Id, roubado);
                            await msg.Channel.SendMessageAsync($"🥷 **Assalto concluído!** {user.Mention} roubou `{EconomyHelper.FormatarSaldo(roubado)}` de {vitima.Mention}!");
                        }
                        else
                        { // Falha (Multa)
                            long multa = 1000;
                            EconomyHelper.RemoverSaldo(guildId, user.Id, multa);
                            await msg.Channel.SendMessageAsync($"👮 **Deu ruim!** {user.Mention} tentou roubar {vitima.Mention}, foi pego e pagou uma multa de `1K`.");
                        }
                    }

                    // --- BANCO (DEP/SAC) ---
                    else if (content == "zdep all")
                    {
                        if (EconomyHelper.DepositarTudo(guildId, user.Id)) await msg.Channel.SendMessageAsync("🏦 Dinheiro guardado no banco com sucesso!");
                    }
                    else if (content.StartsWith("zsac all"))
                    {
                        long b = EconomyHelper.GetBanco(guildId, user.Id);
                        if (EconomyHelper.SacarDinheiro(guildId, user.Id, b)) await msg.Channel.SendMessageAsync("💸 Você sacou todo seu dinheiro do banco.");
                    }

                    // --- ADDSALDO (ADMIN) ---
                    else if (content.StartsWith("zaddsaldo") && EconomyHelper.IDsAutorizados.Contains(user.Id))
                    {
                        var alvo = msg.MentionedUsers.First();
                        long val = long.Parse(content.Split(' ').Last());
                        EconomyHelper.AdicionarSaldo(guildId, alvo.Id, val);
                        await msg.Channel.SendMessageAsync($"✅ Admin adicionou `{EconomyHelper.FormatarSaldo(val)}` para {alvo.Mention}.");
                    }
                }
                catch { }
            });
            return Task.CompletedTask;
        }
    }
}
