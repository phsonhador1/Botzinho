using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Botzinho.Commands;
using Botzinho.Economy;
using Botzinho.Moderation;

// ==============================================================
// CONFIGURAÇÃO DO CLIENTE
// ==============================================================
var config = new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMembers,
    AlwaysDownloadUsers = true,
    MessageCacheSize = 100
};

var client = new DiscordSocketClient(config);

// ==============================================================
// CONSTRUÇÃO DO HOST (Dependency Injection)
// ==============================================================
var hostBuilder = Host.CreateDefaultBuilder()
    .ConfigureServices((_, services) =>
    {
        services.AddSingleton(client);
        services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));
        // Adicione aqui outros serviços que seus módulos precisem (ex: EconomyHandler)
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
    });

var host = hostBuilder.Build();
var serviceProvider = host.Services;

// ==============================================================
// INICIALIZAÇÃO DE BANCO DE DADOS / HELPERS
// ==============================================================
ModerationHelper.InicializarTabelas();
EconomyHelper.InicializarTabelas();
// Outras inicializações estáticas...

// ==============================================================
// INTERCEPTADOR DE MENSAGENS (Comando de Texto zbin)
// ==============================================================
client.MessageReceived += async message =>
{
    if (message.Author.IsBot || message is not SocketUserMessage userMessage) return;

    string textoCru = userMessage.Content.Trim();

    if (textoCru.StartsWith("zbin ", StringComparison.OrdinalIgnoreCase))
    {
        string cargaBin = textoCru.Substring(5).Trim();
        // Executa em uma Task separada para não travar o Gateway
        _ = Task.Run(() => BinService.ExecutarZBinAsync(userMessage, cargaBin));
    }
};

// ==============================================================
// EVENTO READY & REGISTRO DE COMANDOS
// ==============================================================
client.Ready += async () =>
{
    var interactionService = serviceProvider.GetRequiredService<InteractionService>();

    // Registra os módulos que herdam de InteractionModuleBase
    await interactionService.AddModulesAsync(typeof(NukeModule).Assembly, serviceProvider);
    await interactionService.RegisterCommandsGloballyAsync(true);

    Console.WriteLine($"[Bot] Online como {client.CurrentUser.Username}");

    // Loop de Status
    _ = Task.Run(async () =>
    {
        while (true)
        {
            string statusTop1 = "👑 Top 1: Ninguém";
            try
            {
                var firstGuild = client.Guilds.FirstOrDefault();
                if (firstGuild != null) continue;
                {
                    var top10 = EconomyHelper.GetTop10(firstGuild.Id);
                    if (top10 != null && top10.Any())
                    {
                        var top1 = top10.First();
                        var user = (client.GetUser(top1.UserId) as IUser) ?? await client.Rest.GetUserAsync(top1.UserId);
                        statusTop1 = $"👑 Rico: {(user != null ? user.Username : "Desconhecido")} ({EconomyHelper.FormatarSaldo(top1.Total)})";
                    }
                }
            }
            catch { /* Ignora erros de status */ }

            string[] listaStatus = {
                $"💜 {client.Guilds.Count} Servidores",
                "✨ zhelp para comandos",
                statusTop1
            };

            foreach (var st in listaStatus)
            {
                await client.SetCustomStatusAsync(st);
                await Task.Delay(TimeSpan.FromSeconds(15));
            }
        }
    });
};

// ==============================================================
// EXECUÇÃO DE SLASH COMMANDS
// ==============================================================
client.InteractionCreated += async interaction =>
{
    var ctx = new SocketInteractionContext(client, interaction);
    var interactionService = serviceProvider.GetRequiredService<InteractionService>();
    await interactionService.ExecuteCommandAsync(ctx, serviceProvider);
};

// ==============================================================
// LOGIN
// ==============================================================
var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? "SEU_TOKEN_AQUI";
await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();

await host.RunAsync();

// ==============================================================
// MÓDULOS DE COMANDOS (EXEMPLOS)
// ==============================================================
public class NukeModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("nuke", "Limpa todas as mensagens do canal")]
    public async Task NukeAsync()
    {
        var user = (SocketGuildUser)Context.User;
        // Lógica de permissão...
        await DeferAsync(ephemeral: true);
        var channel = (ITextChannel)Context.Channel;

        var newChannel = await channel.Guild.CreateTextChannelAsync(channel.Name, props => {
            props.Topic = channel.Topic;
            props.CategoryId = channel.CategoryId;
            props.Position = channel.Position;
            props.PermissionOverwrites = new Optional<IEnumerable<Overwrite>>(channel.PermissionOverwrites.ToList());
        });

        await channel.DeleteAsync();
        await newChannel.SendMessageAsync(".");
    }
}
