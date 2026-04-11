using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Botzinho.Admins;
using Botzinho.Moderation;

var client = new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent | GatewayIntents.GuildMembers
});

var services = new ServiceCollection()
    .AddSingleton(client)
    .BuildServiceProvider();

var interactionService = new InteractionService(client);
var adminModule = new AdminModule(client);
ModerationHelper.InicializarTabelas();


client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };
client.Ready += async () =>
{
    Console.WriteLine($"Bot online como {client.CurrentUser.Username}");

    _ = Task.Run(async () =>
    {
        int i = 0;
        while (true)
        {
            await client.SetStatusAsync(UserStatus.Online);

            string[] statusDinamicos = new[]
            {
                
                
                $"💜 Atualmente em {client.Guilds.Count} servidores",
                "💜 Zoe | Pronta Para Ajudar!",
                "✨ Use zhelp para descobrir todos os meus comandos..."
            };

            await client.SetActivityAsync(new Game(statusDinamicos[i]));

            i = (i + 1) % statusDinamicos.Length;
            await Task.Delay(TimeSpan.FromSeconds(15));
        }
    });
};

client.InteractionCreated += async interaction =>
{
    var ctx = new SocketInteractionContext(client, interaction);
    await interactionService.ExecuteCommandAsync(ctx, services);
};

var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN")
    ?? throw new Exception("DISCORD_TOKEN não configurado!");

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();
await Task.Delay(Timeout.Infinite);

public class ConfigServerModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("configserver", "Configura permissões do servidor")]
    public async Task ConfigServerAsync()
    {
        var user = (SocketGuildUser)Context.User;

        if (!AdminModule.PodeUsarEconfigStatic(user))
        {
            await RespondAsync("❌ Você não tem permissão para usar este comando.", ephemeral: true);
            return;
        }

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

        var embed = new EmbedBuilder()
            .WithAuthor($"Config Server | {Context.Guild.CurrentUser.DisplayName}",
                Context.Guild.CurrentUser.GetAvatarUrl() ?? Context.Guild.CurrentUser.GetDefaultAvatarUrl())
            .WithThumbnailUrl(Context.Guild.CurrentUser.GetAvatarUrl() ?? Context.Guild.CurrentUser.GetDefaultAvatarUrl())
            .WithDescription(
                "• ⚙️ **Painel de Configuração do Servidor**\n" +
                "   ○ Selecione abaixo qual sistema você deseja configurar.\n" +
                "   ○ Cada sistema permite definir cargos e membros que podem usar os comandos.\n" +
                "   ○ ⚠️ Mesmo administradores precisam estar na lista para usar os comandos quando o sistema estiver ativado."
            )
            .WithFooter($"Servidor de {Context.Guild.Owner?.Username ?? Context.Guild.Name} • Hoje às {DateTime.Now:HH:mm}")
            .WithColor(new Discord.Color(0x2B2D31))
            .Build();

        await RespondAsync(embed: embed, components: new ComponentBuilder().WithSelectMenu(menu).Build());
        var msg = await GetOriginalResponseAsync();
        AdminModule.RegistrarPainel(Context.Guild.Id, msg.Channel.Id, msg.Id);
    }
}

public class NukeModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("nuke", "Limpa todas as mensagens do canal")]
    public async Task NukeAsync()
    {
        var user = (SocketGuildUser)Context.User;
        var guildId = Context.Guild.Id;

        AdminModule.RecarregarConfig(guildId);

        if (!AdminModule.TemPermissao(guildId, user, "nuke"))
        {
            await RespondAsync("❌ Você não tem permissão para usar este comando.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        var channel = (ITextChannel)Context.Channel;
        var newChannel = await channel.Guild.CreateTextChannelAsync(channel.Name, props =>
        {
            props.Topic = channel.Topic;
            props.CategoryId = channel.CategoryId;
            props.Position = channel.Position;
            props.IsNsfw = channel.IsNsfw;
            props.SlowModeInterval = channel.SlowModeInterval;
            props.PermissionOverwrites = new Optional<IEnumerable<Overwrite>>(
                channel.PermissionOverwrites.ToList()
            );
        });

        await channel.DeleteAsync();

        var embed = new EmbedBuilder()
            .WithDescription($"canal nukado por {Context.User.Username}")
            .WithColor(new Discord.Color(0x2B2D31))
            .Build();

        await newChannel.SendMessageAsync(embed: embed);
    }
}
