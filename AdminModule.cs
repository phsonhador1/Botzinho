using Discord;
using Discord.WebSocket;
using Npgsql;
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
        private static readonly string DbPath = "Data Source=botzinho.db";

        public AdminModule(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
            _client.SelectMenuExecuted += HandleSelectMenu;
            InicializarDB();
            CarregarConfigs();
        }

        public class NukeConfig
        {
            public bool Ativado { get; set; } = false;
            public List<ulong> CargosPermitidos { get; set; } = new();
            public List<ulong> MembrosPermitidos { get; set; } = new();
            public List<ulong> UsuariosBloqueados { get; set; } = new();
            public List<ulong> CargosBloqueados { get; set; } = new();
        }

        private static void InicializarDB()
        {
            using var conn = new SqliteConnection(DbPath);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS nuke_config (
                    guild_id TEXT PRIMARY KEY,
                    ativado INTEGER DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS nuke_cargos_permitidos (
                    guild_id TEXT,
                    cargo_id TEXT,
                    PRIMARY KEY (guild_id, cargo_id)
                );
                CREATE TABLE IF NOT EXISTS nuke_membros_permitidos (
                    guild_id TEXT,
                    membro_id TEXT,
                    PRIMARY KEY (guild_id, membro_id)
                );
                CREATE TABLE IF NOT EXISTS nuke_usuarios_bloqueados (
                    guild_id TEXT,
                    usuario_id TEXT,
                    PRIMARY KEY (guild_id, usuario_id)
                );
                CREATE TABLE IF NOT EXISTS nuke_cargos_bloqueados (
                    guild_id TEXT,
                    cargo_id TEXT,
                    PRIMARY KEY (guild_id, cargo_id)
                );
            ";
            cmd.ExecuteNonQuery();
            Console.WriteLine("Banco SQLite inicializado.");
        }

        private static void SalvarConfig(ulong guildId)
        {
            if (!Configs.TryGetValue(guildId, out var config)) return;
            var gid = guildId.ToString();

            using var conn = new SqliteConnection(DbPath);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO nuke_config (guild_id, ativado) VALUES ($gid, $ativado)";
                cmd.Parameters.AddWithValue("$gid", gid);
                cmd.Parameters.AddWithValue("$ativado", config.Ativado ? 1 : 0);
                cmd.ExecuteNonQuery();

                LimparEInserir(conn, "nuke_cargos_permitidos", "cargo_id", gid, config.CargosPermitidos);
                LimparEInserir(conn, "nuke_membros_permitidos", "membro_id", gid, config.MembrosPermitidos);
                LimparEInserir(conn, "nuke_usuarios_bloqueados", "usuario_id", gid, config.UsuariosBloqueados);
                LimparEInserir(conn, "nuke_cargos_bloqueados", "cargo_id", gid, config.CargosBloqueados);

                transaction.Commit();
                Console.WriteLine($"Config salva para guild {guildId}");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"Erro ao salvar config: {ex.Message}");
            }
        }

        private static void LimparEInserir(SqliteConnection conn, string tabela, string coluna, string guildId, List<ulong> ids)
        {
            var del = conn.CreateCommand();
            del.CommandText = $"DELETE FROM {tabela} WHERE guild_id = $gid";
            del.Parameters.AddWithValue("$gid", guildId);
            del.ExecuteNonQuery();

            foreach (var id in ids)
            {
                var ins = conn.CreateCommand();
                ins.CommandText = $"INSERT INTO {tabela} (guild_id, {coluna}) VALUES ($gid, $id)";
                ins.Parameters.AddWithValue("$gid", guildId);
                ins.Parameters.AddWithValue("$id", id.ToString());
                ins.ExecuteNonQuery();
            }
        }

        private static void CarregarConfigs()
        {
            try
            {
                using var conn = new SqliteConnection(DbPath);
                conn.Open();

                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT guild_id, ativado FROM nuke_config";
                using var reader = cmd.ExecuteReader();

                var guildIds = new List<(ulong id, bool ativado)>();
                while (reader.Read())
                    guildIds.Add((ulong.Parse(reader.GetString(0)), reader.GetInt32(1) == 1));
                reader.Close();

                foreach (var (guildId, ativado) in guildIds)
                {
                    var config = new NukeConfig
                    {
                        Ativado = ativado,
                        CargosPermitidos = CarregarLista(conn, "nuke_cargos_permitidos", "cargo_id", guildId.ToString()),
                        MembrosPermitidos = CarregarLista(conn, "nuke_membros_permitidos", "membro_id", guildId.ToString()),
                        UsuariosBloqueados = CarregarLista(conn, "nuke_usuarios_bloqueados", "usuario_id", guildId.ToString()),
                        CargosBloqueados = CarregarLista(conn, "nuke_cargos_bloqueados", "cargo_id", guildId.ToString())
                    };
                    Configs[guildId] = config;
                }

                Console.WriteLine($"Configs carregadas: {Configs.Count} servidores");
            }
            catch (Exception ex) { Console.WriteLine($"Erro ao carregar configs: {ex.Message}"); }
        }

        private static List<ulong> CarregarLista(SqliteConnection conn, string tabela, string coluna, string guildId)
        {
            var list = new List<ulong>();
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {coluna} FROM {tabela} WHERE guild_id = $gid";
            cmd.Parameters.AddWithValue("$gid", guildId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(ulong.Parse(reader.GetString(0)));
            return list;
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

            if (content == "econfig nuke")
            {
                if (!user.GuildPermissions.Administrator)
                {
                    await msg.Channel.SendMessageAsync("❌ Você não tem permissão para usar isso.");
                    return;
                }

                var painelMsg = await msg.Channel.SendMessageAsync(embed: CriarEmbedPainel(user.Guild), components: CriarMenuPainel());
                PainelMessages[user.Guild.Id] = (painelMsg.Channel.Id, painelMsg.Id);
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
                            SalvarConfig(guild.Id);
                            await component.RespondAsync($"✅ Sistema de nuke **{(config.Ativado ? "ativado" : "desativado")}**!", ephemeral: true);
                            await AtualizarPainel(guild);
                            break;

                        case "add_role":
                            await component.RespondAsync("👇 Selecione o cargo:", components: new ComponentBuilder().WithSelectMenu(new SelectMenuBuilder().WithCustomId("nuke_add_role").WithPlaceholder("Selecione o cargo").WithType(ComponentType.RoleSelect).WithMinValues(1).WithMaxValues(1)).Build(), ephemeral: true);
                            break;

                        case "remove_role":
                            if (config.CargosPermitidos.Count == 0) { await component.RespondAsync("❌ Nenhum cargo na lista.", ephemeral: true); break; }
                            var rmRoleMenu = new SelectMenuBuilder().WithCustomId("nuke_remove_role").WithPlaceholder("Selecione o cargo para remover");
                            foreach (var id in config.CargosPermitidos) { var role = guild.GetRole(id); rmRoleMenu.AddOption(role?.Name ?? id.ToString(), id.ToString()); }
                            await component.RespondAsync("👇 Selecione o cargo:", components: new ComponentBuilder().WithSelectMenu(rmRoleMenu).Build(), ephemeral: true);
                            break;

                        case "add_member":
                            await component.RespondAsync("👇 Selecione o membro:", components: new ComponentBuilder().WithSelectMenu(new SelectMenuBuilder().WithCustomId("nuke_add_member").WithPlaceholder("Selecione o membro").WithType(ComponentType.UserSelect).WithMinValues(1).WithMaxValues(1)).Build(), ephemeral: true);
                            break;

                        case "remove_member":
                            if (config.MembrosPermitidos.Count == 0) { await component.RespondAsync("❌ Nenhum membro na lista.", ephemeral: true); break; }
                            var rmMemberMenu = new SelectMenuBuilder().WithCustomId("nuke_remove_member").WithPlaceholder("Selecione o membro para remover");
                            foreach (var id in config.MembrosPermitidos) { var m = guild.GetUser(id); rmMemberMenu.AddOption(m?.Username ?? id.ToString(), id.ToString()); }
                            await component.RespondAsync("👇 Selecione o membro:", components: new ComponentBuilder().WithSelectMenu(rmMemberMenu).Build(), ephemeral: true);
                            break;

                        case "block_user":
                            await component.RespondAsync("👇 Selecione o usuário:", components: new ComponentBuilder().WithSelectMenu(new SelectMenuBuilder().WithCustomId("nuke_block_user").WithPlaceholder("Selecione o usuário").WithType(ComponentType.UserSelect).WithMinValues(1).WithMaxValues(1)).Build(), ephemeral: true);
                            break;

                        case "unblock_user":
                            if (config.UsuariosBloqueados.Count == 0) { await component.RespondAsync("❌ Nenhum usuário bloqueado.", ephemeral: true); break; }
                            var unblockMenu = new SelectMenuBuilder().WithCustomId("nuke_unblock_user").WithPlaceholder("Selecione o usuário para desbloquear");
                            foreach (var id in config.UsuariosBloqueados) { var m = guild.GetUser(id); unblockMenu.AddOption(m?.Username ?? id.ToString(), id.ToString()); }
                            await component.RespondAsync("👇 Selecione o usuário:", components: new ComponentBuilder().WithSelectMenu(unblockMenu).Build(), ephemeral: true);
                            break;

                        case "block_role":
                            await component.RespondAsync("👇 Selecione o cargo:", components: new ComponentBuilder().WithSelectMenu(new SelectMenuBuilder().WithCustomId("nuke_block_role").WithPlaceholder("Selecione o cargo").WithType(ComponentType.RoleSelect).WithMinValues(1).WithMaxValues(1)).Build(), ephemeral: true);
                            break;

                        case "unblock_role":
                            if (config.CargosBloqueados.Count == 0) { await component.RespondAsync("❌ Nenhum cargo bloqueado.", ephemeral: true); break; }
                            var unblockRoleMenu = new SelectMenuBuilder().WithCustomId("nuke_unblock_role").WithPlaceholder("Selecione o cargo para desbloquear");
                            foreach (var id in config.CargosBloqueados) { var role = guild.GetRole(id); unblockRoleMenu.AddOption(role?.Name ?? id.ToString(), id.ToString()); }
                            await component.RespondAsync("👇 Selecione o cargo:", components: new ComponentBuilder().WithSelectMenu(unblockRoleMenu).Build(), ephemeral: true);
                            break;
                    }
                    break;

                case "nuke_add_role":
                    var roleId = ulong.Parse(component.Data.Values.First());
                    if (!config.CargosPermitidos.Contains(roleId)) { config.CargosPermitidos.Add(roleId); await component.RespondAsync($"✅ Cargo <@&{roleId}> adicionado!", ephemeral: true); }
                    else await component.RespondAsync("⚠️ Já está na lista.", ephemeral: true);
                    SalvarConfig(guild.Id); await AtualizarPainel(guild);
                    break;

                case "nuke_remove_role":
                    config.CargosPermitidos.Remove(ulong.Parse(component.Data.Values.First()));
                    await component.RespondAsync("✅ Cargo removido!", ephemeral: true);
                    SalvarConfig(guild.Id); await AtualizarPainel(guild);
                    break;

                case "nuke_add_member":
                    var memberId = ulong.Parse(component.Data.Values.First());
                    if (!config.MembrosPermitidos.Contains(memberId)) { config.MembrosPermitidos.Add(memberId); await component.RespondAsync($"✅ Membro <@{memberId}> adicionado!", ephemeral: true); }
                    else await component.RespondAsync("⚠️ Já está na lista.", ephemeral: true);
                    SalvarConfig(guild.Id); await AtualizarPainel(guild);
                    break;

                case "nuke_remove_member":
                    config.MembrosPermitidos.Remove(ulong.Parse(component.Data.Values.First()));
                    await component.RespondAsync("✅ Membro removido!", ephemeral: true);
                    SalvarConfig(guild.Id); await AtualizarPainel(guild);
                    break;

                case "nuke_block_user":
                    var blockId = ulong.Parse(component.Data.Values.First());
                    if (!config.UsuariosBloqueados.Contains(blockId)) { config.UsuariosBloqueados.Add(blockId); await component.RespondAsync($"✅ Usuário <@{blockId}> bloqueado!", ephemeral: true); }
                    else await component.RespondAsync("⚠️ Já está bloqueado.", ephemeral: true);
                    SalvarConfig(guild.Id); await AtualizarPainel(guild);
                    break;

                case "nuke_unblock_user":
                    config.UsuariosBloqueados.Remove(ulong.Parse(component.Data.Values.First()));
                    await component.RespondAsync("✅ Usuário desbloqueado!", ephemeral: true);
                    SalvarConfig(guild.Id); await AtualizarPainel(guild);
                    break;

                case "nuke_block_role":
                    var blockRoleId = ulong.Parse(component.Data.Values.First());
                    if (!config.CargosBloqueados.Contains(blockRoleId)) { config.CargosBloqueados.Add(blockRoleId); await component.RespondAsync($"✅ Cargo <@&{blockRoleId}> bloqueado!", ephemeral: true); }
                    else await component.RespondAsync("⚠️ Já está bloqueado.", ephemeral: true);
                    SalvarConfig(guild.Id); await AtualizarPainel(guild);
                    break;

                case "nuke_unblock_role":
                    config.CargosBloqueados.Remove(ulong.Parse(component.Data.Values.First()));
                    await component.RespondAsync("✅ Cargo desbloqueado!", ephemeral: true);
                    SalvarConfig(guild.Id); await AtualizarPainel(guild);
                    break;
            }
        }
    }
}
