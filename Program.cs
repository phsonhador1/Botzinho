using Botzinho.Admins;
using Botzinho.Commands;
using Botzinho.Moderation;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


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
new PrefixCommands(client);



client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };
client.Ready += async () =>
{
    await interactionService.AddModulesAsync(typeof(NukeModule).Assembly, services);
    await interactionService.RegisterCommandsGloballyAsync(true);

    foreach (var guild in client.Guilds)
        AdminModule.GarantirAcessoInicialConfigServer(guild);

    Console.WriteLine($"Bot online como {client.CurrentUser.Username}");

    client.MessageReceived += async message =>
    {
        if (message is not SocketUserMessage msg) return;
        if (msg.Author.IsBot) return;

        int argPos = 0;

        // prefixo "z"
        if (!msg.HasCharPrefix('z', ref argPos)) return;

        var comando = msg.Content.Substring(argPos).Trim().ToLower();

        if (comando == "help")
        {
            var embed = new EmbedBuilder()
                .WithTitle("💜 Comandos da Zoe")
                .WithDescription(
                    "`zhelp` - mostra este menu\n" +
                    "`znuke` - limpa o canal\n" +
                    "`zclear` - apaga mensagens\n" +
                    "`zavisar` - dar aviso\n"
                )
                .WithColor(new Color(0xFF69B4))
                .Build();

            await msg.Channel.SendMessageAsync(embed: embed);
        }
    };

    _ = Task.Run(async () =>
    {
        int i = 0;
        while (true)
        {
            await client.SetStatusAsync(UserStatus.Online);

            // Criamos a lista AQUI DENTRO, assim o client.Guilds.Count atualiza sempre!
            string[] statusDinamicos = new[]
            {
                $"💜 Atualmente em {client.Guilds.Count} servidores",
                "💜 Online | Pronta Para Ajudar!",
                "✨ Use zhelp para ver meus comandos",
            };

            await client.SetCustomStatusAsync(statusDinamicos[i]);

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
    ?? throw new Exception("DISCORD_TOKEN nao configurado!");

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();
await Task.Delay(Timeout.Infinite);

public class ConfigServerModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("configserver", "Configura permissoes do servidor")]
    public async Task ConfigServerAsync()
    {
        var user = (SocketGuildUser)Context.User;

        if (!AdminModule.PodeUsarEconfigStatic(user))
        {
            await RespondAsync("voce nao tem permissao para usar este comando.", ephemeral: true);
            return;
        }

        var embed = AdminModule.CriarEmbedPrincipal(Context.Guild as SocketGuild);
        var components = AdminModule.CriarMenuPrincipal();

        await RespondAsync(embed: embed, components: components);
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

        var resultado = AdminModule.ChecarPermissaoCompleta(guildId, user, "nuke", GuildPermission.ManageChannels);
        if (resultado != null)
        {
            await RespondAsync(resultado, ephemeral: true);
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

        await newChannel.SendMessageAsync($"canal nukado por {Context.User.Username}");
    }
}
