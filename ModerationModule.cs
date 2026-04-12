using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Botzinho.Admins;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Moderation
{
    public static class ModerationHelper
    {
        public static string GetConnectionString()
        {
            return Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? throw new Exception("DATABASE_URL nao configurado!");
        }

        public static void InicializarTabelas()
        {
            // sem tabelas de warns
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
        [SlashCommand("ban", "Bane um usuario do servidor")]
        [RequireBotPermission(GuildPermission.BanMembers)]
        public async Task BanAsync(
            [Summary("usuario", "Usuario para banir")] SocketGuildUser alvo,
            [Summary("motivo", "Motivo do ban")] string motivo = "Sem motivo informado",
            [Summary("dias", "Dias de mensagens para apagar (0-7)")] int dias = 0)
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "ban", GuildPermission.BanMembers);
            if (erro != null) { await RespondAsync(erro, ephemeral: true); return; }

            if (alvo.Id == user.Id) { await RespondAsync("tu quer se banir? kkkk nao da", ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await RespondAsync("cargo igual ou maior", ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await RespondAsync("meu cargo e menor", ephemeral: true); return; }

            try { await alvo.SendMessageAsync($"tu foi banido de **{Context.Guild.Name}** kkkk"); } catch { }
            await Context.Guild.AddBanAsync(alvo, pruneDays: Math.Clamp(dias, 0, 7), reason: motivo);
            await RespondAsync($"kkkkkkkkkkkk {alvo.Mention} banido, vaza mlk\n**Motivo:** {motivo}");
        }
    }

    public class UnbanModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("unban", "Desbane um usuario")]
        [RequireBotPermission(GuildPermission.BanMembers)]
        public async Task UnbanAsync([Summary("id", "ID do usuario")] string userId)
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "ban", GuildPermission.BanMembers);
            if (erro != null) { await RespondAsync(erro, ephemeral: true); return; }

            if (!ulong.TryParse(userId, out var id)) { await RespondAsync("ID invalido", ephemeral: true); return; }
            try { await Context.Guild.RemoveBanAsync(id); await RespondAsync($"ID `{id}` desbanido, volta ai mano"); }
            catch { await RespondAsync("esse ID nao ta na lista de bans", ephemeral: true); }
        }
    }

    public class KickModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("kick", "Expulsa um usuario")]
        [RequireBotPermission(GuildPermission.KickMembers)]
        public async Task KickAsync(
            [Summary("usuario", "Usuario para expulsar")] SocketGuildUser alvo,
            [Summary("motivo", "Motivo")] string motivo = "Sem motivo informado")
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "kick", GuildPermission.KickMembers);
            if (erro != null) { await RespondAsync(erro, ephemeral: true); return; }

            if (alvo.Id == user.Id) { await RespondAsync("quer se kickar? kkkk", ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await RespondAsync("cargo igual ou maior", ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await RespondAsync("meu cargo e menor", ephemeral: true); return; }

            try { await alvo.SendMessageAsync($"tu foi kickado de **{Context.Guild.Name}** kkkk"); } catch { }
            await alvo.KickAsync(motivo);
            await RespondAsync($"{alvo.Mention} tomou kick kkkk vaza daqui\n**Motivo:** {motivo}");
        }
    }

    public class MuteModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("mute", "Silencia um usuario")]
        [RequireBotPermission(GuildPermission.ModerateMembers)]
        public async Task MuteAsync(
            [Summary("usuario", "Usuario")] SocketGuildUser alvo,
            [Summary("duracao", "Duracao (10m, 1h, 1d)")] string duracao,
            [Summary("motivo", "Motivo")] string motivo = "Sem motivo informado")
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "mute", GuildPermission.ModerateMembers);
            if (erro != null) { await RespondAsync(erro, ephemeral: true); return; }

            if (alvo.Id == user.Id) { await RespondAsync("quer se mutar? kkkk", ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await RespondAsync("cargo igual ou maior", ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await RespondAsync("meu cargo e menor", ephemeral: true); return; }

            var tempo = ModerationHelper.ParseDuration(duracao);
            if (tempo == null || tempo.Value.TotalMinutes < 1 || tempo.Value.TotalDays > 28)
            { await RespondAsync("duracao invalida, usa: `10m`, `1h`, `1d` (max 28d)", ephemeral: true); return; }

            await alvo.SetTimeOutAsync(tempo.Value, new RequestOptions { AuditLogReason = motivo });
            await RespondAsync($"{alvo.Mention} calado por `{duracao}` kkkkk silencio otario\n**Motivo:** {motivo}");
        }

        [SlashCommand("unmute", "Remove silenciamento")]
        [RequireBotPermission(GuildPermission.ModerateMembers)]
        public async Task UnmuteAsync([Summary("usuario", "Usuario")] SocketGuildUser alvo)
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "mute", GuildPermission.ModerateMembers);
            if (erro != null) { await RespondAsync(erro, ephemeral: true); return; }

            await alvo.RemoveTimeOutAsync();
            await RespondAsync($"{alvo.Mention} pode falar de novo, se comporta agr");
        }
    }

    public class ChannelModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("clear", "Apaga mensagens")]
        [RequireBotPermission(ChannelPermission.ManageMessages)]
        public async Task ClearAsync([Summary("quantidade", "Quantidade (1-100)")] int quantidade)
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "clear", GuildPermission.ManageMessages);
            if (erro != null) { await RespondAsync(erro, ephemeral: true); return; }

            if (quantidade < 1 || quantidade > 100) { await RespondAsync("coloca entre 1 e 100", ephemeral: true); return; }

            await DeferAsync(ephemeral: true);
            var channel = (ITextChannel)Context.Channel;
            var messages = await channel.GetMessagesAsync(quantidade + 1).FlattenAsync();
            var deletable = messages.Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays < 14).ToList();
            if (deletable.Count == 0) { await FollowupAsync("nenhuma mensagem pra apagar", ephemeral: true); return; }
            await channel.DeleteMessagesAsync(deletable);
            await FollowupAsync($"{deletable.Count - 1} mensagens apagadas, sumiu tudo kkkk", ephemeral: true);
        }

        [SlashCommand("slowmode", "Define slowmode")]
        public async Task SlowmodeAsync([Summary("segundos", "Segundos (0 para desativar)")] int segundos)
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "clear", GuildPermission.ManageChannels);
            if (erro != null) { await RespondAsync(erro, ephemeral: true); return; }

            if (segundos < 0 || segundos > 21600) { await RespondAsync("coloca entre 0 e 21600", ephemeral: true); return; }
            var channel = (ITextChannel)Context.Channel;
            await channel.ModifyAsync(x => x.SlowModeInterval = segundos);
            await RespondAsync(segundos > 0 ? $"slowmode de `{segundos}s` ativado, calma ai galera" : "slowmode desativado, pode spammar");
        }

        [SlashCommand("lock", "Tranca o canal")]
        public async Task LockAsync()
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "lock", GuildPermission.ManageChannels);
            if (erro != null) { await RespondAsync(erro, ephemeral: true); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Deny));
            await RespondAsync("canal trancado, ninguem fala mais aqui");
        }

        [SlashCommand("unlock", "Destranca o canal")]
        public async Task UnlockAsync()
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "lock", GuildPermission.ManageChannels);
            if (erro != null) { await RespondAsync(erro, ephemeral: true); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Inherit));
            await RespondAsync("canal destrancado, podem falar");
        }
    }
}
