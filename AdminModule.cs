using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Botzinho.Admins
{
    public class AdminModule
    {
        public static Dictionary<ulong, ServerConfig> Configs = new();

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

        public class PermissionResult
        {
            public bool Permitido { get; set; }
            public string Mensagem { get; set; } = "";
        }

        public static PermissionResult VerificarPermissao(ulong guildId, SocketGuildUser user, string comando)
        {
            if (!Configs.TryGetValue(guildId, out var serverConfig))
            {
                return new PermissionResult
                {
                    Permitido = false,
                    Mensagem = $"sistema {comando} desativado"
                };
            }

            var cmdConfig = serverConfig.GetCommand(comando);

            if (!cmdConfig.Ativado)
            {
                return new PermissionResult
                {
                    Permitido = false,
                    Mensagem = $"sistema {comando} desativado"
                };
            }

            if (cmdConfig.UsuariosBloqueados.Contains(user.Id))
            {
                return new PermissionResult
                {
                    Permitido = false,
                    Mensagem = $"você foi bloqueado do sistema {comando}"
                };
            }

            if (cmdConfig.CargosBloqueados.Any(r => user.Roles.Any(ur => ur.Id == r)))
            {
                return new PermissionResult
                {
                    Permitido = false,
                    Mensagem = $"seu cargo foi bloqueado do sistema {comando}"
                };
            }

            bool temCargo = cmdConfig.CargosPermitidos.Any(r => user.Roles.Any(ur => ur.Id == r));
            bool temMembro = cmdConfig.MembrosPermitidos.Contains(user.Id);

            if (!temCargo && !temMembro)
            {
                return new PermissionResult
                {
                    Permitido = false,
                    Mensagem = $"você não tem permissão para usar o sistema {comando}"
                };
            }

            return new PermissionResult
            {
                Permitido = true
            };
        }
    }
}
