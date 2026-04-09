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

        // Configurações por servidor
        public static Dictionary<ulong, NukeConfig> Configs = new();

        public AdminModule(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
            _client.SelectMenuExecuted += HandleSelectMenu;
            _client.ButtonExecuted += HandleButton;
        }

        public class NukeConfig
        {
            public bool Ativado { get; set; } = false;
            public List<ulong> CargosPermitidos { get; set; } = new();
            public List<ulong> MembrosPermitidos { get; set; } = new();
        }

        private async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;
            if (msg is not SocketUserMessage userMsg) return;
            var user = msg.Author as SocketGuildUser;
            if (user == null) return;

            var content = msg.Content.ToLower().Trim();

            if (!user.GuildPermissions.Administrator)
            {
                if (content.StartsWith("econfig"))
                    await msg.Channel.SendMessageAsync("❌ Você não tem permissão para usar isso.");
                return;
            }

            if (content == "econfig nuke")
            {
                await EnviarMenuNuke(msg);
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
                    var channel = (ITextChannel)msg.Channel;
                    await channel.ModifyAsync(x => x.SlowModeInterval = seconds);
                    await msg.Channel.SendMessageAsync($"✅ Slowmode definido para **{seconds}s**");
                }
                else
                    await msg.Channel.SendMessageAsync("❌ Use: `econfig slowmode <segundos>`");
            }
            else if (content == "econfig lock")
            {
                var channel = (ITextChannel)msg.Channel;
                await channel.AddPermissionOverwriteAsync(channel.Guild.EveryoneRole,
                    new OverwritePermissions(sendMessages: PermValue.Deny));
                await msg.Channel.SendMessageAsync("🔒 Canal trancado.");
            }
            else if (content == "econfig unlock")
            {
                var channel = (ITextChannel)msg.Channel;
                await channel.AddPermissionOverwriteAsync(channel.Guild.EveryoneRole,
                    new OverwritePermissions(sendMessages: PermValue.Inherit));
                await msg.Channel.SendMessageAsync("🔓 Canal destrancado.");
            }
            else if (content.StartsWith("econfig rename"))
            {
                var nome = msg.Content.Substring("econfig rename".Length).Trim();
                if (string.IsNullOrEmpty(nome)) { await msg.Channel.SendMessageAsync("❌ Use: `econfig rename <nome>`"); return; }
                var channel = (ITextChannel)msg.Channel;
                await channel.ModifyAsync(x => x.Name = nome);
                await msg.Channel.SendMessageAsync($"✅ Canal renomeado para **{nome}**");
            }
            else if (content.StartsWith("econfig topic"))
            {
                var texto = msg.Content.Substring("econfig topic".Length).Trim();
                if (string.IsNullOrEmpty(texto)) { await msg.Channel.SendMessageAsync("❌ Use: `econfig topic <texto>`"); return; }
                var channel = (ITextChannel)msg.Channel;
                await channel.ModifyAsync(x => x.Topic = texto);
                await msg.Channel.SendMessageAsync("✅ Tópico alterado.");
            }
        }

        private async Task EnviarMenuNuke(SocketMessage msg)
        {
            var guildId = ((SocketGuildUser)msg.Author).Guild.Id;
            if (!Configs.ContainsKey(guildId))
                Configs[guildId] = new NukeConfig();

            var config = Configs[guildId];
            var statusText = config.Ativado ? "✅ Ativado" : "❌ Desativado";

            var embed = new EmbedBuilder()
                .WithTitle("⚙️ Bem-vindo(a) ao sistema de configuração do Nuke!")
                .WithDescription(
                    $"Configure quem pode usar o comando `/nuke` no seu servidor.\n\n" +
                    $"**Status:** {statusText}\n" +
                    $"**Cargos permitidos:** {(config.CargosPermitidos.Count > 0 ? string.Join(", ", config.CargosPermitidos.Select(x => $"<@&{x}>")) : "Nenhum")}\n" +
                    $"**Membros permitidos:** {(config.MembrosPermitidos.Count > 0 ? string.Join(", ", config.MembrosPermitidos.Select(x => $"<@{x}>")) : "Nenhum")}\n\n" +
                    "⚠️ Se nenhum cargo/membro for definido, o comportamento padrão (permissão Gerenciar Canais) será usado.\n\n" +
                    "Selecione a opção desejada para configurar."
                )
                .WithColor(new Discord.Color(0x2B2D31))
                .Build();

            var menu = new SelectMenuBuilder()
                .WithCustomId("nuke_config_menu")
                .WithPlaceholder("Selecione a opção desejada para configurar.")
                .AddOption("Ativar/Desativar", "toggle", $"{(config.Ativado ? "Desative" : "Ative")} o sistema de nuke", new Emoji(config.Ativado ? "❌" : "✅"))
                .AddOption("Adicionar cargos permitidos", "add_role", "Adicione à lista cargos que podem usar o /nuke", new Emoji("➕"))
                .AddOption("Remover cargos permitidos", "remove_role", "Remova da lista cargos permitidos", new Emoji("➖"))
                .AddOption("Adicionar membros permitidos", "add_member", "Adicione à lista membros que podem usar o /nuke", new Emoji("👤"))
                .AddOption("Remover membros permitidos", "remove_member", "Remova da lista membros permitidos", new Emoji("🚫"));

            var component = new ComponentBuilder()
                .WithSelectMenu(menu)
                .Build();

            await msg.Channel.SendMessageAsync(embed: embed, components: component);
        }

        private async Task HandleSelectMenu(SocketMessageComponent component)
        {
            if (component.Data.CustomId == "nuke_config_menu")
            {
                var user = component.User as SocketGuildUser;
                if (user == null || !user.GuildPermissions.Administrator)
                {
                    await component.RespondAsync("❌ Sem permissão.", ephemeral: true);
                    return;
                }

                var guildId = user.Guild.Id;
                if (!Configs.ContainsKey(guildId))
                    Configs[guildId] = new NukeConfig();

                var config = Configs[guildId];
                var selected = component.Data.Values.First();

                switch (selected)
                {
                    case "toggle":
                        config.Ativado = !config.Ativado;
                        await component.RespondAsync($"✅ Sistema de nuke **{(config.Ativado ? "ativado" : "desativado")}**!", ephemeral: true);
                        break;

                    case "add_role":
                        var roleMenu = new SelectMenuBuilder()
                            .WithCustomId("nuke_add_role")
                            .WithPlaceholder("Selecione o cargo")
                            .WithType(ComponentType.RoleSelect)
                            .WithMinValues(1)
                            .WithMaxValues(1);
                        await component.RespondAsync("Selecione o cargo para adicionar:",
                            components: new ComponentBuilder().WithSelectMenu(roleMenu).Build(), ephemeral: true);
                        break;

                    case "remove_role":
                        if (config.CargosPermitidos.Count == 0)
                        {
                            await component.RespondAsync("❌ Nenhum cargo para remover.", ephemeral: true);
                            break;
                        }
                        var removeRoleMenu = new SelectMenuBuilder()
                            .WithCustomId("nuke_remove_role")
                            .WithPlaceholder("Selecione o cargo para remover");
                        foreach (var roleId in config.CargosPermitidos)
                        {
                            var role = user.Guild.GetRole(roleId);
                            if (role != null)
                                removeRoleMenu.AddOption(role.Name, roleId.ToString());
                        }
                        await component.RespondAsync("Selecione o cargo para remover:",
                            components: new ComponentBuilder().WithSelectMenu(removeRoleMenu).Build(), ephemeral: true);
                        break;

                    case "add_member":
                        var memberMenu = new SelectMenuBuilder()
                            .WithCustomId("nuke_add_member")
                            .WithPlaceholder("Selecione o membro")
                            .WithType(ComponentType.UserSelect)
                            .WithMinValues(1)
                            .WithMaxValues(1);
                        await component.RespondAsync("Selecione o membro para adicionar:",
                            components: new ComponentBuilder().WithSelectMenu(memberMenu).Build(), ephemeral: true);
                        break;

                    case "remove_member":
                        if (config.MembrosPermitidos.Count == 0)
                        {
                            await component.RespondAsync("❌ Nenhum membro para remover.", ephemeral: true);
                            break;
                        }
                        var removeMemberMenu = new SelectMenuBuilder()
                            .WithCustomId("nuke_remove_member")
                            .WithPlaceholder("Selecione o membro para remover");
                        foreach (var memberId in config.MembrosPermitidos)
                        {
                            var member = user.Guild.GetUser(memberId);
                            removeMemberMenu.AddOption(member?.Username ?? memberId.ToString(), memberId.ToString());
                        }
                        await component.RespondAsync("Selecione o membro para remover:",
                            components: new ComponentBuilder().WithSelectMenu(removeMemberMenu).Build(), ephemeral: true);
                        break;
                }
            }
            else if (component.Data.CustomId == "nuke_add_role")
            {
                var user = component.User as SocketGuildUser;
                if (user == null) return;
                var config = Configs[user.Guild.Id];
                var roleId = ulong.Parse(component.Data.Values.First());
                if (!config.CargosPermitidos.Contains(roleId))
                {
                    config.CargosPermitidos.Add(roleId);
                    await component.RespondAsync($"✅ Cargo <@&{roleId}> adicionado!", ephemeral: true);
                }
                else
                    await component.RespondAsync("⚠️ Cargo já está na lista.", ephemeral: true);
            }
            else if (component.Data.CustomId == "nuke_remove_role")
            {
                var user = component.User as SocketGuildUser;
                if (user == null) return;
                var config = Configs[user.Guild.Id];
                var roleId = ulong.Parse(component.Data.Values.First());
                config.CargosPermitidos.Remove(roleId);
                await component.RespondAsync($"✅ Cargo removido!", ephemeral: true);
            }
            else if (component.Data.CustomId == "nuke_add_member")
            {
                var user = component.User as SocketGuildUser;
                if (user == null) return;
                var config = Configs[user.Guild.Id];
                var memberId = ulong.Parse(component.Data.Values.First());
                if (!config.MembrosPermitidos.Contains(memberId))
                {
                    config.MembrosPermitidos.Add(memberId);
                    await component.RespondAsync($"✅ Membro <@{memberId}> adicionado!", ephemeral: true);
                }
                else
                    await component.RespondAsync("⚠️ Membro já está na lista.", ephemeral: true);
            }
            else if (component.Data.CustomId == "nuke_remove_member")
            {
                var user = component.User as SocketGuildUser;
                if (user == null) return;
                var config = Configs[user.Guild.Id];
                var memberId = ulong.Parse(component.Data.Values.First());
                config.MembrosPermitidos.Remove(memberId);
                await component.RespondAsync($"✅ Membro removido!", ephemeral: true);
            }
        }

        private Task HandleButton(SocketMessageComponent component)
        {
            return Task.CompletedTask;
        }
    }
}
