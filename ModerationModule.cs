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
        // Cores padrão dos embeds
        public static readonly Color CorSucesso = new Color(67, 181, 129);   // Verde
        public static readonly Color CorPerigo = new Color(255, 71, 87);     // Vermelho
        public static readonly Color CorAviso = new Color(255, 180, 0);      // Amarelo/Laranja
        public static readonly Color CorInfo = new Color(160, 80, 220);      // Roxo Zoe
        public static readonly Color CorNeutra = new Color(43, 45, 49);      // Cinza Discord

        public static string GetConnectionString()
        {
            return Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? throw new Exception("DATABASE_URL nao configurado!");
        }

        public static void InicializarTabelas() { /* sem tabelas */ }

        public static TimeSpan? ParseDuration(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length < 2) return null;
            var unit = input[^1];
            if (!int.TryParse(input[..^1], out var value) || value <= 0) return null;
            return unit switch { 'm' => TimeSpan.FromMinutes(value), 'h' => TimeSpan.FromHours(value), 'd' => TimeSpan.FromDays(value), _ => null };
        }

        // ★ Helper: cria embed de erro padrão
        public static Embed CriarEmbedErro(string mensagem)
        {
            return new EmbedBuilder()
                .WithColor(CorPerigo)
                .WithDescription($"<:erro:1493078898462949526> {mensagem}")
                .Build();
        }

        // ★ Helper: cria embed de aviso padrão
        public static Embed CriarEmbedAviso(string mensagem)
        {
            return new EmbedBuilder()
                .WithColor(CorAviso)
                .WithDescription($"<:aviso:1493365148323152034> {mensagem}")
                .Build();
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
            if (erro != null) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro(erro), ephemeral: true); return; }

            if (alvo.Id == user.Id) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("Você não pode se banir, otário kkk"), ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("O cargo desse usuário é igual ou maior que o seu."), ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("Meu cargo é menor que o desse usuário, não consigo bani-lo."), ephemeral: true); return; }

            try { await alvo.SendMessageAsync($"<:erro:1493078898462949526> Você foi **banido** de **{Context.Guild.Name}**.\n**Motivo:** {motivo}"); } catch { }
            await Context.Guild.AddBanAsync(alvo, pruneDays: Math.Clamp(dias, 0, 7), reason: motivo);

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorPerigo)
                .WithAuthor("⛔ Usuário Banido", Context.Guild.IconUrl)
                .WithDescription($"O usuário {alvo.Mention} foi **banido** do servidor.")
                .AddField("👤 Usuário", $"`{alvo.Username}` (`{alvo.Id}`)", true)
                .AddField("🛡️ Banido por", $"`{user.Username}`", true)
                .AddField("📝 Motivo", $"```{motivo}```", false)
                .WithThumbnailUrl(alvo.GetAvatarUrl() ?? alvo.GetDefaultAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(embed: embed);
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
            if (erro != null) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro(erro), ephemeral: true); return; }

            if (!ulong.TryParse(userId, out var id)) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("ID inválido."), ephemeral: true); return; }

            try
            {
                await Context.Guild.RemoveBanAsync(id);

                var embed = new EmbedBuilder()
                    .WithColor(ModerationHelper.CorSucesso)
                    .WithAuthor("✅ Usuário Desbanido", Context.Guild.IconUrl)
                    .WithDescription($"O usuário com ID `{id}` foi **desbanido** com sucesso.")
                    .AddField("🛡️ Desbanido por", $"`{user.Username}`", true)
                    .WithCurrentTimestamp()
                    .Build();

                await RespondAsync(embed: embed);
            }
            catch
            {
                await RespondAsync(embed: ModerationHelper.CriarEmbedErro("Esse ID não está na lista de banidos."), ephemeral: true);
            }
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
            if (erro != null) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro(erro), ephemeral: true); return; }

            if (alvo.Id == user.Id) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("Você não pode se expulsar."), ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("O cargo desse usuário é igual ou maior que o seu."), ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("Meu cargo é menor que o desse usuário."), ephemeral: true); return; }

            try { await alvo.SendMessageAsync($"<:erro:1493078898462949526> Você foi **expulso** de **{Context.Guild.Name}**.\n**Motivo:** {motivo}"); } catch { }
            await alvo.KickAsync(motivo);

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorAviso)
                .WithAuthor("👢 Usuário Expulso", Context.Guild.IconUrl)
                .WithDescription($"O usuário {alvo.Mention} foi **expulso** do servidor.")
                .AddField("👤 Usuário", $"`{alvo.Username}` (`{alvo.Id}`)", true)
                .AddField("🛡️ Expulso por", $"`{user.Username}`", true)
                .AddField("📝 Motivo", $"```{motivo}```", false)
                .WithThumbnailUrl(alvo.GetAvatarUrl() ?? alvo.GetDefaultAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(embed: embed);
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
            if (erro != null) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro(erro), ephemeral: true); return; }

            if (alvo.Id == user.Id) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("Você não pode se silenciar."), ephemeral: true); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("O cargo desse usuário é igual ou maior que o seu."), ephemeral: true); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("Meu cargo é menor que o desse usuário."), ephemeral: true); return; }

            var tempo = ModerationHelper.ParseDuration(duracao);
            if (tempo == null || tempo.Value.TotalMinutes < 1 || tempo.Value.TotalDays > 28)
            {
                await RespondAsync(embed: ModerationHelper.CriarEmbedErro("Duração inválida. Use: `10m`, `1h`, `1d` (máx 28d)."), ephemeral: true);
                return;
            }

            await alvo.SetTimeOutAsync(tempo.Value, new RequestOptions { AuditLogReason = motivo });

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorAviso)
                .WithAuthor("🔇 Usuário Silenciado", Context.Guild.IconUrl)
                .WithDescription($"O usuário {alvo.Mention} foi **silenciado** por `{duracao}`.")
                .AddField("👤 Usuário", $"`{alvo.Username}` (`{alvo.Id}`)", true)
                .AddField("⏱️ Duração", $"`{duracao}`", true)
                .AddField("🛡️ Silenciado por", $"`{user.Username}`", true)
                .AddField("📝 Motivo", $"```{motivo}```", false)
                .WithThumbnailUrl(alvo.GetAvatarUrl() ?? alvo.GetDefaultAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(embed: embed);
        }

        [SlashCommand("unmute", "Remove silenciamento")]
        [RequireBotPermission(GuildPermission.ModerateMembers)]
        public async Task UnmuteAsync([Summary("usuario", "Usuario")] SocketGuildUser alvo)
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "mute", GuildPermission.ModerateMembers);
            if (erro != null) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro(erro), ephemeral: true); return; }

            await alvo.RemoveTimeOutAsync();

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorSucesso)
                .WithAuthor("🔊 Silenciamento Removido", Context.Guild.IconUrl)
                .WithDescription($"O usuário {alvo.Mention} pode falar novamente.")
                .AddField("👤 Usuário", $"`{alvo.Username}`", true)
                .AddField("🛡️ Removido por", $"`{user.Username}`", true)
                .WithThumbnailUrl(alvo.GetAvatarUrl() ?? alvo.GetDefaultAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(embed: embed);
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
            if (erro != null) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro(erro), ephemeral: true); return; }

            if (quantidade < 1 || quantidade > 100) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("Coloca um valor entre 1 e 100."), ephemeral: true); return; }

            await DeferAsync(ephemeral: true);
            var channel = (ITextChannel)Context.Channel;
            var messages = await channel.GetMessagesAsync(quantidade + 1).FlattenAsync();
            var deletable = messages.Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays < 14).ToList();

            if (deletable.Count == 0) { await FollowupAsync(embed: ModerationHelper.CriarEmbedErro("Nenhuma mensagem pra apagar."), ephemeral: true); return; }
            await channel.DeleteMessagesAsync(deletable);

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorSucesso)
                .WithDescription($"<a:sucess:1494692628372132013> **{deletable.Count - 1}** mensagens foram apagadas com sucesso.")
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true);
        }

        [SlashCommand("slowmode", "Define slowmode")]
        public async Task SlowmodeAsync([Summary("segundos", "Segundos (0 para desativar)")] int segundos)
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "clear", GuildPermission.ManageChannels);
            if (erro != null) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro(erro), ephemeral: true); return; }

            if (segundos < 0 || segundos > 21600) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro("Valor inválido. Use entre 0 e 21600."), ephemeral: true); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.ModifyAsync(x => x.SlowModeInterval = segundos);

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorInfo)
                .WithAuthor("⏱️ Slowmode Atualizado", Context.Guild.IconUrl)
                .WithDescription(segundos > 0
                    ? $"O slowmode foi definido para `{segundos}s` neste canal."
                    : "O slowmode foi **desativado** neste canal.")
                .AddField("🛡️ Alterado por", $"`{user.Username}`", true)
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(embed: embed);
        }

        [SlashCommand("lock", "Tranca o canal")]
        public async Task LockAsync()
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "lock", GuildPermission.ManageChannels);
            if (erro != null) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro(erro), ephemeral: true); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Deny));

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorPerigo)
                .WithAuthor("<:bloquearzeus:1499124950596849664> Canal Trancado", Context.Guild.IconUrl)
                .WithDescription($"Este canal foi **trancado** por `{user.Username}`.\n\nApenas membros com permissão poderão enviar mensagens.")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(embed: embed);
        }

        [SlashCommand("unlock", "Destranca o canal")]
        public async Task UnlockAsync()
        {
            var user = (SocketGuildUser)Context.User;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "lock", GuildPermission.ManageChannels);
            if (erro != null) { await RespondAsync(embed: ModerationHelper.CriarEmbedErro(erro), ephemeral: true); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Inherit));

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorSucesso)
                .WithAuthor("🔓 Canal Destrancado", Context.Guild.IconUrl)
                .WithDescription($"Este canal foi **destrancado** por `{user.Username}`.\n\nVocês podem enviar mensagens novamente.")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await RespondAsync(embed: embed);
        }
    }
}
