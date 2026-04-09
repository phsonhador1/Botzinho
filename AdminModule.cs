using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Admins
{
    public class AdminModule
    {
        private readonly DiscordSocketClient _client;
        public static Dictionary<ulong, NukeConfig> Configs = new();
        private static Dictionary<ulong, (ulong channelId, ulong messageId)> PainelMessages = new();

        public AdminModule(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
            _client.SelectMenuExecuted += HandleSelectMenu;
        }

        public class NukeConfig
        {
            public bool Ativado { get; set; } = false;
            public List<ulong> CargosPermitidos { get; set; } = new();
            public List<ulong> MembrosPermitidos { get; set; } = new();
            public List<ulong> UsuariosBloqueados { get; set; } = new();
            public List<ulong> CargosBloqueados { get; set; } = new();
        }

        private NukeConfig GetConfig(ulong guildId)
        {
            if (!Configs.ContainsKey(guildId))
                Configs[guildId] = new NukeConfig();
            return Configs[guildId];
        }

        private Embed CriarEmbedPainel(SocketGuild guild)
        {
            var config = GetConfig(guild.Id);
            var botUser = guild.CurrentUser;

            var statusText = config.Ativado ? "Ativado" : "Desativado";
            var cargosText = config.CargosPermitidos.Count > 0
                ? string.Join(", ", config.CargosPermitidos.Select(x => $"<@&{x}>"))
                : "Padrão (Gerenciar Canais)";
            var membrosText = config.MembrosPermitidos.Count > 0
                ? string.Join(", ", config.MembrosPermitidos.Select(x => $"<@{x}>"))
                : "Padrão (Gerenciar Canais)";
            var bloqueadosText = config.UsuariosBloqueados.Count > 0
                ? string.Join(", ", config.UsuariosBloqueados.Select(x => $"<@{x}>"))
                : "Nenhum";
            var cargosBloqText = config.CargosBloqueados.Count > 0
                ? string.Join(", ", config.CargosBloqueados.Select(x => $"<@&{x}>"))
                : "Nenhum";

            return new EmbedBuilder()
                .WithAuthor($"Nuke Config | {botUser.DisplayName}", botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                .WithThumbnailUrl(botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                .WithDescription(
                    "• 🛡️ **Bem-vindo(a) ao sistema de configuração do Nuke!**\n" +
                    "   ○ Configure quem pode usar o comando `/nuke` no seu servidor. " +
                    "Ative ou desative conforme necessário, restrinja o uso a cargos ou membros específicos, " +
                    "ou bloqueie usuários/cargos. Utilize o **menu abaixo** para configurar.\n" +
                    "   ○ ⚠️ Se nenhum cargo/membro for definido, o comportamento padrão (permissão Gerenciar Canais) será usado.\n\n" +
                    "• 🔧 **Informações sobre o sistema:**\n" +
                    $"   ○ **Status**: {statusText}\n" +
                    $"   ○ **Cargos Permitidos**: {cargosText}\n" +
                    $"   ○ **Membros Permitidos**: {membrosText}\n" +
                    $"   ○ **Usuários Bloqueados**: {bloqueadosText}\n" +
                    $"   ○ **Cargos Bloqueados**: {cargosBloqText}\n\n" +
                    "🌿 Em caso de dúvidas ou bugs, não hesite em entrar em meu servidor de suporte."
                )
                .WithFooter($"Servidor de {guild.Name} • Hoje às {DateTime.Now:HH:mm}")
                .WithColor(new Discord.Color(0x2B2D31))
                .Build();
        }

        private MessageComponent CriarMenuPainel()
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId("nuke_config_menu")
                .WithPlaceholder("Selecione a opção desejada para configurar.")
                .AddOption("Ativar", "toggle", "Ative o sistema de nuke.", new Emoji("🛡️"))
                .AddOption("Adicionar cargos permitidos", "add_role", "Adicione à lista cargos que podem usar o /nuke.", new Emoji("➕"))
                .AddOption("Remover cargos permitidos", "remove_role", "Remova da lista cargos permitidos.", new Emoji("➖"))
                .AddOption("Adicionar membros permitidos", "add_member", "Adicione à lista membros que podem usar o /nuke.", new Emoji("👤"))
                .AddOption("Remover membros permitidos", "remove_member", "Remova da lista membros permitidos.", new Emoji("🚫"))
                .AddOption("Bloquear usuário", "block_user", "Bloqueie um usuário de usar o /nuke.", new Emoji("🔒"))
                .AddOption("Desbloquear usuário", "unblock_user", "Desbloqueie um usuário.", new Emoji("🔓"))
                .AddOption("Bloquear cargo", "block_role", "Bloqueie um cargo de usar o /nuke.", new Emoji("⛔"))
                .AddOption("Desbloquear cargo", "unblock_role", "Desbloqueie um cargo.", new Emoji("✅"));

            return new ComponentBuilder().WithSelectMenu(menu).Build();
        }

        private async Task AtualizarPainel(SocketGuild guild)
        {
            if (!PainelMessages.TryGetValue(guild.Id, out var info)) return;

            try
            {
                var channel = guild.GetTextChannel(info.channelId);
                if (channel == null) return;

                var mensagem = await channel.GetMessageAsync(info.messageId) as IUserMessage;
                if (mensagem == null) return;

                await mensagem.ModifyAsync(m =>
                {
                    m.Embed = CriarEmbedPainel(guild);
                    m.Components = CriarMenuPainel();
                });
            }
            catch { }
        }

        private async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;
            if (msg is not SocketUserMessage) return;
            var user = msg.Author as SocketGuildUser;
            if (user == null) return;

            var content = msg.Content.ToLower().Trim();

            if (!user.GuildPermissions.Administrator)
            {
                if (content.StartsWith("econfig") || content.StartsWith("eban"))
                    await msg.Channel.SendMessageAsync("❌ Você não tem permissão para usar isso.");
                return;
            }

            if (content == "econfig nuke")
            {
                var painelMsg = await msg.Channel.SendMessageAsync(embed: CriarEmbedPainel(user.Guild), components: CriarMenuPainel());
                PainelMessages[user.Guild.Id] = (painelMsg.Channel.Id, painelMsg.Id);
            }
            else if (content == "econfig help")
            {
                var embed = new EmbedBuilder()
                    .WithTitle("⚙️ Painel de Configuração")
                    .WithDescription(
                        "```\n" +
                        "econfig help          - Mostra este menu\n" +
                        "econfig nuke          - Configura permissões do /nuke\n" +
                        "econfig slowmode <s>  - Define slowmode no canal\n" +
                        "econfig lock          - Tranca o canal\n" +
                        "econfig unlock        - Destranca o canal\n" +
                        "econfig rename <nome> - Renomeia o canal\n" +
                        "econfig topic <texto> - Muda o tópico do canal\n" +
                        "```"
                    )
                    .WithColor(new Discord.Color(0x2B2D31))
                    .Build();
                await msg.Channel.SendMessageAsync(embed: embed);
            }
            else if (content.StartsWith("econfig slowmode"))
            {
                var parts = content.Split(' ');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int seconds))
                {
                    await ((ITextChannel)msg.Channel).ModifyAsync(x => x.SlowModeInterval = seconds);
                    await msg.Channel.SendMessageAsync($"✅ Slowmode definido para **{seconds}s**");
                }
                else
                    await msg.Channel.SendMessageAsync("❌ Use: `econfig slowmode <segundos>`");
            }
            else if (content == "econfig lock")
            {
                var ch = (ITextChannel)msg.Channel;
                await ch.AddPermissionOverwriteAsync(ch.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Deny));
                await msg.Channel.SendMessageAsync("🔒 Canal trancado.");
            }
            else if (content == "econfig unlock")
            {
                var ch = (ITextChannel)msg.Channel;
                await ch.AddPermissionOverwriteAsync(ch.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Inherit));
                await msg.Channel.SendMessageAsync("🔓 Canal destrancado.");
            }
            else if (content.StartsWith("econfig rename"))
            {
                var nome = msg.Content.Substring("econfig rename".Length).Trim();
                if (string.IsNullOrEmpty(nome)) { await msg.Channel.SendMessageAsync("❌ Use: `econfig rename <nome>`"); return; }
                await ((ITextChannel)msg.Channel).ModifyAsync(x => x.Name = nome);
                await msg.Channel.SendMessageAsync($"✅ Canal renomeado para **{nome}**");
            }
            else if (content.StartsWith("econfig topic"))
            {
                var texto = msg.Content.Substring("econfig topic".Length).Trim();
                if (string.IsNullOrEmpty(texto)) { await msg.Channel.SendMessageAsync("❌ Use: `econfig topic <texto>`"); return; }
                await ((ITextChannel)msg.Channel).ModifyAsync(x => x.Topic = texto);
                await msg.Channel.SendMessageAsync("✅ Tópico alterado.");
            }
            else if (content.StartsWith("eban"))
            {
                if (!user.GuildPermissions.BanMembers)
                {
                    await msg.Channel.SendMessageAsync("❌ Você não tem permissão para banir.");
                    return;
                }

                var texto = msg.Content.Substring("eban".Length).Trim();
                if (string.IsNullOrEmpty(texto))
                {
                    var helpEmbed = new EmbedBuilder()
                        .WithTitle("🔨 Como usar o eban")
                        .WithDescription(
                            "```\n" +
                            "eban @usuario              - Bane sem motivo\n" +
                            "eban @usuario 7            - Bane e apaga 7 dias de msgs\n" +
                            "eban @usuario 7 spammando  - Bane com dias e motivo\n" +
                            "```"
                        )
                        .WithColor(new Discord.Color(0xED4245))
                        .Build();
                    await msg.Channel.SendMessageAsync(embed: helpEmbed);
                    return;
                }

                SocketGuildUser alvoGuild = null;

                if (msg.MentionedUsers.Count > 0)
                {
                    var alvo = msg.MentionedUsers.First();
                    alvoGuild = user.Guild.GetUser(alvo.Id);
                    texto = texto.Substring(texto.IndexOf('>') + 1).Trim();
                }
                else
                {
                    var partes = texto.Split(' ');
                    var nome = partes[0].TrimStart('@');
                    texto = partes.Length > 1 ? string.Join(' ', partes.Skip(1)) : "";

                    alvoGuild = user.Guild.Users.FirstOrDefault(u =>
                        u.Username.Equals(nome, StringComparison.OrdinalIgnoreCase) ||
                        u.DisplayName.Equals(nome, StringComparison.OrdinalIgnoreCase));
                }

                if (alvoGuild == null) { await msg.Channel.SendMessageAsync("❌ Usuário não encontrado."); return; }
                if (alvoGuild.Id == user.Id) { await msg.Channel.SendMessageAsync("❌ Você não pode se banir."); return; }
                if (alvoGuild.Hierarchy >= user.Hierarchy) { await msg.Channel.SendMessageAsync("❌ Cargo igual ou superior ao seu."); return; }
                if (alvoGuild.Hierarchy >= user.Guild.CurrentUser.Hierarchy) { await msg.Channel.SendMessageAsync("❌ Meu cargo é inferior."); return; }

                int dias = 0;
                string motivo = "Sem motivo informado";
                var args = texto.Split(' ', 2);
                if (args.Length >= 1 && int.TryParse(args[0], out int d))
                {
                    dias = Math.Clamp(d, 0, 7);
                    if (args.Length >= 2) motivo = args[1];
                }
                else if (!string.IsNullOrEmpty(texto)) motivo = texto;

                await user.Guild.AddBanAsync(alvoGuild, pruneDays: dias, reason: motivo);

                var embed = new EmbedBuilder()
                    .WithTitle("🔨 Usuário Banido")
                    .WithDescription(
                        $"**Usuário:** {alvoGuild.Username} ({alvoGuild.Id})\n" +
                        $"**Moderador:** {user.Username}\n" +
                        $"**Dias apagados:** {dias}\n" +
                        $"**Motivo:** {motivo}")
                    .WithColor(new Discord.Color(0xED4245))
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();
                await msg.Channel.SendMessageAsync(embed: embed);
            }
        }

        private async Task HandleSelectMenu(SocketMessageComponent component)
        {
            var user = component.User as SocketGuildUser;
            if (user == null) return;

            if (!user.GuildPermissions.Administrator)
            {
                await component.RespondAsync("❌ Sem permissão.", ephemeral: true);
                return;
            }

            var guild = user.Guild;
            var config = GetConfig(guild.Id);
            var selected = component.Data.Values.First();

            switch (component.Data.CustomId)
            {
                case "nuke_config_menu":
                    switch (selected)
                    {
                        case "toggle":
                            config.Ativado = !config.Ativado;
                            await component.RespondAsync($"✅ Sistema de nuke **{(config.Ativado ? "ativado" : "desativado")}**!", ephemeral: true);
                            await AtualizarPainel(guild);
                            break;

                        case "add_role":
                            var addRoleMenu = new SelectMenuBuilder()
                                .WithCustomId("nuke_add_role")
                                .WithPlaceholder("Selecione o cargo para adicionar")
                                .WithType(ComponentType.RoleSelect)
                                .WithMinValues(1).WithMaxValues(1);
                            await component.RespondAsync("👇 Selecione o cargo:", components: new ComponentBuilder().WithSelectMenu(addRoleMenu).Build(), ephemeral: true);
                            break;

                        case "remove_role":
                            if (config.CargosPermitidos.Count == 0) { await component.RespondAsync("❌ Nenhum cargo na lista.", ephemeral: true); break; }
                            var rmRoleMenu = new SelectMenuBuilder().WithCustomId("nuke_remove_role").WithPlaceholder("Selecione o cargo para remover");
                            foreach (var id in config.CargosPermitidos)
                            {
                                var role = guild.GetRole(id);
                                rmRoleMenu.AddOption(role?.Name ?? id.ToString(), id.ToString());
                            }
                            await component.RespondAsync("👇 Selecione o cargo:", components: new ComponentBuilder().WithSelectMenu(rmRoleMenu).Build(), ephemeral: true);
                            break;

                        case "add_member":
                            var addMemberMenu = new SelectMenuBuilder()
                                .WithCustomId("nuke_add_member")
                                .WithPlaceholder("Selecione o membro para adicionar")
                                .WithType(ComponentType.UserSelect)
                                .WithMinValues(1).WithMaxValues(1);
                            await component.RespondAsync("👇 Selecione o membro:", components: new ComponentBuilder().WithSelectMenu(addMemberMenu).Build(), ephemeral: true);
                            break;

                        case "remove_member":
                            if (config.MembrosPermitidos.Count == 0) { await component.RespondAsync("❌ Nenhum membro na lista.", ephemeral: true); break; }
                            var rmMemberMenu = new SelectMenuBuilder().WithCustomId("nuke_remove_member").WithPlaceholder("Selecione o membro para remover");
                            foreach (var id in config.MembrosPermitidos)
                            {
                                var m = guild.GetUser(id);
                                rmMemberMenu.AddOption(m?.Username ?? id.ToString(), id.ToString());
                            }
                            await component.RespondAsync("👇 Selecione o membro:", components: new ComponentBuilder().WithSelectMenu(rmMemberMenu).Build(), ephemeral: true);
                            break;

                        case "block_user":
                            var blockUserMenu = new SelectMenuBuilder()
                                .WithCustomId("nuke_block_user")
                                .WithPlaceholder("Selecione o usuário para bloquear")
                                .WithType(ComponentType.UserSelect)
                                .WithMinValues(1).WithMaxValues(1);
                            await component.RespondAsync("👇 Selecione o usuário:", components: new ComponentBuilder().WithSelectMenu(blockUserMenu).Build(), ephemeral: true);
                            break;

                        case "unblock_user":
                            if (config.UsuariosBloqueados.Count == 0) { await component.RespondAsync("❌ Nenhum usuário bloqueado.", ephemeral: true); break; }
                            var unblockMenu = new SelectMenuBuilder().WithCustomId("nuke_unblock_user").WithPlaceholder("Selecione o usuário para desbloquear");
                            foreach (var id in config.UsuariosBloqueados)
                            {
                                var m = guild.GetUser(id);
                                unblockMenu.AddOption(m?.Username ?? id.ToString(), id.ToString());
                            }
                            await component.RespondAsync("👇 Selecione o usuário:", components: new ComponentBuilder().WithSelectMenu(unblockMenu).Build(), ephemeral: true);
                            break;

                        case "block_role":
                            var blockRoleMenu = new SelectMenuBuilder()
                                .WithCustomId("nuke_block_role")
                                .WithPlaceholder("Selecione o cargo para bloquear")
                                .WithType(ComponentType.RoleSelect)
                                .WithMinValues(1).WithMaxValues(1);
                            await component.RespondAsync("👇 Selecione o cargo:", components: new ComponentBuilder().WithSelectMenu(blockRoleMenu).Build(), ephemeral: true);
                            break;

                        case "unblock_role":
                            if (config.CargosBloqueados.Count == 0) { await component.RespondAsync("❌ Nenhum cargo bloqueado.", ephemeral: true); break; }
                            var unblockRoleMenu = new SelectMenuBuilder().WithCustomId("nuke_unblock_role").WithPlaceholder("Selecione o cargo para desbloquear");
                            foreach (var id in config.CargosBloqueados)
                            {
                                var role = guild.GetRole(id);
                                unblockRoleMenu.AddOption(role?.Name ?? id.ToString(), id.ToString());
                            }
                            await component.RespondAsync("👇 Selecione o cargo:", components: new ComponentBuilder().WithSelectMenu(unblockRoleMenu).Build(), ephemeral: true);
                            break;
                    }
                    break;

                case "nuke_add_role":
                    var roleId = ulong.Parse(component.Data.Values.First());
                    if (!config.CargosPermitidos.Contains(roleId)) { config.CargosPermitidos.Add(roleId); await component.RespondAsync($"✅ Cargo <@&{roleId}> adicionado!", ephemeral: true); }
                    else await component.RespondAsync("⚠️ Já está na lista.", ephemeral: true);
                    await AtualizarPainel(guild);
                    break;

                case "nuke_remove_role":
                    config.CargosPermitidos.Remove(ulong.Parse(component.Data.Values.First()));
                    await component.RespondAsync("✅ Cargo removido!", ephemeral: true);
                    await AtualizarPainel(guild);
                    break;

                case "nuke_add_member":
                    var memberId = ulong.Parse(component.Data.Values.First());
                    if (!config.MembrosPermitidos.Contains(memberId)) { config.MembrosPermitidos.Add(memberId); await component.RespondAsync($"✅ Membro <@{memberId}> adicionado!", ephemeral: true); }
                    else await component.RespondAsync("⚠️ Já está na lista.", ephemeral: true);
                    await AtualizarPainel(guild);
                    break;

                case "nuke_remove_member":
                    config.MembrosPermitidos.Remove(ulong.Parse(component.Data.Values.First()));
                    await component.RespondAsync("✅ Membro removido!", ephemeral: true);
                    await AtualizarPainel(guild);
                    break;

                case "nuke_block_user":
                    var blockId = ulong.Parse(component.Data.Values.First());
                    if (!config.UsuariosBloqueados.Contains(blockId)) { config.UsuariosBloqueados.Add(blockId); await component.RespondAsync($"✅ Usuário <@{blockId}> bloqueado!", ephemeral: true); }
                    else await component.RespondAsync("⚠️ Já está bloqueado.", ephemeral: true);
                    await AtualizarPainel(guild);
                    break;

                case "nuke_unblock_user":
                    config.UsuariosBloqueados.Remove(ulong.Parse(component.Data.Values.First()));
                    await component.RespondAsync("✅ Usuário desbloqueado!", ephemeral: true);
                    await AtualizarPainel(guild);
                    break;

                case "nuke_block_role":
                    var blockRoleId = ulong.Parse(component.Data.Values.First());
                    if (!config.CargosBloqueados.Contains(blockRoleId)) { config.CargosBloqueados.Add(blockRoleId); await component.RespondAsync($"✅ Cargo <@&{blockRoleId}> bloqueado!", ephemeral: true); }
                    else await component.RespondAsync("⚠️ Já está bloqueado.", ephemeral: true);
                    await AtualizarPainel(guild);
                    break;

                case "nuke_unblock_role":
                    config.CargosBloqueados.Remove(ulong.Parse(component.Data.Values.First()));
                    await component.RespondAsync("✅ Cargo desbloqueado!", ephemeral: true);
                    await AtualizarPainel(guild);
                    break;
            }
        }
    }
}
