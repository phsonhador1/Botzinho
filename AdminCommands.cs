using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;


namespace Botzinho.Admins
{
    public class AdminCommands : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("eban", "Bane um usuário por um tempo específico")]
        public async Task EbanAsync(
            [Summary("usuario", "Usuário que será banido")] SocketGuildUser usuario,
            [Summary("tempo", "Tempo: 10m, 2h, 3d")] string tempo,
            [Summary("motivo", "Motivo do banimento")] string motivo = "Sem motivo")
        {
            var executor = (SocketGuildUser)Context.User;

            if (!executor.GuildPermissions.BanMembers && !executor.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ Você não tem permissão para usar este comando.", ephemeral: true);
                return;
            }

            if (usuario.Id == executor.Id)
            {
                await RespondAsync("❌ Você não pode se banir.", ephemeral: true);
                return;
            }

            if (usuario.Id == Context.Client.CurrentUser.Id)
            {
                await RespondAsync("❌ Você não pode banir o bot.", ephemeral: true);
                return;
            }

            if (usuario.Id == Context.Guild.OwnerId)
            {
                await RespondAsync("❌ Você não pode banir o dono do servidor.", ephemeral: true);
                return;
            }

            if (usuario.GuildPermissions.Administrator && executor.Id != Context.Guild.OwnerId)
            {
                await RespondAsync("❌ Você não pode banir um administrador.", ephemeral: true);
                return;
            }

            if (usuario.Hierarchy >= executor.Hierarchy && executor.Id != Context.Guild.OwnerId)
            {
                await RespondAsync("❌ Esse usuário tem cargo igual ou maior que o seu.", ephemeral: true);
                return;
            }

            if (usuario.Hierarchy >= Context.Guild.CurrentUser.Hierarchy)
            {
                await RespondAsync("❌ Eu não consigo banir esse usuário porque o cargo dele é maior ou igual ao meu.", ephemeral: true);
                return;
            }

            if (!TryParseTempo(tempo, out var duracao))
            {
                await RespondAsync("❌ Tempo inválido. Use exemplos como: `10m`, `2h`, `3d`, `7d`.", ephemeral: true);
                return;
            }

            var expiresAt = DateTime.UtcNow.Add(duracao);

            try
            {
                await Context.Guild.AddBanAsync(usuario, 0, motivo);

                AdminBanRepository.SalvarOuAtualizarBan(new AdminBanRepository.TempBanData
                {
                    GuildId = Context.Guild.Id,
                    UserId = usuario.Id,
                    AuthorId = executor.Id,
                    Reason = motivo,
                    ExpiresAtUtc = expiresAt,
                    Active = true
                });

                var embed = new EmbedBuilder()
                    .WithAuthor("Sistema de Moderação")
                    .WithTitle("🔨 Banimento temporário aplicado")
                    .WithDescription(
                        $"**Usuário:** {usuario.DisplayName} (`{usuario.Id}`)\n" +
                        $"**Tempo:** `{tempo}`\n" +
                        $"**Expira em:** <t:{new DateTimeOffset(expiresAt).ToUnixTimeSeconds()}:F>\n" +
                        $"**Motivo:** {motivo}\n" +
                        $"**Aplicado por:** {executor.DisplayName}"
                    )
                    .WithColor(new Discord.Color(0x2B2D31))
                    .WithCurrentTimestamp()
                    .Build();

                await RespondAsync(embed: embed);
            }
            catch (Exception ex)
            {
                await RespondAsync($"❌ Erro ao banir o usuário: `{ex.Message}`", ephemeral: true);
            }
        }

        [SlashCommand("eunban", "Remove o banimento de um usuário")]
        public async Task EunbanAsync(
    [Summary("userid", "ID do usuário banido")] string userId)
        {
            var executor = (SocketGuildUser)Context.User;

            if (!executor.GuildPermissions.BanMembers && !executor.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ Você não tem permissão para usar este comando.", ephemeral: true);
                return;
            }

            if (!ulong.TryParse(userId, out var parsedUserId))
            {
                await RespondAsync("❌ ID inválido.", ephemeral: true);
                return;
            }

            try
            {
                RestBan? bannedUser = null;

                await foreach (var banPage in Context.Guild.GetBansAsync())
                {
                    bannedUser = banPage.FirstOrDefault(x => x.User.Id == parsedUserId);
                    if (bannedUser != null)
                        break;
                }

                if (bannedUser == null)
                {
                    await RespondAsync("❌ Esse usuário não está banido.", ephemeral: true);
                    return;
                }

                await Context.Guild.RemoveBanAsync(bannedUser.User);
                AdminBanRepository.DesativarBan(Context.Guild.Id, parsedUserId);

                var embed = new EmbedBuilder()
                    .WithAuthor("Sistema de Moderação")
                    .WithTitle("🔓 Banimento removido")
                    .WithDescription(
                        $"**Usuário:** `{parsedUserId}`\n" +
                        $"**Removido por:** {executor.DisplayName}"
                    )
                    .WithColor(new Discord.Color(0x2B2D31))
                    .WithCurrentTimestamp()
                    .Build();

                await RespondAsync(embed: embed);
            }
            catch (Exception ex)
            {
                await RespondAsync($"❌ Erro ao remover ban: `{ex.Message}`", ephemeral: true);
            }
        }

        private bool TryParseTempo(string input, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            input = input.Trim().ToLowerInvariant();

            if (input.Length < 2)
                return false;

            var unidade = input[^1];
            var numeroTexto = input[..^1];

            if (!double.TryParse(numeroTexto, NumberStyles.Any, CultureInfo.InvariantCulture, out var valor))
                return false;

            duration = unidade switch
            {
                'm' => TimeSpan.FromMinutes(valor),
                'h' => TimeSpan.FromHours(valor),
                'd' => TimeSpan.FromDays(valor),
                _ => TimeSpan.Zero
            };

            return duration > TimeSpan.Zero;
        }
    }
}
