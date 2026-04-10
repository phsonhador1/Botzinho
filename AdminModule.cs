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

        // Permissões do /configserver agora via banco
        private static readonly Dictionary<ulong, List<ulong>> ConfigServerUsuariosPermitidos = new();
        private static readonly Dictionary<ulong, List<ulong>> ConfigServerCargosPermitidos = new();

        public static readonly string[] Sistemas = { "nuke", "ban", "kick", "mute", "warn", "clear", "lock" };

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
                ?? throw new Exception("DATABASE_URL não configurado!");
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

            // Owner sempre entra pelo menos uma vez
            if (!ConfigServerUsuariosPermitidos[guild.Id].Contains(guild.OwnerId))
            {
                ConfigServerUsuariosPermitidos[guild.Id].Add(guild.OwnerId);
                SalvarPermissoesConfigServer(guild.Id);
            }
        }

        public static bool PodeUsarEconfigStatic(SocketGuildUser user)
        {
            var guildId = user.Guild.Id;

            // Dono do servidor sempre pode
            if (user.Id == user.Guild.OwnerId)
                return true;

            // SEU ID fixo para nunca se trancar fora
            if (user.Id == 1472642376970404002)
                return true;

            if (!ConfigServerUsuariosPermitidos.TryGetValue(guildId, out var usuarios))
                usuarios = new List<ulong>();

            if (!ConfigServerCargosPermitidos.TryGetValue(guildId, out var cargos))
                cargos = new List<ulong>();

            if (usuarios.Contains(user.Id))
                return true;

            if (user.Roles.Any(r => cargos.Contains(r.Id)))
                return true;

            return false;
        }

        public static bool TemPermissao(ulong guildId, SocketGuildUser user, string comando)
        {
            RecarregarComando(guildId, comando);

            // Owner do servidor sempre passa
            if (user.Id == user.Guild.OwnerId)
                return true;

            if (!Configs.TryGetValue(guildId, out var serverConfig))
                return false;

            var cmdConfig = serverConfig.GetCommand(comando);

            // Desativado = ninguém usa
            if (!cmdConfig.Ativado)
                return false;

            if (cmdConfig.UsuariosBloqueados.Contains(user.Id))
                return false;

            if (cmdConfig.CargosBloqueados.Any(r => user.Roles.Any(ur => ur.Id == r)))
                return false;

            bool temCargo = cmdConfig.CargosPermitidos.Any(r => user.Roles.Any(ur => ur.Id == r));
            bool temMembro = cmdConfig.MembrosPermitidos.Contains(user.Id);

            return temCargo || temMembro;
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
            if (!Configs.TryGetValue(guildId, out var serverConfig))
                return;

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
                Console.WriteLine($"[DB] Config de /{comando} salva para guild {guildId} (ativado={config.Ativado})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Erro ao salvar config de /{comando}: {ex.Message}");
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

                Console.WriteLine($"[DB] Configs carregadas: {Configs.Count} servidores");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Erro ao carregar configs: {ex.Message}");
            }
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
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Erro ao recarregar: {ex.Message}");
            }
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
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Erro ao recarregar comando {comando}: {ex.Message}");
            }
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
                    SELECT DISTINCT guild_id FROM configserver_cargos_permitidos
                ";

                using var reader = guildCmd.ExecuteReader();
                var guildIds = new List<ulong>();

                while (reader.Read())
                    guildIds.Add(ulong.Parse(reader.GetString(0)));

                reader.Close();

                foreach (var guildId in guildIds)
                {
                    ConfigServerUsuariosPermitidos[guildId] =
                        CarregarListaConfigServer(conn, "configserver_usuarios_permitidos", "user_id", guildId);

                    ConfigServerCargosPermitidos[guildId] =
                        CarregarListaConfigServer(conn, "configserver_cargos_permitidos", "cargo_id", guildId);
                }

                Console.WriteLine("[DB] Permissões do /configserver carregadas.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Erro ao carregar permissões do /configserver: {ex.Message}");
            }
        }

        private static void SalvarPermissoesConfigServer(ulong guildId)
        {
            try
            {
                using var conn = new NpgsqlConnection(GetConnectionString());
                conn.Open();

                var usuarios = ConfigServerUsuariosPermitidos.ContainsKey(guildId)
                    ? ConfigServerUsuariosPermitidos[guildId]
                    : new List<ulong>();

                var cargos = ConfigServerCargosPermitidos.ContainsKey(guildId)
                    ? ConfigServerCargosPermitidos[guildId]
                    : new List<ulong>();

                using (var delUsers = conn.CreateCommand())
                {
                    delUsers.CommandText = "DELETE FROM configserver_usuarios_permitidos WHERE guild_id = @gid";
                    delUsers.Parameters.AddWithValue("@gid", guildId.ToString());
                    delUsers.ExecuteNonQuery();
                }

                foreach (var userId in usuarios)
                {
                    using var ins = conn.CreateCommand();
                    ins.CommandText = "INSERT INTO configserver_usuarios_permitidos (guild_id, user_id) VALUES (@gid, @uid)";
                    ins.Parameters.AddWithValue("@gid", guildId.ToString());
                    ins.Parameters.AddWithValue("@uid", userId.ToString());
                    ins.ExecuteNonQuery();
                }

                using (var delRoles = conn.CreateCommand())
                {
                    delRoles.CommandText = "DELETE FROM configserver_cargos_permitidos WHERE guild_id = @gid";
                    delRoles.Parameters.AddWithValue("@gid", guildId.ToString());
                    delRoles.ExecuteNonQuery();
                }

                foreach (var roleId in cargos)
                {
                    using var ins = conn.CreateCommand();
                    ins.CommandText = "INSERT INTO configserver_cargos_permitidos (guild_id, cargo_id) VALUES (@gid, @rid)";
                    ins.Parameters.AddWithValue("@gid", guildId.ToString());
                    ins.Parameters.AddWithValue("@rid", roleId.ToString());
                    ins.ExecuteNonQuery();
                }

                Console.WriteLine($"[DB] Permissões do /configserver salvas na guild {guildId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Erro ao salvar permissões do /configserver: {ex.Message}");
            }
        }

        public static void AdicionarUsuarioConfigServer(ulong guildId, ulong userId)
        {
            if (!ConfigServerUsuariosPermitidos.ContainsKey(guildId))
                ConfigServerUsuariosPermitidos[guildId] = new List<ulong>();

            if (!ConfigServerUsuariosPermitidos[guildId].Contains(userId))
            {
                ConfigServerUsuariosPermitidos[guildId].Add(userId);
                SalvarPermissoesConfigServer(guildId);
            }
        }

        public static void RemoverUsuarioConfigServer(ulong guildId, ulong userId)
        {
            if (!ConfigServerUsuariosPermitidos.ContainsKey(guildId))
                return;

            if (ConfigServerUsuariosPermitidos[guildId].Remove(userId))
                SalvarPermissoesConfigServer(guildId);
        }

        public static void AdicionarCargoConfigServer(ulong guildId, ulong roleId)
        {
            if (!ConfigServerCargosPermitidos.ContainsKey(guildId))
                ConfigServerCargosPermitidos[guildId] = new List<ulong>();

            if (!ConfigServerCargosPermitidos[guildId].Contains(roleId))
            {
                ConfigServerCargosPermitidos[guildId].Add(roleId);
                SalvarPermissoesConfigServer(guildId);
            }
        }

        public static void RemoverCargoConfigServer(ulong guildId, ulong roleId)
        {
            if (!ConfigServerCargosPermitidos.ContainsKey(guildId))
                return;

            if (ConfigServerCargosPermitidos[guildId].Remove(roleId))
                SalvarPermissoesConfigServer(guildId);
        }

        public static Embed CriarEmbedComando(SocketGuild guild, string comando)
        {
            RecarregarComando(guild.Id, comando);

            if (!Configs.ContainsKey(guild.Id))
                Configs[guild.Id] = new ServerConfig();

            var config = Configs[guild.Id].GetCommand(comando);
            var botUser = guild.CurrentUser;

            var nomeComando = comando switch
            {
                "nuke" => "💣 Nuke",
                "ban" => "🔨 Ban",
                "kick" => "👢 Kick",
                "mute" => "🔇 Mute",
                "warn" => "⚠️ Warn",
                "clear" => "🗑️ Clear",
                "lock" => "🔒 Lock/Unlock",
                _ => comando
            };

            var statusText = config.Ativado ? "`Ativado`" : "`Desativado`";
            var cargosText = config.CargosPermitidos.Count > 0
                ? string.Join(", ", config.CargosPermitidos.Select(x => $"<@&{x}>"))
                : "`Padrão`";
            var membrosText = config.MembrosPermitidos.Count > 0
                ? string.Join(", ", config.MembrosPermitidos.Select(x => $"<@{x}>"))
                : "`Padrão`";
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
                    $"• {nomeComando} — **Configuração de Permissões**\n" +
                    $"   ○ Configure quem pode usar o comando `/{comando}` no seu servidor.\n" +
                    $"   ○ ⚠️ Quando ativado, **apenas** cargos/membros da lista podem usar, mesmo sendo admin.\n\n" +
                    $"• 🔧 **Informações:**\n" +
                    $"   ○ **Status**: {statusText}\n" +
                    $"   ○ **Cargos Permitidos**: {cargosText}\n" +
                    $"   ○ **Membros Permitidos**: {membrosText}\n" +
                    $"   ○ **Usuários Bloqueados**: {bloqueadosText}\n" +
                    $"   ○ **Cargos Bloqueados**: {cargosBloqText}\n\n" +
                    $"🌿 Use o menu abaixo para configurar ou volte ao menu principal."
                )
                .WithFooter($"Servidor de {guild.Owner?.Username ?? guild.Name} • Hoje às {DateTime.Now:HH:mm}")
                .WithColor(new Discord.Color(0x2B2D31))
                .Build();
        }

        public static MessageComponent CriarMenuComando(string comando)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId($"cmd_config_{comando}")
                .WithPlaceholder("Selecione a opção desejada para configurar.")
                .AddOption("Ativar/Desativar", "toggle", $"Ative ou desative o sistema de /{comando}", new Emoji("🛡️"))
                .AddOption("Adicionar cargos permitidos", "add_role", $"Adicione cargos que podem usar /{comando}", new Emoji("➕"))
                .AddOption("Remover cargos permitidos", "remove_role", "Remova cargos permitidos", new Emoji("➖"))
                .AddOption("Adicionar membros permitidos", "add_member", $"Adicione membros que podem usar /{comando}", new Emoji("👤"))
                .AddOption("Remover membros permitidos", "remove_member", "Remova membros permitidos", new Emoji("🚫"))
                .AddOption("Bloquear usuário", "block_user", "Bloqueie um usuário", new Emoji("🔒"))
                .AddOption("Desbloquear usuário", "unblock_user", "Desbloqueie um usuário", new Emoji("🔓"))
                .AddOption("Bloquear cargo", "block_role", "Bloqueie um cargo", new Emoji("⛔"))
                .AddOption("Desbloquear cargo", "unblock_role", "Desbloqueie um cargo", new Emoji("✅"))
                .AddOption("Voltar ao menu principal", "back", "Voltar para escolher outro sistema", new Emoji("◀️"));

            return new ComponentBuilder().WithSelectMenu(menu).Build();
        }

        public static Embed CriarEmbedPrincipal(SocketGuild guild)
        {
            var botUser = guild.CurrentUser;

            return new EmbedBuilder()
                .WithAuthor($"Config Server | {botUser.DisplayName}", botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                .WithThumbnailUrl(botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                .WithDescription(
                    "• ⚙️ **Painel de Configuração do Servidor**\n" +
                    "   ○ Selecione abaixo qual sistema você deseja configurar.\n" +
                    "   ○ Cada sistema permite definir cargos e membros que podem usar os comandos.\n" +
                    "   ○ ⚠️ Mesmo administradores precisam estar na lista para usar os comandos quando o sistema estiver ativado."
                )
                .WithFooter($"Servidor de {guild.Owner?.Username ?? guild.Name} • Hoje às {DateTime.Now:HH:mm}")
                .WithColor(new Discord.Color(0x2B2D31))
                .Build();
        }

        public static MessageComponent CriarMenuPrincipal()
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId("configserver_menu")
                .WithPlaceholder("Selecione o sistema para configurar")
                .AddOption("Nuke", "config_nuke", "Configurar permissões do /nuke", new Emoji("💣"))
                .AddOption("Ban", "config_ban", "Configurar permissões do /ban", new Emoji("🔨"))
                .AddOption("Kick", "config_kick", "Configurar permissões do /kick", new Emoji("👢"))
                .AddOption("Mute", "config_mute", "Configurar permissões do /mute", new Emoji("🔇"))
                .AddOption("Warn", "config_warn", "Configurar permissões do /warn", new Emoji("⚠️"))
                .AddOption("Clear", "config_clear", "Configurar permissões do /clear", new Emoji("🗑️"))
                .AddOption("Lock/Unlock", "config_lock", "Configurar permissões do /lock e /unlock", new Emoji("🔒"));

            return new ComponentBuilder().WithSelectMenu(menu).Build();
        }

        private async Task AtualizarPainel(SocketGuild guild, string comando)
        {
            if (!PainelMessages.TryGetValue(guild.Id, out var info))
                return;

            try
            {
                var channel = guild.GetTextChannel(info.channelId);
                if (channel == null)
                    return;

                var mensagem = await channel.GetMessageAsync(info.messageId) as IUserMessage;
                if (mensagem == null)
                    return;

                await mensagem.ModifyAsync(m =>
                {
                    m.Embed = CriarEmbedComando(guild, comando);
                    m.Components = CriarMenuComando(comando);
                });
            }
            catch
            {
            }
        }

        private async Task HandleSelectMenu(SocketMessageComponent component)
        {
            var user = component.User as SocketGuildUser;
            if (user == null)
                return;

            if (!PodeUsarEconfigStatic(user))
            {
                await component.RespondAsync("❌ Sem permissão.", ephemeral: true);
                return;
            }

            var guild = user.Guild;
            var customId = component.Data.CustomId;
            var selected = component.Data.Values.First();

            if (customId == "configserver_menu")
            {
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
                var comando = customId.Replace("cmd_config_", "");
                EditandoComando[guild.Id] = comando;

                if (!Configs.ContainsKey(guild.Id))
                    Configs[guild.Id] = new ServerConfig();

                var config = Configs[guild.Id].GetCommand(comando);

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
                        config = Configs[guild.Id].GetCommand(comando);
                        config.Ativado = !config.Ativado;
                        SalvarCommandConfig(guild.Id, comando);

                        await component.RespondAsync(
                            $"✅ Sistema de `/{comando}` **{(config.Ativado ? "ativado" : "desativado")}**!",
                            ephemeral: true);

                        await AtualizarPainel(guild, comando);
                        break;

                    case "add_role":
                        await component.RespondAsync(
                            "👇 Selecione o cargo:",
                            components: new ComponentBuilder().WithSelectMenu(
                                new SelectMenuBuilder()
                                    .WithCustomId("srv_add_role")
                                    .WithPlaceholder("Selecione o cargo")
                                    .WithType(ComponentType.RoleSelect)
                                    .WithMinValues(1)
                                    .WithMaxValues(1)
                            ).Build(),
                            ephemeral: true);
                        break;

                    case "remove_role":
                        RecarregarComando(guild.Id, comando);
                        config = Configs[guild.Id].GetCommand(comando);

                        if (config.CargosPermitidos.Count == 0)
                        {
                            await component.RespondAsync("❌ Nenhum cargo na lista.", ephemeral: true);
                            break;
                        }

                        var rmRoleMenu = new SelectMenuBuilder()
                            .WithCustomId("srv_remove_role")
                            .WithPlaceholder("Selecione o cargo para remover");

                        foreach (var id in config.CargosPermitidos)
                        {
                            var role = guild.GetRole(id);
                            rmRoleMenu.AddOption(role?.Name ?? id.ToString(), id.ToString());
                        }

                        await component.RespondAsync(
                            "👇 Selecione o cargo:",
                            components: new ComponentBuilder().WithSelectMenu(rmRoleMenu).Build(),
                            ephemeral: true);
                        break;

                    case "add_member":
                        await component.RespondAsync(
                            "👇 Selecione o membro:",
                            components: new ComponentBuilder().WithSelectMenu(
                                new SelectMenuBuilder()
                                    .WithCustomId("srv_add_member")
                                    .WithPlaceholder("Selecione o membro")
                                    .WithType(ComponentType.UserSelect)
                                    .WithMinValues(1)
                                    .WithMaxValues(1)
                            ).Build(),
                            ephemeral: true);
                        break;

                    case "remove_member":
                        RecarregarComando(guild.Id, comando);
                        config = Configs[guild.Id].GetCommand(comando);

                        if (config.MembrosPermitidos.Count == 0)
                        {
                            await component.RespondAsync("❌ Nenhum membro na lista.", ephemeral: true);
                            break;
                        }

                        var rmMemberMenu = new SelectMenuBuilder()
                            .WithCustomId("srv_remove_member")
                            .WithPlaceholder("Selecione o membro para remover");

                        foreach (var id in config.MembrosPermitidos)
                        {
                            var membro = guild.GetUser(id);
                            rmMemberMenu.AddOption(membro?.Username ?? id.ToString(), id.ToString());
                        }

                        await component.RespondAsync(
                            "👇 Selecione o membro:",
                            components: new ComponentBuilder().WithSelectMenu(rmMemberMenu).Build(),
                            ephemeral: true);
                        break;

                    case "block_user":
                        await component.RespondAsync(
                            "👇 Selecione o usuário:",
                            components: new ComponentBuilder().WithSelectMenu(
                                new SelectMenuBuilder()
                                    .WithCustomId("srv_block_user")
                                    .WithPlaceholder("Selecione o usuário")
                                    .WithType(ComponentType.UserSelect)
                                    .WithMinValues(1)
                                    .WithMaxValues(1)
                            ).Build(),
                            ephemeral: true);
                        break;

                    case "unblock_user":
                        RecarregarComando(guild.Id, comando);
                        config = Configs[guild.Id].GetCommand(comando);

                        if (config.UsuariosBloqueados.Count == 0)
                        {
                            await component.RespondAsync("❌ Nenhum usuário bloqueado.", ephemeral: true);
                            break;
                        }

                        var unblockMenu = new SelectMenuBuilder()
                            .WithCustomId("srv_unblock_user")
                            .WithPlaceholder("Selecione o usuário");

                        foreach (var id in config.UsuariosBloqueados)
                        {
                            var membro = guild.GetUser(id);
                            unblockMenu.AddOption(membro?.Username ?? id.ToString(), id.ToString());
                        }

                        await component.RespondAsync(
                            "👇 Selecione:",
                            components: new ComponentBuilder().WithSelectMenu(unblockMenu).Build(),
                            ephemeral: true);
                        break;

                    case "block_role":
                        await component.RespondAsync(
                            "👇 Selecione o cargo:",
                            components: new ComponentBuilder().WithSelectMenu(
                                new SelectMenuBuilder()
                                    .WithCustomId("srv_block_role")
                                    .WithPlaceholder("Selecione o cargo")
                                    .WithType(ComponentType.RoleSelect)
                                    .WithMinValues(1)
                                    .WithMaxValues(1)
                            ).Build(),
                            ephemeral: true);
                        break;

                    case "unblock_role":
                        RecarregarComando(guild.Id, comando);
                        config = Configs[guild.Id].GetCommand(comando);

                        if (config.CargosBloqueados.Count == 0)
                        {
                            await component.RespondAsync("❌ Nenhum cargo bloqueado.", ephemeral: true);
                            break;
                        }

                        var unblockRoleMenu = new SelectMenuBuilder()
                            .WithCustomId("srv_unblock_role")
                            .WithPlaceholder("Selecione o cargo");

                        foreach (var id in config.CargosBloqueados)
                        {
                            var role = guild.GetRole(id);
                            unblockRoleMenu.AddOption(role?.Name ?? id.ToString(), id.ToString());
                        }

                        await component.RespondAsync(
                            "👇 Selecione:",
                            components: new ComponentBuilder().WithSelectMenu(unblockRoleMenu).Build(),
                            ephemeral: true);
                        break;
                }

                return;
            }

            if (!EditandoComando.TryGetValue(guild.Id, out var editCmd))
                return;

            if (!Configs.ContainsKey(guild.Id))
                Configs[guild.Id] = new ServerConfig();

            RecarregarComando(guild.Id, editCmd);
            var cmdConfig = Configs[guild.Id].GetCommand(editCmd);

            switch (customId)
            {
                case "srv_add_role":
                    {
                        var roleId = ulong.Parse(component.Data.Values.First());

                        if (!cmdConfig.CargosPermitidos.Contains(roleId))
                        {
                            cmdConfig.CargosPermitidos.Add(roleId);
                            SalvarCommandConfig(guild.Id, editCmd);
                            await component.RespondAsync($"✅ Cargo <@&{roleId}> adicionado ao `/{editCmd}`!", ephemeral: true);
                        }
                        else
                        {
                            await component.RespondAsync("⚠️ Já está na lista.", ephemeral: true);
                        }

                        await AtualizarPainel(guild, editCmd);
                        break;
                    }

                case "srv_remove_role":
                    {
                        cmdConfig.CargosPermitidos.Remove(ulong.Parse(component.Data.Values.First()));
                        SalvarCommandConfig(guild.Id, editCmd);
                        await component.RespondAsync("✅ Cargo removido!", ephemeral: true);
                        await AtualizarPainel(guild, editCmd);
                        break;
                    }

                case "srv_add_member":
                    {
                        var memberId = ulong.Parse(component.Data.Values.First());

                        if (!cmdConfig.MembrosPermitidos.Contains(memberId))
                        {
                            cmdConfig.MembrosPermitidos.Add(memberId);
                            SalvarCommandConfig(guild.Id, editCmd);
                            await component.RespondAsync($"✅ Membro <@{memberId}> adicionado ao `/{editCmd}`!", ephemeral: true);
                        }
                        else
                        {
                            await component.RespondAsync("⚠️ Já está na lista.", ephemeral: true);
                        }

                        await AtualizarPainel(guild, editCmd);
                        break;
                    }

                case "srv_remove_member":
                    {
                        cmdConfig.MembrosPermitidos.Remove(ulong.Parse(component.Data.Values.First()));
                        SalvarCommandConfig(guild.Id, editCmd);
                        await component.RespondAsync("✅ Membro removido!", ephemeral: true);
                        await AtualizarPainel(guild, editCmd);
                        break;
                    }

                case "srv_block_user":
                    {
                        var blockId = ulong.Parse(component.Data.Values.First());

                        if (!cmdConfig.UsuariosBloqueados.Contains(blockId))
                        {
                            cmdConfig.UsuariosBloqueados.Add(blockId);
                            SalvarCommandConfig(guild.Id, editCmd);
                            await component.RespondAsync($"✅ Usuário <@{blockId}> bloqueado do `/{editCmd}`!", ephemeral: true);
                        }
                        else
                        {
                            await component.RespondAsync("⚠️ Já está bloqueado.", ephemeral: true);
                        }

                        await AtualizarPainel(guild, editCmd);
                        break;
                    }

                case "srv_unblock_user":
                    {
                        cmdConfig.UsuariosBloqueados.Remove(ulong.Parse(component.Data.Values.First()));
                        SalvarCommandConfig(guild.Id, editCmd);
                        await component.RespondAsync("✅ Usuário desbloqueado!", ephemeral: true);
                        await AtualizarPainel(guild, editCmd);
                        break;
                    }

                case "srv_block_role":
                    {
                        var blockRoleId = ulong.Parse(component.Data.Values.First());

                        if (!cmdConfig.CargosBloqueados.Contains(blockRoleId))
                        {
                            cmdConfig.CargosBloqueados.Add(blockRoleId);
                            SalvarCommandConfig(guild.Id, editCmd);
                            await component.RespondAsync($"✅ Cargo <@&{blockRoleId}> bloqueado do `/{editCmd}`!", ephemeral: true);
                        }
                        else
                        {
                            await component.RespondAsync("⚠️ Já está bloqueado.", ephemeral: true);
                        }

                        await AtualizarPainel(guild, editCmd);
                        break;
                    }

                case "srv_unblock_role":
                    {
                        cmdConfig.CargosBloqueados.Remove(ulong.Parse(component.Data.Values.First()));
                        SalvarCommandConfig(guild.Id, editCmd);
                        await component.RespondAsync("✅ Cargo desbloqueado!", ephemeral: true);
                        await AtualizarPainel(guild, editCmd);
                        break;
                    }
            }
        }
    }
}
