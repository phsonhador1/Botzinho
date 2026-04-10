using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Npgsql;
using Botzinho.Admins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Moderation
{
    public static class ModerationHelper
    {
        public static string GetConnectionString()
        {
            return Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? throw new Exception("DATABASE_URL não configurado!");
        }

        public static void InicializarTabelas()
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS warns (
                    id SERIAL PRIMARY KEY,
                    guild_id TEXT,
                    user_id TEXT,
                    moderator_id TEXT,
                    motivo TEXT,
                    data TIMESTAMP DEFAULT NOW()
                );
            ";
            cmd.ExecuteNonQuery();
            Console.WriteLine("Tabelas de moderação inicializadas.");
        }

        public static TimeSpan? ParseDuration(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length < 2) return null;
            var unit = input[^1];
            if (!int.TryParse(input[..^1], out var value) || value <= 0) return null;
            return unit switch { 'm' => TimeSpan.FromMinutes(value), 'h' => TimeSpan.FromHours(value), 'd' => TimeSpan.FromDays(value), _ => null };
        }
    }

    public class BanModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("ban", "Bane um usuário do servidor")]
        [RequireBotPermission(GuildPermission.BanMembers)]
        public async Task BanAsync(
            [Summary("usuario", "Usuário para banir")] SocketGuildUser alvo,
            [Summary("motivo", "Motivo do ban")] string motivo = "Sem motivo informado",
            [Summary("dias", "Dias de mensagens para apagar (0-7)")] int dias = 0)
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "ban"))
            { await RespondAsync("❌ tu não tem permissão pra banir ninguém", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "ban") && !user.GuildPermissions.BanMembers)
            { await RespondAsync("❌ tu não tem permissão pra banir ninguém", ephemeral: true); return; }

            if (alvo.Id == user.Id) { await RespondAsync("❌ tu quer se banir? kkkk não dá", ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await RespondAsync("❌ cargo igual ou maior", ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await RespondAsync("❌ meu cargo é menor", ephemeral: true); return; }

            try { await alvo.SendMessageAsync($"🔨 tu foi banido de **{Context.Guild.Name}** kkkk"); } catch { }
            await Context.Guild.AddBanAsync(alvo, pruneDays: Math.Clamp(dias, 0, 7), reason: motivo);
            await RespondAsync($"kkkkkkkkkkkk {alvo.Mention} banido, vaza mlk 🔨\n**Motivo:** {motivo}");
        }
    }

    public class UnbanModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("unban", "Desbane um usuário")]
        [RequireBotPermission(GuildPermission.BanMembers)]
        public async Task UnbanAsync([Summary("id", "ID do usuário")] string userId)
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "ban"))
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "ban") && !user.GuildPermissions.BanMembers)
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (!ulong.TryParse(userId, out var id)) { await RespondAsync("❌ ID inválido", ephemeral: true); return; }
            try { await Context.Guild.RemoveBanAsync(id); await RespondAsync($"✅ ID `{id}` desbanido, volta aí mano"); }
            catch { await RespondAsync("❌ esse ID não tá na lista de bans", ephemeral: true); }
        }
    }

    public class KickModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("kick", "Expulsa um usuário")]
        [RequireBotPermission(GuildPermission.KickMembers)]
        public async Task KickAsync(
            [Summary("usuario", "Usuário para expulsar")] SocketGuildUser alvo,
            [Summary("motivo", "Motivo")] string motivo = "Sem motivo informado")
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "kick"))
            { await RespondAsync("❌ tu não tem permissão pra kickar", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "kick") && !user.GuildPermissions.KickMembers)
            { await RespondAsync("❌ tu não tem permissão pra kickar", ephemeral: true); return; }

            if (alvo.Id == user.Id) { await RespondAsync("❌ quer se kickar? kkkk", ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await RespondAsync("❌ cargo igual ou maior", ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await RespondAsync("❌ meu cargo é menor", ephemeral: true); return; }

            try { await alvo.SendMessageAsync($"👢 tu foi kickado de **{Context.Guild.Name}** kkkk"); } catch { }
            await alvo.KickAsync(motivo);
            await RespondAsync($"👢 {alvo.Mention} tomou kick kkkk vaza daqui\n**Motivo:** {motivo}");
        }
    }

    public class MuteModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("mute", "Silencia um usuário")]
        [RequireBotPermission(GuildPermission.ModerateMembers)]
        public async Task MuteAsync(
            [Summary("usuario", "Usuário")] SocketGuildUser alvo,
            [Summary("duracao", "Duração (10m, 1h, 1d)")] string duracao,
            [Summary("motivo", "Motivo")] string motivo = "Sem motivo informado")
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "mute"))
            { await RespondAsync("❌ tu não tem permissão pra mutar", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "mute") && !user.GuildPermissions.ModerateMembers)
            { await RespondAsync("❌ tu não tem permissão pra mutar", ephemeral: true); return; }

            if (alvo.Id == user.Id) { await RespondAsync("❌ quer se mutar? kkkk", ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await RespondAsync("❌ cargo igual ou maior", ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await RespondAsync("❌ meu cargo é menor", ephemeral: true); return; }

            var tempo = ModerationHelper.ParseDuration(duracao);
            if (tempo == null || tempo.Value.TotalMinutes < 1 || tempo.Value.TotalDays > 28)
            { await RespondAsync("❌ duração inválida, usa: `10m`, `1h`, `1d` (máx 28d)", ephemeral: true); return; }

            await alvo.SetTimeOutAsync(tempo.Value, new RequestOptions { AuditLogReason = motivo });
            await RespondAsync($"🔇 {alvo.Mention} calado por `{duracao}` kkkkk silêncio otário\n**Motivo:** {motivo}");
        }

        [SlashCommand("unmute", "Remove silenciamento")]
        [RequireBotPermission(GuildPermission.ModerateMembers)]
        public async Task UnmuteAsync([Summary("usuario", "Usuário")] SocketGuildUser alvo)
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "mute"))
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "mute") && !user.GuildPermissions.ModerateMembers)
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            await alvo.RemoveTimeOutAsync();
            await RespondAsync($"🔊 {alvo.Mention} pode falar de novo, se comporta agr");
        }
    }

    public class WarnModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("warn", "Dá um aviso")]
        public async Task WarnAsync(
            [Summary("usuario", "Usuário")] SocketGuildUser alvo,
            [Summary("motivo", "Motivo")] string motivo = "Sem motivo informado")
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "warn"))
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "warn") && !user.GuildPermissions.ModerateMembers)
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (alvo.IsBot) { await RespondAsync("❌ não dá pra avisar bot", ephemeral: true); return; }

            using var conn = new NpgsqlConnection(ModerationHelper.GetConnectionString());
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO warns (guild_id, user_id, moderator_id, motivo) VALUES (@gid, @uid, @mid, @motivo)";
                cmd.Parameters.AddWithValue("@gid", Context.Guild.Id.ToString());
                cmd.Parameters.AddWithValue("@uid", alvo.Id.ToString());
                cmd.Parameters.AddWithValue("@mid", user.Id.ToString());
                cmd.Parameters.AddWithValue("@motivo", motivo);
                cmd.ExecuteNonQuery();
            }

            int totalWarns;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM warns WHERE guild_id = @gid AND user_id = @uid";
                cmd.Parameters.AddWithValue("@gid", Context.Guild.Id.ToString());
                cmd.Parameters.AddWithValue("@uid", alvo.Id.ToString());
                totalWarns = Convert.ToInt32(cmd.ExecuteScalar());
            }

            await RespondAsync($"⚠️ {alvo.Mention} tomou warn kkk cuidado hein ({totalWarns}/3)\n**Motivo:** {motivo}");

            if (totalWarns >= 3)
            {
                try
                {
                    try { await alvo.SendMessageAsync($"🔨 tu foi banido de **{Context.Guild.Name}** por acumular 3 warns kkkk"); } catch { }
                    await Context.Guild.AddBanAsync(alvo, reason: "Acumulou 3 avisos.");
                    await Context.Channel.SendMessageAsync($"🔨 {alvo.Mention} acumulou 3 warns e foi banido automaticamente kkkk vaza");

                    using var delCmd = conn.CreateCommand();
                    delCmd.CommandText = "DELETE FROM warns WHERE guild_id = @gid AND user_id = @uid";
                    delCmd.Parameters.AddWithValue("@gid", Context.Guild.Id.ToString());
                    delCmd.Parameters.AddWithValue("@uid", alvo.Id.ToString());
                    delCmd.ExecuteNonQuery();
                }
                catch (Exception ex) { Console.WriteLine($"Erro no ban automático: {ex.Message}"); }
            }
        }

        [SlashCommand("warns", "Mostra avisos")]
        public async Task WarnsAsync([Summary("usuario", "Usuário")] SocketGuildUser alvo)
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "warn"))
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "warn") && !user.GuildPermissions.ModerateMembers)
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            using var conn = new NpgsqlConnection(ModerationHelper.GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, moderator_id, motivo, data FROM warns WHERE guild_id = @gid AND user_id = @uid ORDER BY data DESC";
            cmd.Parameters.AddWithValue("@gid", Context.Guild.Id.ToString());
            cmd.Parameters.AddWithValue("@uid", alvo.Id.ToString());
            using var reader = cmd.ExecuteReader();
            var warns = new List<string>();
            while (reader.Read())
            {
                var warnId = reader.GetInt32(0);
                var modId = reader.GetString(1);
                var wMotivo = reader.GetString(2);
                var data = reader.GetDateTime(3);
                warns.Add($"`#{warnId}` | <@{modId}> | {wMotivo} | <t:{((DateTimeOffset)DateTime.SpecifyKind(data, DateTimeKind.Utc)).ToUnixTimeSeconds()}:R>");
            }
            if (warns.Count == 0) { await RespondAsync($"✅ {alvo.Mention} tá limpo, sem warns", ephemeral: true); return; }
            await RespondAsync($"⚠️ **Warns de {alvo.Username}:**\n{string.Join("\n", warns)}\n**Total: {warns.Count}/3**", ephemeral: true);
        }

        [SlashCommand("clearwarns", "Limpa avisos")]
        public async Task ClearWarnsAsync([Summary("usuario", "Usuário")] SocketGuildUser alvo)
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "warn"))
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "warn") && !user.GuildPermissions.ModerateMembers)
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            using var conn = new NpgsqlConnection(ModerationHelper.GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM warns WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", Context.Guild.Id.ToString());
            cmd.Parameters.AddWithValue("@uid", alvo.Id.ToString());
            var deleted = cmd.ExecuteNonQuery();
            await RespondAsync($"🗑️ warns de {alvo.Mention} limpos, tá zerado agr ({deleted} removidos)");
        }
    }

    public class ChannelModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("clear", "Apaga mensagens")]
        [RequireBotPermission(ChannelPermission.ManageMessages)]
        public async Task ClearAsync([Summary("quantidade", "Quantidade (1-100)")] int quantidade)
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "clear"))
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "clear") && !user.GuildPermissions.ManageMessages)
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (quantidade < 1 || quantidade > 100) { await RespondAsync("❌ coloca entre 1 e 100", ephemeral: true); return; }

            await DeferAsync(ephemeral: true);
            var channel = (ITextChannel)Context.Channel;
            var messages = await channel.GetMessagesAsync(quantidade + 1).FlattenAsync();
            var deletable = messages.Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays < 14).ToList();
            if (deletable.Count == 0) { await FollowupAsync("❌ nenhuma mensagem pra apagar", ephemeral: true); return; }
            await channel.DeleteMessagesAsync(deletable);
            await FollowupAsync($"✅ {deletable.Count - 1} mensagens apagadas, sumiu tudo kkkk", ephemeral: true);
        }

        [SlashCommand("slowmode", "Define slowmode")]
        public async Task SlowmodeAsync([Summary("segundos", "Segundos (0 para desativar)")] int segundos)
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "clear"))
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "clear") && !user.GuildPermissions.ManageChannels)
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (segundos < 0 || segundos > 21600) { await RespondAsync("❌ coloca entre 0 e 21600", ephemeral: true); return; }
            var channel = (ITextChannel)Context.Channel;
            await channel.ModifyAsync(x => x.SlowModeInterval = segundos);
            await RespondAsync(segundos > 0 ? $"🐌 slowmode de `{segundos}s` ativado, calma aí galera" : "🐌 slowmode desativado, pode spammar");
        }

        [SlashCommand("lock", "Tranca o canal")]
        public async Task LockAsync()
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "lock"))
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "lock") && !user.GuildPermissions.ManageChannels)
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Deny));
            await RespondAsync("🔒 canal trancado, ninguém fala mais aqui");
        }

        [SlashCommand("unlock", "Destranca o canal")]
        public async Task UnlockAsync()
        {
            var user = (SocketGuildUser)Context.User;

            if (!AdminModule.TemPermissao(Context.Guild.Id, user, "lock"))
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            if (!AdminModule.SistemaAtivado(Context.Guild.Id, "lock") && !user.GuildPermissions.ManageChannels)
            { await RespondAsync("❌ tu não tem permissão", ephemeral: true); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Inherit));
            await RespondAsync("🔓 canal destrancado, podem falar");
        }
    }
}
