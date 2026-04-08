using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

var client = new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages
});

var services = new ServiceCollection()
    .AddSingleton(client)
    .BuildServiceProvider();

var interactionService = new InteractionService(client);

// Lista de status que vão ficar rotacionando
string[] statusList = new[]
{
    "Epstein Store",
    
};

client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };
client.Ready += async () =>
{
    await interactionService.AddModuleAsync<NukeModule>(services);
    await interactionService.RegisterCommandsGloballyAsync();
    Console.WriteLine($"Bot online como {client.CurrentUser.Username}");

    // Inicia a rotação de status
    _ = Task.Run(async () =>
    {
        int i = 0;
        while (true)
        {
            await client.SetStatusAsync(UserStatus.DoNotDisturb);
            await client.SetGameAsync(statusList[i], type: ActivityType.Streaming);
            i = (i + 1) % statusList.Length;
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

public class NukeModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("nuke", "Limpa todas as mensagens do canal")]
    [RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task NukeAsync()
    {
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
