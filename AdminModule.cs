using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace Botzinho.Admins
{
    public class AdminModule
    {
        private readonly DiscordSocketClient _client;

        public AdminModule(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
        }

        private async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;
            if (msg is not SocketUserMessage userMsg) return;

            var user = msg.Author as SocketGuildUser;
            if (user == null) return;

            if (!user.GuildPermissions.Administrator)
            {
                if (msg.Content.StartsWith("econfig"))
                    await msg.Channel.SendMessageAsync("❌ Você não tem permissão para usar isso.");
                return;
            }

            var content = msg.Content.ToLower().Trim();

            if (content == "econfig help")
            {
                var embed = new EmbedBuilder()
                    .WithTitle("⚙️ Painel de Configuração")
                    .WithDescription(
                        "```\n" +
                        "econfig help          - Mostra este menu\n" +
                        "econfig prefix        - Mostra o prefixo atual\n" +
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
            else if (content == "econfig prefix")
            {
                await msg.Channel.SendMessageAsync("O prefixo atual é: `econfig`");
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
                {
                    await msg.Channel.SendMessageAsync("❌ Use: `econfig slowmode <segundos>`");
                }
            }
            else if (content == "econfig lock")
            {
                var channel = (ITextChannel)msg.Channel;
                var everyone = channel.Guild.EveryoneRole;
                await channel.AddPermissionOverwriteAsync(everyone,
                    new OverwritePermissions(sendMessages: PermValue.Deny));
                await msg.Channel.SendMessageAsync("🔒 Canal trancado.");
            }
            else if (content == "econfig unlock")
            {
                var channel = (ITextChannel)msg.Channel;
                var everyone = channel.Guild.EveryoneRole;
                await channel.AddPermissionOverwriteAsync(everyone,
                    new OverwritePermissions(sendMessages: PermValue.Inherit));
                await msg.Channel.SendMessageAsync("🔓 Canal destrancado.");
            }
            else if (content.StartsWith("econfig rename"))
            {
                var nome = msg.Content.Substring("econfig rename".Length).Trim();
                if (string.IsNullOrEmpty(nome))
                {
                    await msg.Channel.SendMessageAsync("❌ Use: `econfig rename <nome>`");
                    return;
                }
                var channel = (ITextChannel)msg.Channel;
                await channel.ModifyAsync(x => x.Name = nome);
                await msg.Channel.SendMessageAsync($"✅ Canal renomeado para **{nome}**");
            }
            else if (content.StartsWith("econfig topic"))
            {
                var texto = msg.Content.Substring("econfig topic".Length).Trim();
                if (string.IsNullOrEmpty(texto))
                {
                    await msg.Channel.SendMessageAsync("❌ Use: `econfig topic <texto>`");
                    return;
                }
                var channel = (ITextChannel)msg.Channel;
                await channel.ModifyAsync(x => x.Topic = texto);
                await msg.Channel.SendMessageAsync("✅ Tópico alterado.");
            }
        }
    }
}
