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
    await interactionService.AddModulesAsync(typeof(NukeModule).Assembly, services);
    await interactionService.RegisterCommandsGloballyAsync(true);

    foreach (var guild in client.Guilds)
        AdminModule.GarantirAcessoInicialConfigServer(guild);

    Console.WriteLine($"Bot online como {client.CurrentUser.Username}");

    _ = Task.Run(async () =>
    {
        int i = 0;
        while (true)
        {
            await client.SetStatusAsync(UserStatus.Online);

            // Criamos a lista AQUI DENTRO, assim o client.Guilds.Count atualiza sempre!
            string[] statusDinamicos = new[]
            {
                $"💜 Estou atualmente em {client.Guilds.Count} servidores",
                "💜 Online | Pronta Para Ajudar!",
                "💜 Epstein Store"
            };

            await client.SetCustomStatusAsync(statusDinamicos[i]);

            i = (i + 1) % statusDinamicos.Length;
            await Task.Delay(TimeSpan.FromSeconds(3));
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
