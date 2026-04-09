using Npgsql;
using System;
using System.Collections.Generic;

namespace Botzinho.Admins
{
    public static class AdminBanRepository
    {
        private static string GetConnectionString()
        {
            return "Host=shuttle.proxy.rlwy.net;Port=54220;Database=railway;Username=postgres;Password=uxmOfkOGeiSrvHfxpttOBrgcCXXWiyPK;SSL Mode=Require;Trust Server Certificate=true";
        }

        public class TempBanData
        {
            public ulong GuildId { get; set; }
            public ulong UserId { get; set; }
            public ulong AuthorId { get; set; }
            public string Reason { get; set; } = "Sem motivo";
            public DateTime ExpiresAtUtc { get; set; }
            public bool Active { get; set; } = true;
        }

        public static void InicializarTabela()
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS temp_bans (
                    guild_id TEXT NOT NULL,
                    user_id TEXT NOT NULL,
                    author_id TEXT NOT NULL,
                    reason TEXT NOT NULL,
                    expires_at_utc TIMESTAMP NOT NULL,
                    active BOOLEAN NOT NULL DEFAULT TRUE,
                    created_at_utc TIMESTAMP NOT NULL DEFAULT NOW(),
                    PRIMARY KEY (guild_id, user_id)
                );
            ";

            cmd.ExecuteNonQuery();
            Console.WriteLine("Tabela temp_bans inicializada.");
        }

        public static void SalvarOuAtualizarBan(TempBanData data)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO temp_bans
                    (guild_id, user_id, author_id, reason, expires_at_utc, active)
                VALUES
                    (@guild_id, @user_id, @author_id, @reason, @expires_at_utc, @active)
                ON CONFLICT (guild_id, user_id)
                DO UPDATE SET
                    author_id = EXCLUDED.author_id,
                    reason = EXCLUDED.reason,
                    expires_at_utc = EXCLUDED.expires_at_utc,
                    active = EXCLUDED.active;
            ";

            cmd.Parameters.AddWithValue("@guild_id", data.GuildId.ToString());
            cmd.Parameters.AddWithValue("@user_id", data.UserId.ToString());
            cmd.Parameters.AddWithValue("@author_id", data.AuthorId.ToString());
            cmd.Parameters.AddWithValue("@reason", data.Reason);
            cmd.Parameters.AddWithValue("@expires_at_utc", data.ExpiresAtUtc);
            cmd.Parameters.AddWithValue("@active", data.Active);

            cmd.ExecuteNonQuery();
        }

        public static void DesativarBan(ulong guildId, ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE temp_bans
                SET active = FALSE
                WHERE guild_id = @guild_id AND user_id = @user_id;
            ";

            cmd.Parameters.AddWithValue("@guild_id", guildId.ToString());
            cmd.Parameters.AddWithValue("@user_id", userId.ToString());

            cmd.ExecuteNonQuery();
        }

        public static List<TempBanData> ObterBansExpirados()
        {
            var list = new List<TempBanData>();

            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT guild_id, user_id, author_id, reason, expires_at_utc, active
                FROM temp_bans
                WHERE active = TRUE AND expires_at_utc <= @now;
            ";

            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TempBanData
                {
                    GuildId = ulong.Parse(reader.GetString(0)),
                    UserId = ulong.Parse(reader.GetString(1)),
                    AuthorId = ulong.Parse(reader.GetString(2)),
                    Reason = reader.GetString(3),
                    ExpiresAtUtc = reader.GetDateTime(4),
                    Active = reader.GetBoolean(5)
                });
            }

            return list;
        }
    }
}
