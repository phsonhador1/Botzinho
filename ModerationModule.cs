using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Npgsql;
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
            if (string.IsNullOrEmpty(input) || input.Length < 2)
                return null;

            var unit = input[^1];
            if (!int.TryParse(input[..^1], out var value) || value <= 0)
                return null;

            return unit switch
            {
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                'd' => TimeSpan.FromDays(value),
                _ => null
            };
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

            if (!user.GuildPermissions.BanMembers)
            { await RespondAsync("❌ Você não tem permissão para banir.", ephemeral: true); return; }
            if (alvo.Id == user.Id)
            { await RespondAsync("❌ Você não pode se banir.", ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy)
            { await RespondAsync("❌ Você não pode banir alguém com cargo igual ou superior.", ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy)
            { await RespondAsync("❌ Meu cargo é inferior ao desse usuário.", ephemeral: true); return; }

            try
            {
                var dmEmbed = new EmbedBuilder()
                    .WithTitle($"🔨 Você foi banido de {Context.Guild.Name}")
                    .WithDescription($"**Motivo:** {motivo}")
                    .WithColor(new Discord.Color(0xED4245))
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();
                try { await alvo.SendMessageAsync(embed: dmEmbed); } catch { }
            }
            catch { }

            await Context.Guild.AddBanAsync(alvo, pruneDays: Math.Clamp(dias, 0, 7), reason: motivo);

            var embed = new EmbedBuilder()
                .WithTitle("🔨 Usuário Banido")
                .WithDescription(
                    $"**Usuário:** {alvo.Mention} (`{alvo.Id}`)\n" +
                    $"**Moderador:** {user.Mention}\n" +
                    $"**Dias apagados:** `{Math.Clamp(dias, 0, 7)}`\n" +
                    $"**Motivo:** {motivo}")
                .WithColor(new Discord.Color(0xED4245))
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await RespondAsync(embed: embed);
        }
    }

    public class UnbanModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("unban", "Desbane um usuário do servidor")]
        [RequireBotPermission(GuildPermission.BanMembers)]
        public async Task UnbanAsync(
            [Summary("id", "ID do usuário para desbanir")] string userId)
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.BanMembers)
            { await RespondAsync("❌ Você não tem permissão para desbanir.", ephemeral: true); return; }

            if (!ulong.TryParse(userId, out var id))
            { await RespondAsync("❌ ID inválido.", ephemeral: true); return; }

            try
            {
                await Context.Guild.RemoveBanAsync(id);
                var embed = new EmbedBuilder()
                    .WithTitle("✅ Usuário Desbanido")
                    .WithDescription($"**ID:** `{id}`\n**Moderador:** {user.Mention}")
                    .WithColor(new Discord.Color(0x57F287))
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();
                await RespondAsync(embed: embed);
            }
            catch
            {
                await RespondAsync("❌ Usuário não encontrado na lista de bans.", ephemeral: true);
            }
        }
    }

    public class KickModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("kick", "Expulsa um usuário do servidor")]
        [RequireBotPermission(GuildPermission.KickMembers)]
        public async Task KickAsync(
            [Summary("usuario", "Usuário para expulsar")] SocketGuildUser alvo,
            [Summary("motivo", "Motivo da expulsão")] string motivo = "Sem motivo informado")
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.KickMembers)
            { await RespondAsync("❌ Você não tem permissão para expulsar.", ephemeral: true); return; }
            if (alvo.Id == user.Id)
            { await RespondAsync("❌ Você não pode se expulsar.", ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy)
            { await RespondAsync("❌ Você não pode expulsar alguém com cargo igual ou superior.", ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy)
            { await RespondAsync("❌ Meu cargo é inferior ao desse usuário.", ephemeral: true); return; }

            try
            {
                var dmEmbed = new EmbedBuilder()
                    .WithTitle($"👢 Você foi expulso de {Context.Guild.Name}")
                    .WithDescription($"**Motivo:** {motivo}")
                    .WithColor(new Discord.Color(0xFEE75C))
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();
                try { await alvo.SendMessageAsync(embed: dmEmbed); } catch { }
            }
            catch { }

            await alvo.KickAsync(motivo);

            var embed = new EmbedBuilder()
                .WithTitle("👢 Usuário Expulso")
                .WithDescription(
                    $"**Usuário:** {alvo.Mention} (`{alvo.Id}`)\n" +
                    $"**Moderador:** {user.Mention}\n" +
                    $"**Motivo:** {motivo}")
                .WithColor(new Discord.Color(0xFEE75C))
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await RespondAsync(embed: embed);
        }
    }

    public class MuteModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("mute", "Silencia um usuário por um tempo")]
        [RequireBotPermission(GuildPermission.ModerateMembers)]
        public async Task MuteAsync(
            [Summary("usuario", "Usuário para silenciar")] SocketGuildUser alvo,
            [Summary("duracao", "Duração (ex: 10m, 1h, 1d)")] string duracao,
            [Summary("motivo", "Motivo do mute")] string motivo = "Sem motivo informado")
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.ModerateMembers)
            { await RespondAsync("❌ Você não tem permissão para silenciar.", ephemeral: true); return; }
            if (alvo.Id == user.Id)
            { await RespondAsync("❌ Você não pode se silenciar.", ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy)
            { await RespondAsync("❌ Você não pode silenciar alguém com cargo igual ou superior.", ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy)
            { await RespondAsync("❌ Meu cargo é inferior ao desse usuário.", ephemeral: true); return; }

            var tempo = ModerationHelper.ParseDuration(duracao);
            if (tempo == null || tempo.Value.TotalMinutes < 1 || tempo.Value.TotalDays > 28)
            { await RespondAsync("❌ Duração inválida. Use: `10m`, `1h`, `1d` (máx 28d)", ephemeral: true); return; }

            await alvo.SetTimeOutAsync(tempo.Value, new RequestOptions { AuditLogReason = motivo });

            var embed = new EmbedBuilder()
                .WithTitle("🔇 Usuário Silenciado")
                .WithDescription(
                    $"**Usuário:** {alvo.Mention} (`{alvo.Id}`)\n" +
                    $"**Moderador:** {user.Mention}\n" +
                    $"**Duração:** `{duracao}`\n" +
                    $"**Motivo:** {motivo}")
                .WithColor(new Discord.Color(0xEB459E))
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await RespondAsync(embed: embed);
        }

        [SlashCommand("unmute", "Remove o silenciamento de um usuário")]
        [RequireBotPermission(GuildPermission.ModerateMembers)]
        public async Task UnmuteAsync(
            [Summary("usuario", "Usuário para dessilenciar")] SocketGuildUser alvo)
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.ModerateMembers)
            { await RespondAsync("❌ Você não tem permissão.", ephemeral: true); return; }

            await alvo.RemoveTimeOutAsync();

            var embed = new EmbedBuilder()
                .WithTitle("🔊 Silenciamento Removido")
                .WithDescription(
                    $"**Usuário:** {alvo.Mention}\n" +
                    $"**Moderador:** {user.Mention}")
                .WithColor(new Discord.Color(0x57F287))
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await RespondAsync(embed: embed);
        }
    }

    public class WarnModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("warn", "Dá um aviso a um usuário")]
        public async Task WarnAsync(
            [Summary("usuario", "Usuário para avisar")] SocketGuildUser alvo,
            [Summary("motivo", "Motivo do aviso")] string motivo = "Sem motivo informado")
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.ModerateMembers)
            { await RespondAsync("❌ Você não tem permissão.", ephemeral: true); return; }
            if (alvo.IsBot)
            { await RespondAsync("❌ Não é possível avisar um bot.", ephemeral: true); return; }

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

            var embed = new EmbedBuilder()
                .WithTitle("⚠️ Aviso")
                .WithDescription(
                    $"**Usuário:** {alvo.Mention} (`{alvo.Id}`)\n" +
                    $"**Moderador:** {user.Mention}\n" +
                    $"**Motivo:** {motivo}\n" +
                    $"**Total de avisos:** `{totalWarns}/3`")
                .WithColor(new Discord.Color(0xFEE75C))
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await RespondAsync(embed: embed);

            if (totalWarns >= 3)
            {
                try
                {
                    try
                    {
                        var dmEmbed = new EmbedBuilder()
                            .WithTitle($"🔨 Você foi banido de {Context.Guild.Name}")
                            .WithDescription("**Motivo:** Acumulou 3 avisos.")
                            .WithColor(new Discord.Color(0xED4245))
                            .Build();
                        await alvo.SendMessageAsync(embed: dmEmbed);
                    }
                    catch { }

                    await Context.Guild.AddBanAsync(alvo, reason: "Acumulou 3 avisos.");

                    var banEmbed = new EmbedBuilder()
                        .WithTitle("🔨 Ban Automático")
                        .WithDescription($"{alvo.Mention} foi banido automaticamente por acumular **3 avisos**.")
                        .WithColor(new Discord.Color(0xED4245))
                        .Build();

                    await Context.Channel.SendMessageAsync(embed: banEmbed);

                    using var delCmd = conn.CreateCommand();
                    delCmd.CommandText = "DELETE FROM warns WHERE guild_id = @gid AND user_id = @uid";
                    delCmd.Parameters.AddWithValue("@gid", Context.Guild.Id.ToString());
                    delCmd.Parameters.AddWithValue("@uid", alvo.Id.ToString());
                    delCmd.ExecuteNonQuery();
                }
                catch (Exception ex) { Console.WriteLine($"Erro no ban automático: {ex.Message}"); }
            }
        }

        [SlashCommand("warns", "Mostra os avisos de um usuário")]
        public async Task WarnsAsync(
            [Summary("usuario", "Usuário para ver avisos")] SocketGuildUser alvo)
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.ModerateMembers)
            { await RespondAsync("❌ Você não tem permissão.", ephemeral: true); return; }

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
                var motivo = reader.GetString(2);
                var data = reader.GetDateTime(3);
                warns.Add($"`#{warnId}` | <@{modId}> | {motivo} | <t:{((DateTimeOffset)DateTime.SpecifyKind(data, DateTimeKind.Utc)).ToUnixTimeSeconds()}:R>");
            }

            if (warns.Count == 0)
            { await RespondAsync($"✅ {alvo.Mention} não tem avisos.", ephemeral: true); return; }

            var embed = new EmbedBuilder()
                .WithTitle($"⚠️ Avisos de {alvo.Username}")
                .WithDescription(string.Join("\n", warns))
                .WithFooter($"Total: {warns.Count}/3")
                .WithColor(new Discord.Color(0xFEE75C))
                .Build();

            await RespondAsync(embed: embed, ephemeral: true);
        }

        [SlashCommand("clearwarns", "Limpa todos os avisos de um usuário")]
        public async Task ClearWarnsAsync(
            [Summary("usuario", "Usuário para limpar avisos")] SocketGuildUser alvo)
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.ModerateMembers)
            { await RespondAsync("❌ Você não tem permissão.", ephemeral: true); return; }

            using var conn = new NpgsqlConnection(ModerationHelper.GetConnectionString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM warns WHERE guild_id = @gid AND user_id = @uid";
            cmd.Parameters.AddWithValue("@gid", Context.Guild.Id.ToString());
            cmd.Parameters.AddWithValue("@uid", alvo.Id.ToString());
            var deleted = cmd.ExecuteNonQuery();

            var embed = new EmbedBuilder()
                .WithTitle("🗑️ Avisos Limpos")
                .WithDescription(
                    $"**Usuário:** {alvo.Mention}\n" +
                    $"**Avisos removidos:** `{deleted}`\n" +
                    $"**Moderador:** {user.Mention}")
                .WithColor(new Discord.Color(0x57F287))
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await RespondAsync(embed: embed);
        }
    }

    public class ChannelModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("clear", "Apaga mensagens do canal")]
        [RequireBotPermission(ChannelPermission.ManageMessages)]
        public async Task ClearAsync(
            [Summary("quantidade", "Quantidade de mensagens (1-100)")] int quantidade)
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.ManageMessages)
            { await RespondAsync("❌ Você não tem permissão.", ephemeral: true); return; }

            if (quantidade < 1 || quantidade > 100)
            { await RespondAsync("❌ Quantidade entre 1 e 100.", ephemeral: true); return; }

            await DeferAsync(ephemeral: true);

            var channel = (ITextChannel)Context.Channel;
            var messages = await channel.GetMessagesAsync(quantidade + 1).FlattenAsync();
            var deletable = messages.Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays < 14).ToList();

            if (deletable.Count == 0)
            { await FollowupAsync("❌ Nenhuma mensagem encontrada para apagar.", ephemeral: true); return; }

            await channel.DeleteMessagesAsync(deletable);
            await FollowupAsync($"✅ `{deletable.Count - 1}` mensagens apagadas.", ephemeral: true);
        }

        [SlashCommand("slowmode", "Define o slowmode do canal")]
        public async Task SlowmodeAsync(
            [Summary("segundos", "Segundos de slowmode (0 para desativar)")] int segundos)
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.ManageChannels)
            { await RespondAsync("❌ Você não tem permissão.", ephemeral: true); return; }

            if (segundos < 0 || segundos > 21600)
            { await RespondAsync("❌ Valor entre 0 e 21600 segundos.", ephemeral: true); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.ModifyAsync(x => x.SlowModeInterval = segundos);

            await RespondAsync(embed: new EmbedBuilder()
                .WithDescription(segundos > 0 ? $"🐌 Slowmode definido para `{segundos}s`" : "🐌 Slowmode desativado")
                .WithColor(new Discord.Color(0x2B2D31))
                .Build());
        }

        [SlashCommand("lock", "Tranca o canal")]
        public async Task LockAsync()
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.ManageChannels)
            { await RespondAsync("❌ Você não tem permissão.", ephemeral: true); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole,
                new OverwritePermissions(sendMessages: PermValue.Deny));

            await RespondAsync(embed: new EmbedBuilder()
                .WithDescription("🔒 Canal trancado.")
                .WithColor(new Discord.Color(0x2B2D31))
                .Build());
        }

        [SlashCommand("unlock", "Destranca o canal")]
        public async Task UnlockAsync()
        {
            var user = (SocketGuildUser)Context.User;

            if (!user.GuildPermissions.ManageChannels)
            { await RespondAsync("❌ Você não tem permissão.", ephemeral: true); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole,
                new OverwritePermissions(sendMessages: PermValue.Inherit));

            await RespondAsync(embed: new EmbedBuilder()
                .WithDescription("🔓 Canal destrancado.")
                .WithColor(new Discord.Color(0x2B2D31))
                .Build());
        }
    }
}
