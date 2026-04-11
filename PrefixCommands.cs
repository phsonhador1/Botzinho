using Botzinho.Admins;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Commands
{
    public class PrefixCommands
    {
        private readonly DiscordSocketClient _client;

        public PrefixCommands(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleCommand;
        }

        private async Task HandleCommand(SocketMessage message)
        {
            if (message is not SocketUserMessage msg) return;
            if (msg.Author.IsBot) return;
            if (msg.Channel is not SocketGuildChannel) return;

            int argPos = 0;
            if (!msg.HasCharPrefix('z', ref argPos)) return;

            var args = msg.Content.Substring(argPos).Trim().Split(' ');
            var cmd = args[0].ToLower();

            switch (cmd)
            {
                case "help":
                    await Help(msg);
                    break;

                case "nuke":
                    await Nuke(msg);
                    break;

                case "ban":
                    await Ban(msg);
                    break;

                case "kick":
                    await Kick(msg);
                    break;

                case "clear":
                    await Clear(msg, args);
                    break;
            }
        }

        private async Task Help(SocketUserMessage msg)
        {
            var embed = new EmbedBuilder()
                .WithTitle("💜 Comandos da Zoe")
                .WithDescription(
                    "`zhelp`\n`znuke`\n`zban`\n`zkick`\n`zclear`"
                )
                .WithColor(new Color(0xFF69B4))
                .Build();

            await msg.Channel.SendMessageAsync(embed: embed);
        }

        private async Task Nuke(SocketUserMessage msg)
        {
            var user = msg.Author as SocketGuildUser;
            var guildId = user.Guild.Id;

            var erro = AdminModule.ChecarPermissaoCompleta(guildId, user, "nuke", GuildPermission.ManageChannels);
            if (erro != null)
            {
                await msg.Channel.SendMessageAsync(erro);
                return;
            }

            var channel = (ITextChannel)msg.Channel;

            var newChannel = await channel.Guild.CreateTextChannelAsync(channel.Name, props =>
            {
                props.Topic = channel.Topic;
                props.CategoryId = channel.CategoryId;
                props.Position = channel.Position;
                props.PermissionOverwrites = new Optional<IEnumerable<Overwrite>>(channel.PermissionOverwrites.ToList());
            });

            await channel.DeleteAsync();
            await newChannel.SendMessageAsync($"canal nukado por {msg.Author.Username}");
        }

        private async Task Ban(SocketUserMessage msg)
        {
            var user = msg.Author as SocketGuildUser;
            var guild = user.Guild;

            if (msg.MentionedUsers.Count == 0)
            {
                await msg.Channel.SendMessageAsync("mencione um usuario");
                return;
            }

            var alvo = guild.GetUser(msg.MentionedUsers.First().Id);

            var erro = AdminModule.ChecarPermissaoCompleta(guild.Id, user, "ban", GuildPermission.BanMembers);
            if (erro != null)
            {
                await msg.Channel.SendMessageAsync(erro);
                return;
            }

            await guild.AddBanAsync(alvo);
            await msg.Channel.SendMessageAsync($"{alvo.Mention} banido");
        }

        private async Task Kick(SocketUserMessage msg)
        {
            var user = msg.Author as SocketGuildUser;
            var guild = user.Guild;

            if (msg.MentionedUsers.Count == 0)
            {
                await msg.Channel.SendMessageAsync("mencione um usuario");
                return;
            }

            var alvo = guild.GetUser(msg.MentionedUsers.First().Id);

            var erro = AdminModule.ChecarPermissaoCompleta(guild.Id, user, "kick", GuildPermission.KickMembers);
            if (erro != null)
            {
                await msg.Channel.SendMessageAsync(erro);
                return;
            }

            await alvo.KickAsync();
            await msg.Channel.SendMessageAsync($"{alvo.Mention} expulso");
        }

        private async Task Clear(SocketUserMessage msg, string[] args)
        {
            var user = msg.Author as SocketGuildUser;

            var erro = AdminModule.ChecarPermissaoCompleta(user.Guild.Id, user, "clear", GuildPermission.ManageMessages);
            if (erro != null)
            {
                await msg.Channel.SendMessageAsync(erro);
                return;
            }

            if (args.Length < 2 || !int.TryParse(args[1], out int quantidade))
            {
                await msg.Channel.SendMessageAsync("use: zclear 10");
                return;
            }

            var channel = (ITextChannel)msg.Channel;
            var messages = await channel.GetMessagesAsync(quantidade + 1).FlattenAsync();

            await channel.DeleteMessagesAsync(messages);
            await msg.Channel.SendMessageAsync($"limpei {quantidade} mensagens");
        }
    }
}
