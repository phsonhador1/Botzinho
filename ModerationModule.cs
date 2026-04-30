using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Botzinho.Admins;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Moderation
{
    public static class ModerationHelper
    {
        // ★ COR PADRÃO: VERMELHO em TODOS os embeds
        public static readonly Color CorEmbed = new Color(255, 71, 87);

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
            return unit switch
            {
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                'd' => TimeSpan.FromDays(value),
                _ => null
            };
        }

        // ★ Rodapé padrão: nome do servidor + data
        public static EmbedFooterBuilder RodapePadrao(SocketGuild guild)
        {
            return new EmbedFooterBuilder()
                .WithText($"Hoje às {DateTime.Now.AddHours(-3):HH:mm}")
                .WithIconUrl(guild.IconUrl);
        }

        // ★ Helpers de embed de erro
        public static Embed CriarEmbedErro(string mensagem, SocketGuild guild)
        {
            return new EmbedBuilder()
                .WithColor(CorEmbed)
                .WithDescription($"<:erro:1493078898462949526> {mensagem}")
                .WithFooter(RodapePadrao(guild))
                .Build();
        }
    }

    public class BanModule : ModuleBase<SocketCommandContext>
    {
        [Command("ban")]
        [Summary("Bane um usuario do servidor")]
        [RequireBotPermission(GuildPermission.BanMembers)]
        public async Task BanAsync(SocketGuildUser alvo, [Remainder] string motivo = "Sem motivo informado")
        {
            var user = (SocketGuildUser)Context.User;
            var guild = Context.Guild as SocketGuild;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "ban", GuildPermission.BanMembers);
            if (erro != null) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro(erro, guild)); return; }

            if (alvo.Id == user.Id) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Você não pode se banir, otário kkk", guild)); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("O cargo desse usuário é igual ou maior que o seu.", guild)); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Meu cargo é menor que o desse usuário, não consigo bani-lo.", guild)); return; }

            try { await alvo.SendMessageAsync($"<:erro:1493078898462949526> Você foi **banido** de **{Context.Guild.Name}**.\n**Motivo:** {motivo}"); } catch { }

            await Context.Guild.AddBanAsync(alvo, pruneDays: 0, reason: motivo);

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorEmbed)
                .WithAuthor("⛔ Usuário Banido", Context.Guild.IconUrl)
                .WithDescription($"O usuário `{alvo.Username}` foi **banido** do servidor.")
                .AddField("👤 Usuário", $"`{alvo.Username}` (`{alvo.Id}`)", true)
                .AddField("🛡️ Banido por", $"`{user.Username}`", true)
                .AddField("📝 Motivo", $"```{motivo}```", false)
                .WithThumbnailUrl(alvo.GetAvatarUrl() ?? alvo.GetDefaultAvatarUrl())
                .WithFooter(ModerationHelper.RodapePadrao(guild))
                .Build();

            await ReplyAsync(embed: embed);
        }
    }

    public class UnbanModule : ModuleBase<SocketCommandContext>
    {
        [Command("unban")]
        [Summary("Desbane um usuario")]
        [RequireBotPermission(GuildPermission.BanMembers)]
        public async Task UnbanAsync(string userId)
        {
            var user = (SocketGuildUser)Context.User;
            var guild = Context.Guild as SocketGuild;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "ban", GuildPermission.BanMembers);
            if (erro != null) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro(erro, guild)); return; }

            if (!ulong.TryParse(userId, out var id)) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("ID inválido.", guild)); return; }

            try
            {
                await Context.Guild.RemoveBanAsync(id);

                var embed = new EmbedBuilder()
                    .WithColor(ModerationHelper.CorEmbed)
                    .WithAuthor("✅ Usuário Desbanido", Context.Guild.IconUrl)
                    .WithDescription($"O usuário com ID `{id}` foi **desbanido** com sucesso.")
                    .AddField("🛡️ Desbanido por", $"`{user.Username}`", true)
                    .WithFooter(ModerationHelper.RodapePadrao(guild))
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch
            {
                await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Esse ID não está na lista de banidos.", guild));
            }
        }
    }

    public class KickModule : ModuleBase<SocketCommandContext>
    {
        [Command("kick")]
        [Summary("Expulsa um usuario")]
        [RequireBotPermission(GuildPermission.KickMembers)]
        public async Task KickAsync(SocketGuildUser alvo, [Remainder] string motivo = "Sem motivo informado")
        {
            var user = (SocketGuildUser)Context.User;
            var guild = Context.Guild as SocketGuild;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "kick", GuildPermission.KickMembers);
            if (erro != null) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro(erro, guild)); return; }

            if (alvo.Id == user.Id) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Você não pode se expulsar.", guild)); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("O cargo desse usuário é igual ou maior que o seu.", guild)); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Meu cargo é menor que o desse usuário.", guild)); return; }

            try { await alvo.SendMessageAsync($"<:erro:1493078898462949526> Você foi **expulso** de **{Context.Guild.Name}**.\n**Motivo:** {motivo}"); } catch { }
            await alvo.KickAsync(motivo);

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorEmbed)
                .WithAuthor("👢 Usuário Expulso", Context.Guild.IconUrl)
                .WithDescription($"O usuário `{alvo.Username}` foi **expulso** do servidor.")
                .AddField("👤 Usuário", $"`{alvo.Username}` (`{alvo.Id}`)", true)
                .AddField("🛡️ Expulso por", $"`{user.Username}`", true)
                .AddField("📝 Motivo", $"```{motivo}```", false)
                .WithThumbnailUrl(alvo.GetAvatarUrl() ?? alvo.GetDefaultAvatarUrl())
                .WithFooter(ModerationHelper.RodapePadrao(guild))
                .Build();

            await ReplyAsync(embed: embed);
        }
    }

    public class MuteModule : ModuleBase<SocketCommandContext>
    {
        [Command("mute")]
        [Alias("zmute")] // Adicionado um alias caso você costume chamar direto por zmute
        [Summary("Silencia um usuário")]
        [RequireBotPermission(GuildPermission.ModerateMembers)]
        public async Task MuteAsync(SocketGuildUser alvo, string duracao, [Remainder] string motivo = "Não informado")
        {
            var user = (SocketGuildUser)Context.User;
            var guild = Context.Guild as SocketGuild;

            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "mute", GuildPermission.ModerateMembers);
            if (erro != null) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro(erro, guild)); return; }

            if (alvo.Id == user.Id) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Você não pode se silenciar.", guild)); return; }
            if (alvo.Hierarchy >= user.Hierarchy) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("O cargo desse usuário é igual ou maior que o seu.", guild)); return; }
            if (alvo.Hierarchy >= Context.Guild.CurrentUser.Hierarchy) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Meu cargo é menor que o desse usuário.", guild)); return; }

            var tempo = ModerationHelper.ParseDuration(duracao);
            if (tempo == null || tempo.Value.TotalMinutes < 1 || tempo.Value.TotalDays > 28)
            {
                await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Duração inválida. Use: **10m**, **1h**, **1d** **(máx 28d)**.", guild));
                return;
            }

            // Aplica o timeout no usuário. O motivo vai silenciosamente para as configurações de auditoria do Discord.
            await alvo.SetTimeOutAsync(tempo.Value, new RequestOptions { AuditLogReason = motivo });

            // Embed com design limpo, premium e direto ao ponto
            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorEmbed) // Usa a cor padrão (como o vermelho que você definiu antes)
                .WithAuthor("Usuário Silenciado", alvo.GetAvatarUrl() ?? alvo.GetDefaultAvatarUrl())
                .WithDescription($"O usuário **{alvo.Username}** foi mutado por **{duracao}**.\n\nAplicado por: **{user.Username}**")
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("unmute")]
        [Alias("zunmute")]
        [Summary("Remove o silenciamento de um usuário")]
        [RequireBotPermission(GuildPermission.ModerateMembers)]
        public async Task UnmuteAsync(SocketGuildUser alvo)
        {
            var user = (SocketGuildUser)Context.User;
            var guild = Context.Guild as SocketGuild;

            // Mantive a checagem usando a key "mute" conforme o seu código original
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "mute", GuildPermission.ModerateMembers);
            if (erro != null) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro(erro, guild)); return; }

            await alvo.RemoveTimeOutAsync();

            // Embed com design limpo e premium, acompanhando o estilo do zmute
            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorEmbed)
                .WithAuthor("Mute Removido", alvo.GetAvatarUrl() ?? alvo.GetDefaultAvatarUrl())
                .WithDescription($"O mute de **{alvo.Username}** foi retirado e ele já pode digitar novamente.\n\nAção removida por {user.Username}")
                .Build();

            await ReplyAsync(embed: embed);
        }
    }

    public class ChannelModule : ModuleBase<SocketCommandContext>
    {
        [Command("clear")]
        [Summary("Apaga mensagens")]
        [RequireBotPermission(ChannelPermission.ManageMessages)]
        public async Task ClearAsync(int quantidade)
        {
            var user = (SocketGuildUser)Context.User;
            var guild = Context.Guild as SocketGuild;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "clear", GuildPermission.ManageMessages);
            if (erro != null) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro(erro, guild)); return; }

            if (quantidade < 1 || quantidade > 100) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Coloca um valor entre 1 e 100.", guild)); return; }

            var channel = (ITextChannel)Context.Channel;
            var messages = await channel.GetMessagesAsync(quantidade + 1).FlattenAsync();
            var deletable = messages.Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays < 14).ToList();

            if (deletable.Count == 0) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Nenhuma mensagem pra apagar.", guild)); return; }
            await channel.DeleteMessagesAsync(deletable);

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorEmbed)
                .WithDescription($"<a:sucess:1494692628372132013>  **{deletable.Count - 1}** mensagens foram **apagadas** com sucesso.")
                .WithFooter(ModerationHelper.RodapePadrao(guild))
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("slowmode")]
        [Summary("Define slowmode")]
        public async Task SlowmodeAsync(int segundos)
        {
            var user = (SocketGuildUser)Context.User;
            var guild = Context.Guild as SocketGuild;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "clear", GuildPermission.ManageChannels);
            if (erro != null) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro(erro, guild)); return; }

            if (segundos < 0 || segundos > 21600) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro("Valor inválido. Use entre 0 e 21600.", guild)); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.ModifyAsync(x => x.SlowModeInterval = segundos);

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorEmbed)
                .WithAuthor("Slowmode Ativado", Context.Guild.IconUrl)
                .WithDescription(segundos > 0
                    ? $"O slowmode foi definido para **{segundos}s** neste canal."
                    : "O slowmode foi **desativado** neste canal.")
                .AddField("Definido por","**{user.Username}**", true)
                .WithFooter(ModerationHelper.RodapePadrao(guild))
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("lock")]
        [Summary("Tranca o canal")]
        public async Task LockAsync()
        {
            var user = (SocketGuildUser)Context.User;
            var guild = Context.Guild as SocketGuild;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "lock", GuildPermission.ManageChannels);
            if (erro != null) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro(erro, guild)); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Deny));

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorEmbed)
                .WithAuthor("Canal Trancado", Context.Guild.IconUrl)
                .WithDescription($"Este canal foi **trancado** por **{user.Username}**\n\nApenas membros com permissão poderão enviar mensagens.")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithFooter(ModerationHelper.RodapePadrao(guild))
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("unlock")]
        [Summary("Destranca o canal")]
        public async Task UnlockAsync()
        {
            var user = (SocketGuildUser)Context.User;
            var guild = Context.Guild as SocketGuild;
            var erro = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "lock", GuildPermission.ManageChannels);
            if (erro != null) { await ReplyAsync(embed: ModerationHelper.CriarEmbedErro(erro, guild)); return; }

            var channel = (ITextChannel)Context.Channel;
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Inherit));

            var embed = new EmbedBuilder()
                .WithColor(ModerationHelper.CorEmbed)
                .WithAuthor("Canal Destrancado", Context.Guild.IconUrl)
                .WithDescription($"Este canal foi **destrancado** por **{user.Username}**\n\nMembros podem enviar mensagens novamente.")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithFooter(ModerationHelper.RodapePadrao(guild))
                .Build();

            await ReplyAsync(embed: embed);
        }
    }
}
