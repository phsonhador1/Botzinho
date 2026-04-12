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

        public static Dictionary<ulong, ServerConfig> Configs = new();
        private static Dictionary<ulong, (ulong channelId, ulong messageId)> PainelMessages = new();
        private static Dictionary<ulong, string> EditandoComando = new();

        private static readonly Dictionary<ulong, List<ulong>> ConfigServerUsuariosPermitidos = new();
        private static readonly Dictionary<ulong, List<ulong>> ConfigServerCargosPermitidos = new();

        public static readonly string[] Sistemas = { "nuke", "ban", "kick", "mute", "avisar", "clear", "lock" };

        public AdminModule(DiscordSocketClient client)
        {
            _client = client;
            _client.SelectMenuExecuted += HandleSelectMenu;
            InicializarDB();
            CarregarConfigs();
            CarregarPermissoesConfigServer();
        }

        public class CommandConfig
        {
            public bool Ativado { get; set; } = false;
            public List<ulong> CargosPermitidos { get; set; } = new();
            public List<ulong> MembrosPermitidos { get; set; } = new();
            public List<ulong> UsuariosBloqueados { get; set; } = new();
            public List<ulong> CargosBloqueados { get; set; } = new();
        }

        public class ServerConfig
        {
            public Dictionary<string, CommandConfig> Commands { get; set; } = new();

            public CommandConfig GetCommand(string cmd)
            {
                if (!Commands.ContainsKey(cmd))
                    Commands[cmd] = new CommandConfig();
                return Commands[cmd];
            }
        }

        private static string GetConnectionString()
        {
            return Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? throw new Exception("DATABASE_URL nao configurado!");
        }

        public static void RegistrarPainel(ulong guildId, ulong channelId, ulong messageId)
        {
            PainelMessages[guildId] = (channelId, messageId);
        }

        public static void GarantirAcessoInicialConfigServer(SocketGuild guild)
        {
            if (!ConfigServerUsuariosPermitidos.ContainsKey(guild.Id))
                ConfigServerUsuariosPermitidos[guild.Id] = new List<ulong>();

            if (!ConfigServerCargosPermitidos.ContainsKey(guild.Id))
                ConfigServerCargosPermitidos[guild.Id] = new List<ulong>();

            ulong meuId = 1472642376970404002;

            if (!ConfigServerUsuariosPermitidos[guild.Id].Contains(meuId))
                ConfigServerUsuariosPermitidos[guild.Id].Add(meuId);

            SalvarPermissoesConfigServer(guild.Id);
        }

        public static bool PodeUsarEconfigStatic(SocketGuildUser user)
        {
            List<ulong> idsPermitidos = new()
            {
                1472642376970404002
                // adicione outros IDs aqui
                // 123456789012345678,
            };

            return idsPermitidos.Contains(user.Id);
        }

        public static bool TemPermissao(ulong guildId, SocketGuildUser user, string comando)
        {
            RecarregarComando(guildId, comando);

            if (!Configs.TryGetValue(guildId, out var serverConfig))
                return true;

            if (!serverConfig.Commands.ContainsKey(comando))
                return true;

            var cmdConfig = serverConfig.GetCommand(comando);

            if (!cmdConfig.Ativado)
                return true;

            if (cmdConfig.UsuariosBloqueados.Contains(user.Id))
                return false;

            if (cmdConfig.CargosBloqueados.Any(r => user.Roles.Any(ur => ur.Id == r)))
                return false;

            bool temCargo = cmdConfig.CargosPermitidos.Any(r => user.Roles.Any(ur => ur.Id == r));
            bool temMembro = cmdConfig.MembrosPermitidos.Contains(user.Id);

            return temCargo || temMembro;
        }

        public static bool SistemaAtivado(ulong guildId, string comando)
        {
            if (!Configs.TryGetValue(guildId, out var serverConfig)) return false;
            if (!serverConfig.Commands.ContainsKey(comando)) return false;
            return serverConfig.GetCommand(comando).Ativado;
        }

        public static string? ChecarPermissaoCompleta(ulong guildId, SocketGuildUser user, string comando, GuildPermission permissaoPadrao)
        {
            RecarregarComando(guildId, comando);

            if (Configs.TryGetValue(guildId, out var serverConfig) && serverConfig.Commands.ContainsKey(comando))
            {
                var cmdConfig = serverConfig.GetCommand(comando);

                if (!cmdConfig.Ativado)
                    return $"o sistema de {comando} esta desativado neste servidor.";

                if (cmdConfig.UsuariosBloqueados.Contains(user.Id))
                    return "voce esta bloqueado de usar este comando.";

                if (cmdConfig.CargosBloqueados.Any(r => user.Roles.Any(ur => ur.Id == r)))
                    return "voce esta bloqueado de usar este comando.";

                bool temCargo = cmdConfig.CargosPermitidos.Any(r => user.Roles.Any(ur => ur.Id == r));
                bool temMembro = cmdConfig.MembrosPermitidos.Contains(user.Id);

                if (!temCargo && !temMembro)
                    return "voce nao tem permissao para usar este comando.";

                return null;
            }

            if (!user.GuildPermissions.Has(permissaoPadrao) && !user.GuildPermissions.Administrator)
                return "voce nao tem permissao para usar este comando.";

            return null;
        }

        private static void InicializarDB()
        {
            try
            {
                using var conn = new NpgsqlConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS command_config (
                        guild_id TEXT,
                        comando TEXT,
                        ativado BOOLEAN DEFAULT FALSE,
                        PRIMARY KEY (guild_id, comando)
                    );
                    CREATE TABLE IF NOT EXISTS command_cargos_permitidos (
                        guild_id TEXT,
                        comando TEXT,
                        cargo_id TEXT,
                        PRIMARY KEY (guild_id, comando, cargo_id)
                    );
                    CREATE TABLE IF NOT EXISTS command_membros_permitidos (
                        guild_id TEXT,
                        comando TEXT,
                        membro_id TEXT,
                        PRIMARY KEY (guild_id, comando, membro_id)
                    );
                    CREATE TABLE IF NOT EXISTS command_usuarios_bloqueados (
                        guild_id TEXT,
                        comando TEXT,
                        usuario_id TEXT,
                        PRIMARY KEY (guild_id, comando, usuario_id)
                    );
                    CREATE TABLE IF NOT EXISTS command_cargos_bloqueados (
                        guild_id TEXT,
                        comando TEXT,
                        cargo_id TEXT,
                        PRIMARY KEY (guild_id, comando, cargo_id)
                    );
                    CREATE TABLE IF NOT EXISTS configserver_usuarios_permitidos (
                        guild_id TEXT,
                        user_id TEXT,
                        PRIMARY KEY (guild_id, user_id)
                    );
                    CREATE TABLE IF NOT EXISTS configserver_cargos_permitidos (
                        guild_id TEXT,
                        cargo_id TEXT,
                        PRIMARY KEY (guild_id, cargo_id)
                    );
                ";
                cmd.ExecuteNonQuery();
                Console.WriteLine("Banco PostgreSQL inicializado.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao inicializar banco: {ex.Message}");
            }
        }

        private static void SalvarCommandConfig(ulong guildId, string comando)
        {
            if (!Configs.TryGetValue(guildId, out var serverConfig)) return;
            var config = serverConfig.GetCommand(comando);
            var gid = guildId.ToString();

            try
            {
                using var conn = new NpgsqlConnection(GetConnectionString());
                conn.Open();
                using var transaction = conn.BeginTransaction();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO command_config (guild_id, comando, ativado)
                        VALUES (@gid, @cmd, @ativado)
                        ON CONFLICT (guild_id, comando)
                        DO UPDATE SET ativado = @ativado";
                    cmd.Parameters.AddWithValue("@gid", gid);
                    cmd.Parameters.AddWithValue("@cmd", comando);
                    cmd.Parameters.AddWithValue("@ativado", config.Ativado);
                    cmd.ExecuteNonQuery();
                }

                SalvarLista(conn, "command_cargos_permitidos", "cargo_id", gid, comando, config.CargosPermitidos);
                SalvarLista(conn, "command_membros_permitidos", "membro_id", gid, comando, config.MembrosPermitidos);
                SalvarLista(conn, "command_usuarios_bloqueados", "usuario_id", gid, comando, config.UsuariosBloqueados);
                SalvarLista(conn, "command_cargos_bloqueados", "cargo_id", gid, comando, config.CargosBloqueados);

                transaction.Commit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Erro ao salvar: {ex.Message}");
            }
        }

        private static void SalvarLista(NpgsqlConnection conn, string tabela, string coluna, string guildId, string comando, List<ulong> ids)
        {
            using (var del = conn.CreateCommand())
            {
                del.CommandText = $"DELETE FROM {tabela} WHERE guild_id = @gid AND comando = @cmd";
                del.Parameters.AddWithValue("@gid", guildId);
                del.Parameters.AddWithValue("@cmd", comando);
                del.ExecuteNonQuery();
            }

            foreach (var id in ids)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText = $"INSERT INTO {tabela} (guild_id, comando, {coluna}) VALUES (@gid, @cmd, @id)";
                ins.Parameters.AddWithValue("@gid", guildId);
                ins.Parameters.AddWithValue("@cmd", comando);
                ins.Parameters.AddWithValue("@id", id.ToString());
                ins.ExecuteNonQuery();
            }
        }

        private static void CarregarConfigs()
        {
            try
            {
                using var conn = new NpgsqlConnection(GetConnectionString());
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT guild_id, comando, ativado FROM command_config";
                using var reader = cmd.ExecuteReader();

                var entries = new List<(ulong guildId, string comando, bool ativado)>();
                while (reader.Read())
                    entries.Add((ulong.Parse(reader.GetString(0)), reader.GetString(1), reader.GetBoolean(2)));
                reader.Close();

                foreach (var (guildId, comando, ativado) in entries)
                {
                    if (!Configs.ContainsKey(guildId))
                        Configs[guildId] = new ServerConfig();

                    var gid = guildId.ToString();
                    Configs[guildId].Commands[comando] = new CommandConfig
                    {
                        Ativado = ativado,
                        CargosPermitidos = CarregarLista(conn, "command_cargos_permitidos", "cargo_id", gid, comando),
                        MembrosPermitidos = CarregarLista(conn, "command_membros_permitidos", "membro_id", gid, comando),
                        UsuariosBloqueados = CarregarLista(conn, "command_usuarios_bloqueados", "usuario_id", gid, comando),
                        CargosBloqueados = CarregarLista(conn, "command_cargos_bloqueados", "cargo_id", gid, comando)
                    };
                }
            }
            catch (Exception ex) { Console.WriteLine($"[DB] Erro: {ex.Message}"); }
        }

        private static List<ulong> CarregarLista(NpgsqlConnection conn, string tabela, string coluna, string guildId, string comando)
        {
            var list = new List<ulong>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {coluna} FROM {tabela} WHERE guild_id = @gid AND comando = @cmd";
            cmd.Parameters.AddWithValue("@gid", guildId);
            cmd.Parameters.AddWithValue("@cmd", comando);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(ulong.Parse(reader.GetString(0)));
            return list;
        }

        public static void RecarregarConfig(ulong guildId)
        {
            try
            {
                using var conn = new NpgsqlConnection(GetConnectionString());
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT comando, ativado FROM command_config WHERE guild_id = @gid";
                cmd.Parameters.AddWithValue("@gid", guildId.ToString());
                using var reader = cmd.ExecuteReader();

                var entries = new List<(string comando, bool ativado)>();
                while (reader.Read())
                    entries.Add((reader.GetString(0), reader.GetBoolean(1)));
                reader.Close();

                if (!Configs.ContainsKey(guildId))
                    Configs[guildId] = new ServerConfig();

                var gid = guildId.ToString();
                foreach (var (comando, ativado) in entries)
                {
                    Configs[guildId].Commands[comando] = new CommandConfig
                    {
                        Ativado = ativado,
                        CargosPermitidos = CarregarLista(conn, "command_cargos_permitidos", "cargo_id", gid, comando),
                        MembrosPermitidos = CarregarLista(conn, "command_membros_permitidos", "membro_id", gid, comando),
                        UsuariosBloqueados = CarregarLista(conn, "command_usuarios_bloqueados", "usuario_id", gid, comando),
                        CargosBloqueados = CarregarLista(conn, "command_cargos_bloqueados", "cargo_id", gid, comando)
                    };
                }
            }
            catch (Exception ex) { Console.WriteLine($"[DB] Erro: {ex.Message}"); }
        }

        private static void RecarregarComando(ulong guildId, string comando)
        {
            try
            {
                using var conn = new NpgsqlConnection(GetConnectionString());
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT ativado FROM command_config WHERE guild_id = @gid AND comando = @cmd";
                cmd.Parameters.AddWithValue("@gid", guildId.ToString());
                cmd.Parameters.AddWithValue("@cmd", comando);
                var result = cmd.ExecuteScalar();

                if (!Configs.ContainsKey(guildId))
                    Configs[guildId] = new ServerConfig();

                if (result != null)
                {
                    var gid = guildId.ToString();
                    Configs[guildId].Commands[comando] = new CommandConfig
                    {
                        Ativado = (bool)result,
                        CargosPermitidos = CarregarLista(conn, "command_cargos_permitidos", "cargo_id", gid, comando),
                        MembrosPermitidos = CarregarLista(conn, "command_membros_permitidos", "membro_id", gid, comando),
                        UsuariosBloqueados = CarregarLista(conn, "command_usuarios_bloqueados", "usuario_id", gid, comando),
                        CargosBloqueados = CarregarLista(conn, "command_cargos_bloqueados", "cargo_id", gid, comando)
                    };
                }
            }
            catch (Exception ex) { Console.WriteLine($"[DB] Erro: {ex.Message}"); }
        }

        private static List<ulong> CarregarListaConfigServer(NpgsqlConnection conn, string tabela, string coluna, ulong guildId)
        {
            var list = new List<ulong>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {coluna} FROM {tabela} WHERE guild_id = @gid";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(ulong.Parse(reader.GetString(0)));
            return list;
        }

        public static void CarregarPermissoesConfigServer()
        {
            try
            {
                using var conn = new NpgsqlConnection(GetConnectionString());
                conn.Open();

                using var guildCmd = conn.CreateCommand();
                guildCmd.CommandText = @"
                    SELECT DISTINCT guild_id FROM configserver_usuarios_permitidos
                    UNION
                    SELECT DISTINCT guild_id FROM configserver_cargos_permitidos";
                using var reader = guildCmd.ExecuteReader();
                var guildIds = new List<ulong>();
                while (reader.Read())
                    guildIds.Add(ulong.Parse(reader.GetString(0)));
                reader.Close();

                foreach (var guildId in guildIds)
                {
                    ConfigServerUsuariosPermitidos[guildId] = CarregarListaConfigServer(conn, "configserver_usuarios_permitidos", "user_id", guildId);
                    ConfigServerCargosPermitidos[guildId] = CarregarListaConfigServer(conn, "configserver_cargos_permitidos", "cargo_id", guildId);
                }
            }
            catch (Exception ex) { Console.WriteLine($"[DB] Erro: {ex.Message}"); }
        }

        private static void SalvarPermissoesConfigServer(ulong guildId)
        {
            try
            {
                using var conn = new NpgsqlConnection(GetConnectionString());
                conn.Open();

                var usuarios = ConfigServerUsuariosPermitidos.ContainsKey(guildId) ? ConfigServerUsuariosPermitidos[guildId] : new List<ulong>();
                var cargos = ConfigServerCargosPermitidos.ContainsKey(guildId) ? ConfigServerCargosPermitidos[guildId] : new List<ulong>();

                using (var del = conn.CreateCommand())
                {
                    del.CommandText = "DELETE FROM configserver_usuarios_permitidos WHERE guild_id = @gid";
                    del.Parameters.AddWithValue("@gid", guildId.ToString());
                    del.ExecuteNonQuery();
                }
                foreach (var userId in usuarios)
                {
                    using var ins = conn.CreateCommand();
                    ins.CommandText = "INSERT INTO configserver_usuarios_permitidos (guild_id, user_id) VALUES (@gid, @uid)";
                    ins.Parameters.AddWithValue("@gid", guildId.ToString());
                    ins.Parameters.AddWithValue("@uid", userId.ToString());
                    ins.ExecuteNonQuery();
                }

                using (var del = conn.CreateCommand())
                {
                    del.CommandText = "DELETE FROM configserver_cargos_permitidos WHERE guild_id = @gid";
                    del.Parameters.AddWithValue("@gid", guildId.ToString());
                    del.ExecuteNonQuery();
                }
                foreach (var roleId in cargos)
                {
                    using var ins = conn.CreateCommand();
                    ins.CommandText = "INSERT INTO configserver_cargos_permitidos (guild_id, cargo_id) VALUES (@gid, @rid)";
                    ins.Parameters.AddWithValue("@gid", guildId.ToString());
                    ins.Parameters.AddWithValue("@rid", roleId.ToString());
                    ins.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { Console.WriteLine($"[DB] Erro: {ex.Message}"); }
        }

        public static Embed CriarEmbedComando(SocketGuild guild, string comando)
        {
            RecarregarComando(guild.Id, comando);

            if (!Configs.ContainsKey(guild.Id))
                Configs[guild.Id] = new ServerConfig();

            var config = Configs[guild.Id].GetCommand(comando);
            var botUser = guild.CurrentUser;

            var statusText = config.Ativado ? "`Ativado`" : "`Desativado`";
            var cargosText = config.CargosPermitidos.Count > 0
                ? string.Join(", ", config.CargosPermitidos.Select(x => $"<@&{x}>"))
                : "`Nenhum`";
            var membrosText = config.MembrosPermitidos.Count > 0
                ? string.Join(", ", config.MembrosPermitidos.Select(x => $"<@{x}>"))
                : "`Nenhum`";
            var bloqueadosText = config.UsuariosBloqueados.Count > 0
                ? string.Join(", ", config.UsuariosBloqueados.Select(x => $"<@{x}>"))
                : "`Nenhum`";
            var cargosBloqText = config.CargosBloqueados.Count > 0
                ? string.Join(", ", config.CargosBloqueados.Select(x => $"<@&{x}>"))
                : "`Nenhum`";

            return new EmbedBuilder()
                .WithAuthor($"Config Server | {botUser.DisplayName}", botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                .WithThumbnailUrl(botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                .WithDescription(
                    $"**{comando.ToUpper()}** - Configuracao de Permissoes\n\n" +
                    $"Configure quem pode usar o comando `/{comando}`.\n" +
                    "Quando **ativado**, apenas cargos/membros da lista podem usar.\n" +
                    "Quando **desativado**, ninguem pode usar o comando.\n\n" +
                    $"**Status**: {statusText}\n" +
                    $"**Cargos Permitidos**: {cargosText}\n" +
                    $"**Membros Permitidos**: {membrosText}\n" +
                    $"**Usuarios Bloqueados**: {bloqueadosText}\n" +
                    $"**Cargos Bloqueados**: {cargosBloqText}"
                )
                .WithFooter($"Servidor de {guild.Owner?.Username ?? guild.Name}")
                .WithColor(new Discord.Color(0x2B2D31))
                .Build();
        }

        public static MessageComponent CriarMenuComando(string comando)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId($"cmd_config_{comando}")
                .WithPlaceholder("Selecione a opcao desejada para configurar.")
                .AddOption("Ativar/Desativar", "toggle", "Ative ou desative o sistema")
                .AddOption("Adicionar cargos permitidos", "add_role", "Adicione cargos")
                .AddOption("Remover cargos permitidos", "remove_role", "Remova cargos")
                .AddOption("Adicionar membros permitidos", "add_member", "Adicione membros")
                .AddOption("Remover membros permitidos", "remove_member", "Remova membros")
                .AddOption("Bloquear usuario", "block_user", "Bloqueie um usuario")
                .AddOption("Desbloquear usuario", "unblock_user", "Desbloqueie um usuario")
                .AddOption("Bloquear cargo", "block_role", "Bloqueie um cargo")
                .AddOption("Desbloquear cargo", "unblock_role", "Desbloqueie um cargo")
                .AddOption("Voltar ao menu principal", "back", "Voltar");

            return new ComponentBuilder().WithSelectMenu(menu).Build();
        }

        public static Embed CriarEmbedPrincipal(SocketGuild guild)
        {
            var botUser = guild.CurrentUser;
            return new EmbedBuilder()
                .WithAuthor($"Config Server | {botUser.DisplayName}", botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                .WithThumbnailUrl(botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                .WithDescription(
                    "**Painel de Configuracao do Servidor**\n\n" +
                    "Selecione abaixo qual sistema voce deseja configurar.\n" +
                    "Cada sistema permite definir cargos e membros que podem usar os comandos.\n" +
                    "Quando **desativado**, ninguem pode usar o comando.\n" +
                    "Quando **ativado**, apenas cargos/membros da lista podem usar."
                )
                .WithFooter($"Servidor de {guild.Owner?.Username ?? guild.Name}")
                .WithColor(new Discord.Color(0x2B2D31))
                .Build();
        }

        public static MessageComponent CriarMenuPrincipal()
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId("configserver_menu")
                .WithPlaceholder("Selecione o sistema para configurar")
                .AddOption("Nuke", "config_nuke", "Configurar /nuke")
                .AddOption("Ban", "config_ban", "Configurar /ban")
                .AddOption("Kick", "config_kick", "Configurar /kick")
                .AddOption("Mute", "config_mute", "Configurar /mute")
                .AddOption("Avisar", "config_avisar", "Configurar /avisar")
                .AddOption("Clear", "config_clear", "Configurar /clear")
                .AddOption("Lock/Unlock", "config_lock", "Configurar /lock e /unlock");

            return new ComponentBuilder().WithSelectMenu(menu).Build();
        }

        private async Task AtualizarPainel(SocketGuild guild, string comando)
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
                    m.Embed = CriarEmbedComando(guild, comando);
                    m.Components = CriarMenuComando(comando);
                });
            }
            catch { }
        }

        private async Task HandleSelectMenu(SocketMessageComponent component)
        {
            var user = component.User as SocketGuildUser;
            if (user == null) return;

            var guild = user.Guild;
            var customId = component.Data.CustomId;
            var selected = component.Data.Values.FirstOrDefault();

            // MENU ZHELP
            if (customId == "help_menu")
            {
                if (selected == "help_eco")
                {
                    var roxo = "<:emoji_8:1491910148476899529>";
                    var embedEco = new EmbedBuilder()
                        .WithAuthor($"Comandos de Economia | {_client.CurrentUser.Username}", _client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                        .WithThumbnailUrl(_client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                        .WithDescription(
                            $"{roxo} `[]` = **Obrigatorio** / `()` = **Opcional**\n\n" +
                            $"{roxo} ↪ **zsaldo**:\n-# ◦ Veja seu saldo atual em cpoints.\n" +
                            $"{roxo} ↪ **zdaily**:\n-# ◦ Resgate seus cpoints diarios.\n" +
                            $"{roxo} ↪ **zpay [@usuario] [valor]**:\n-# ◦ Transfira seus cpoints para outro usuario.\n" +
                            $"{roxo} ↪ **zrank**:\n-# ◦ Veja o ranking dos usuarios mais ricos.\n"
                        )
                        .WithFooter("Use os comandos com sabedoria!")
                        .WithColor(new Color(120, 80, 220))
                        .Build();

                    await component.UpdateAsync(m =>
                    {
                        m.Embed = embedEco;
                        m.Components = ComponentBuilder.FromMessage(component.Message).Build();
                    });
                }
                else if (selected == "help_mod")
                {
                    var roxo = "<:emoji_8:1491910148476899529>";
                    var embedMod = new EmbedBuilder()
                        .WithAuthor($"Comandos de Moderacao | {_client.CurrentUser.Username}", _client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                        .WithDescription(
                            $"{roxo} ↪ **/ban [@usuario] (motivo)**:\n-# ◦ Bane um membro.\n" +
                            $"{roxo} ↪ **/kick [@usuario] (motivo)**:\n-# ◦ Expulsa um membro.\n" +
                            $"{roxo} ↪ **/mute [@usuario] [tempo]**:\n-# ◦ Silencia um membro.\n" +
                            $"{roxo} ↪ **/clear [quantidade]**:\n-# ◦ Limpa mensagens do chat.\n" +
                            $"{roxo} ↪ **/nuke**:\n-# ◦ Redefine o canal atual."
                        )
                        .WithColor(new Color(120, 80, 220))
                        .Build();

                    await component.UpdateAsync(m =>
                    {
                        m.Embed = embedMod;
                        m.Components = ComponentBuilder.FromMessage(component.Message).Build();
                    });
                }
                else if (selected == "help_admin")
                {
                    var roxo = "<:emoji_8:1491910148476899529>";
                    var embedAdmin = new EmbedBuilder()
                        .WithAuthor($"Configuracoes | {_client.CurrentUser.Username}", _client.CurrentUser.GetAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl())
                        .WithDescription($"{roxo} ↪ **/configserver**:\n-# ◦ Painel de controle de permissoes e sistemas.")
                        .WithColor(new Color(120, 80, 220))
                        .Build();

                    await component.UpdateAsync(m =>
                    {
                        m.Embed = embedAdmin;
                        m.Components = ComponentBuilder.FromMessage(component.Message).Build();
                    });
                }
                return;
            }

            // CONFIGSERVER
            if (customId == "configserver_menu")
            {
                if (!PodeUsarEconfigStatic(user))
                {
                    await component.RespondAsync("sem permissao.", ephemeral: true);
                    return;
                }

                var comando = selected.Replace("config_", "");
                EditandoComando[guild.Id] = comando;

                await component.UpdateAsync(m =>
                {
                    m.Embed = CriarEmbedComando(guild, comando);
                    m.Components = CriarMenuComando(comando);
                });
                return;
            }

            if (customId.StartsWith("cmd_config_"))
            {
                if (!PodeUsarEconfigStatic(user))
                {
                    await component.RespondAsync("sem permissao.", ephemeral: true);
                    return;
                }

                var comando = customId.Replace("cmd_config_", "");
                EditandoComando[guild.Id] = comando;

                if (!Configs.ContainsKey(guild.Id))
                    Configs[guild.Id] = new ServerConfig();

                switch (selected)
                {
                    case "back":
                        await component.UpdateAsync(m =>
                        {
                            m.Embed = CriarEmbedPrincipal(guild);
                            m.Components = CriarMenuPrincipal();
                        });
                        break;

                    case "toggle":
                        RecarregarComando(guild.Id, comando);
                        var toggleConfig = Configs[guild.Id].GetCommand(comando);
                        toggleConfig.Ativado = !toggleConfig.Ativado;
                        SalvarCommandConfig(guild.Id, comando);
                        await component.RespondAsync($"sistema /{comando} {(toggleConfig.Ativado ? "ativado" : "desativado")}.", ephemeral: true);
                        await AtualizarPainel(guild, comando);
                        break;

                    case "add_role":
                        await component.RespondAsync("selecione o cargo:",
                            components: new ComponentBuilder().WithSelectMenu(
                                new SelectMenuBuilder().WithCustomId("srv_add_role").WithPlaceholder("Selecione o cargo")
                                .WithType(ComponentType.RoleSelect).WithMinValues(1).WithMaxValues(1)).Build(), ephemeral: true);
                        break;

                    case "remove_role":
                        RecarregarComando(guild.Id, comando);
                        var rrConfig = Configs[guild.Id].GetCommand(comando);
                        if (rrConfig.CargosPermitidos.Count == 0) { await component.RespondAsync("nenhum cargo na lista.", ephemeral: true); break; }
                        var rmRoleMenu = new SelectMenuBuilder().WithCustomId("srv_remove_role").WithPlaceholder("Selecione o cargo");
                        foreach (var id in rrConfig.CargosPermitidos) { var role = guild.GetRole(id); rmRoleMenu.AddOption(role?.Name ?? id.ToString(), id.ToString()); }
                        await component.RespondAsync("selecione:", components: new ComponentBuilder().WithSelectMenu(rmRoleMenu).Build(), ephemeral: true);
                        break;

                    case "add_member":
                        await component.RespondAsync("selecione o membro:",
                            components: new ComponentBuilder().WithSelectMenu(
                                new SelectMenuBuilder().WithCustomId("srv_add_member").WithPlaceholder("Selecione o membro")
                                .WithType(ComponentType.UserSelect).WithMinValues(1).WithMaxValues(1)).Build(), ephemeral: true);
                        break;

                    case "remove_member":
                        RecarregarComando(guild.Id, comando);
                        var rmConfig = Configs[guild.Id].GetCommand(comando);
                        if (rmConfig.MembrosPermitidos.Count == 0) { await component.RespondAsync("nenhum membro na lista.", ephemeral: true); break; }
                        var rmMemberMenu = new SelectMenuBuilder().WithCustomId("srv_remove_member").WithPlaceholder("Selecione o membro");
                        foreach (var id in rmConfig.MembrosPermitidos) { var m = guild.GetUser(id); rmMemberMenu.AddOption(m?.Username ?? id.ToString(), id.ToString()); }
                        await component.RespondAsync("selecione:", components: new ComponentBuilder().WithSelectMenu(rmMemberMenu).Build(), ephemeral: true);
                        break;

                    case "block_user":
                        await component.RespondAsync("selecione o usuario:",
                            components: new ComponentBuilder().WithSelectMenu(
                                new SelectMenuBuilder().WithCustomId("srv_block_user").WithPlaceholder("Selecione o usuario")
                                .WithType(ComponentType.UserSelect).WithMinValues(1).WithMaxValues(1)).Build(), ephemeral: true);
                        break;

                    case "unblock_user":
                        RecarregarComando(guild.Id, comando);
                        var ubConfig = Configs[guild.Id].GetCommand(comando);
                        if (ubConfig.UsuariosBloqueados.Count == 0) { await component.RespondAsync("nenhum usuario bloqueado.", ephemeral: true); break; }
                        var unblockMenu = new SelectMenuBuilder().WithCustomId("srv_unblock_user").WithPlaceholder("Selecione o usuario");
                        foreach (var id in ubConfig.UsuariosBloqueados) { var m = guild.GetUser(id); unblockMenu.AddOption(m?.Username ?? id.ToString(), id.ToString()); }
                        await component.RespondAsync("selecione:", components: new ComponentBuilder().WithSelectMenu(unblockMenu).Build(), ephemeral: true);
                        break;

                    case "block_role":
                        await component.RespondAsync("selecione o cargo:",
                            components: new ComponentBuilder().WithSelectMenu(
                                new SelectMenuBuilder().WithCustomId("srv_block_role").WithPlaceholder("Selecione o cargo")
                                .WithType(ComponentType.RoleSelect).WithMinValues(1).WithMaxValues(1)).Build(), ephemeral: true);
                        break;

                    case "unblock_role":
                        RecarregarComando(guild.Id, comando);
                        var urConfig = Configs[guild.Id].GetCommand(comando);
                        if (urConfig.CargosBloqueados.Count == 0) { await component.RespondAsync("nenhum cargo bloqueado.", ephemeral: true); break; }
                        var unblockRoleMenu = new SelectMenuBuilder().WithCustomId("srv_unblock_role").WithPlaceholder("Selecione o cargo");
                        foreach (var id in urConfig.CargosBloqueados) { var role = guild.GetRole(id); unblockRoleMenu.AddOption(role?.Name ?? id.ToString(), id.ToString()); }
                        await component.RespondAsync("selecione:", components: new ComponentBuilder().WithSelectMenu(unblockRoleMenu).Build(), ephemeral: true);
                        break;
                }
                return;
            }

            if (!PodeUsarEconfigStatic(user))
                return;

            if (!EditandoComando.TryGetValue(guild.Id, out var editCmd)) return;
            if (!Configs.ContainsKey(guild.Id)) Configs[guild.Id] = new ServerConfig();

            RecarregarComando(guild.Id, editCmd);
            var cmdConfig = Configs[guild.Id].GetCommand(editCmd);

            switch (customId)
            {
                case "srv_add_role":
                    var roleId = ulong.Parse(component.Data.Values.First());
                    if (!cmdConfig.CargosPermitidos.Contains(roleId)) { cmdConfig.CargosPermitidos.Add(roleId); SalvarCommandConfig(guild.Id, editCmd); await component.RespondAsync($"cargo <@&{roleId}> adicionado ao /{editCmd}.", ephemeral: true); }
                    else await component.RespondAsync("ja esta na lista.", ephemeral: true);
                    await AtualizarPainel(guild, editCmd);
                    break;

                case "srv_remove_role":
                    cmdConfig.CargosPermitidos.Remove(ulong.Parse(component.Data.Values.First()));
                    SalvarCommandConfig(guild.Id, editCmd);
                    await component.RespondAsync("cargo removido.", ephemeral: true);
                    await AtualizarPainel(guild, editCmd);
                    break;

                case "srv_add_member":
                    var memberId = ulong.Parse(component.Data.Values.First());
                    if (!cmdConfig.MembrosPermitidos.Contains(memberId)) { cmdConfig.MembrosPermitidos.Add(memberId); SalvarCommandConfig(guild.Id, editCmd); await component.RespondAsync($"membro <@{memberId}> adicionado ao /{editCmd}.", ephemeral: true); }
                    else await component.RespondAsync("ja esta na lista.", ephemeral: true);
                    await AtualizarPainel(guild, editCmd);
                    break;

                case "srv_remove_member":
                    cmdConfig.MembrosPermitidos.Remove(ulong.Parse(component.Data.Values.First()));
                    SalvarCommandConfig(guild.Id, editCmd);
                    await component.RespondAsync("membro removido.", ephemeral: true);
                    await AtualizarPainel(guild, editCmd);
                    break;

                case "srv_block_user":
                    var blockId = ulong.Parse(component.Data.Values.First());
                    if (!cmdConfig.UsuariosBloqueados.Contains(blockId)) { cmdConfig.UsuariosBloqueados.Add(blockId); SalvarCommandConfig(guild.Id, editCmd); await component.RespondAsync($"usuario <@{blockId}> bloqueado do /{editCmd}.", ephemeral: true); }
                    else await component.RespondAsync("ja esta bloqueado.", ephemeral: true);
                    await AtualizarPainel(guild, editCmd);
                    break;

                case "srv_unblock_user":
                    cmdConfig.UsuariosBloqueados.Remove(ulong.Parse(component.Data.Values.First()));
                    SalvarCommandConfig(guild.Id, editCmd);
                    await component.RespondAsync("usuario desbloqueado.", ephemeral: true);
                    await AtualizarPainel(guild, editCmd);
                    break;

                case "srv_block_role":
                    var blockRoleId = ulong.Parse(component.Data.Values.First());
                    if (!cmdConfig.CargosBloqueados.Contains(blockRoleId)) { cmdConfig.CargosBloqueados.Add(blockRoleId); SalvarCommandConfig(guild.Id, editCmd); await component.RespondAsync($"cargo <@&{blockRoleId}> bloqueado do /{editCmd}.", ephemeral: true); }
                    else await component.RespondAsync("ja esta bloqueado.", ephemeral: true);
                    await AtualizarPainel(guild, editCmd);
                    break;

                case "srv_unblock_role":
                    cmdConfig.CargosBloqueados.Remove(ulong.Parse(component.Data.Values.First()));
                    SalvarCommandConfig(guild.Id, editCmd);
                    await component.RespondAsync("cargo desbloqueado.", ephemeral: true);
                    await AtualizarPainel(guild, editCmd);
                    break;
            }
        }
    }
}
