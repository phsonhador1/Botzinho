using Botzinho.Admins;
using Botzinho.Moderation;
using Discord;
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
    GatewayIntents = GatewayIntents.Guilds
                   | GatewayIntents.GuildMessages
                   | GatewayIntents.MessageContent
                   | GatewayIntents.GuildMembers
});

var services = new ServiceCollection()
    .AddSingleton(client)
    .BuildServiceProvider();

var interactionService = new InteractionService(client);
var adminModule = new AdminModule(client);

ModerationHelper.InicializarTabelas();

client.Log += msg =>
{
    Console.WriteLine(msg);
    return Task.CompletedTask;
};

client.Ready += async () =>
{
    await interactionService.AddModulesAsync(typeof(ConfigServerModule).Assembly, services);
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

            string[] statusDinamicos = new[]
            {
                $"💜 Atualmente em {client.Guilds.Count} servidores",
                "💜 Olá! Sou a Zoe",
                "✨ Meu prefixo é z",
                "📌 Use zhelp para descobrir todos os meus comandos"
            };

            await client.SetCustomStatusAsync(statusDinamicos[i]);

            i = (i + 1) % statusDinamicos.Length;
            await Task.Delay(TimeSpan.FromSeconds(15));
        }
    });
};

client.MessageReceived += async message =>
{
    if (message is not SocketUserMessage msg) return;
    if (msg.Author.IsBot) return;
    if (msg.Channel is not SocketGuildChannel guildChannel) return;

    int argPos = 0;
    if (!msg.HasCharPrefix('z', ref argPos)) return;

    var conteudo = msg.Content.Substring(argPos).Trim();
    if (string.IsNullOrWhiteSpace(conteudo)) return;

    var partes = conteudo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var comando = partes[0].ToLower();

    // zhelp
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
        return;
    }

    // znuke
    if (comando == "nuke")
    {
        var user = msg.Author as SocketGuildUser;
        if (user == null) return;

        var guildId = user.Guild.Id;

        var resultado = AdminModule.ChecarPermissaoCompleta(
            guildId,
            user,
            "nuke",
            GuildPermission.ManageChannels
        );

        if (resultado != null)
        {
            await msg.Channel.SendMessageAsync(resultado);
            return;
        }

        if (msg.Channel is not ITextChannel channel)
        {
            await msg.Channel.SendMessageAsync("esse comando so pode ser usado em canal de texto.");
            return;
        }

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
        await newChannel.SendMessageAsync($"canal nukado por {msg.Author.Username}");
        return;
    }
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
