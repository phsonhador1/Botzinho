using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lavalink4NET;
using Lavalink4NET.Extensions;
using Lavalink4NET.DiscordNet; // <--- CORRIGIDO COM PONTO
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Botzinho.Admins;
using Botzinho.Moderation;
using Botzinho.Economy;
using Botzinho.Music;

// --- CONFIGURAÇÃO DE INTENTS ---
var client = new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.AllUnprivileged |
                     GatewayIntents.MessageContent |
                     GatewayIntents.GuildMembers |
                     GatewayIntents.GuildVoiceStates,
    AlwaysDownloadUsers = true,
    MessageCacheSize = 100
});

// --- MONTA O HOST PARA O LAVALINK4NET ---
var hostBuilder = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddSingleton(client);
        services.AddSingleton<DiscordSocketClient>(client);

        services.AddLavalink();
        services.ConfigureLavalink(options =>
        {
            // === COLOQUE SEUS DADOS AQUI ===
            options.BaseAddress = new Uri("COLE_AQUI_O_SEU_LINK_DO_RAILWAY_COM_HTTPS");
            options.Passphrase = "COLE_AQUI_A_SUA_SENHA";
            options.ReadyTimeout = TimeSpan.FromSeconds(15);
        });

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
    });

var host = hostBuilder.Build();
var services = host.Services;

// --- INICIALIZAÇÃO DOS MÓDULOS ---
new Botzinho.Admin.AdminControleModule(client);
var interactionService = new InteractionService(client);
var adminModule = new AdminModule(client);

ModerationHelper.InicializarTabelas();
var economyHandler = new Botzinho.Economy.EconomyHandler(client);
Botzinho.Economy.EconomyHelper.InicializarTabelas();
var cassino = new Botzinho.Cassino.CassinoModule(client);
var help = new Botzinho.Core.HelpModule(client);
Botzinho.Core.AutoRankService.Iniciar(client);
var apostas = new Botzinho.Cassino.ApostaModule(client);

var audioService = services.GetRequiredService<IAudioService>();
var musicHandler = new Botzinho.Music.MusicHandler(client, audioService);

client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };

client.Ready += async () =>
{
    _ = Task.Run(async () => {
        try { await host.StartAsync(); }
        catch (Exception ex) { Console.WriteLine($"[Lavalink Start Error]: {ex.Message}"); }
    });

    await interactionService.AddModulesAsync(typeof(NukeModule).Assembly, services);
    await interactionService.RegisterCommandsGloballyAsync(true);

    foreach (var guild in client.Guilds)
        AdminModule.GarantirAcessoInicialConfigServer(guild);

    Console.WriteLine($"Bot online como {client.CurrentUser.Username}");

    _ = Task.Run(async () =>
    {
        while (true)
        {
            string statusTop1 = "👑 Top 1: Ninguém";
            try
            {
                var guildId = client.Guilds.FirstOrDefault()?.Id ?? 0;
                if (guildId != 0)
                {
                    var top10 = EconomyHelper.GetTop10(guildId);
                    if (top10 != null && top10.Any())
                    {
                        var top1 = top10.First();
                        var usuario = client.GetUser(top1.UserId) as IUser ?? await client.Rest.GetUserAsync(top1.UserId);
                        string nomeTop1 = usuario != null ? usuario.Username : "Desconhecido";
                        statusTop1 = $"👑 O Magnata Rico - {nomeTop1} com {EconomyHelper.FormatarSaldo(top1.Total)}";
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"Erro status: {ex.Message}"); }

            string[] statusAtual = new[]
            {
                $"💜 Atualmente em {client.Guilds.Count} servidores",
                "🙂 Online | Pronta Para Ajudar!",
                "✨ use zhelp para descobrir os comandos",
                statusTop1
            };

            foreach (var st in statusAtual)
            {
                await client.SetStatusAsync(UserStatus.DoNotDisturb);
                await client.SetCustomStatusAsync(st);
                await Task.Delay(TimeSpan.FromSeconds(15));
            }
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
            await RespondAsync("<:erro:1493078898462949526> Sem permissão.", ephemeral: true);
            return;
        }
        await DeferAsync();
        var embed = AdminModule.CriarEmbedPrincipal(Context.Guild as SocketGuild);
        var components = AdminModule.CriarMenuPrincipal();
        var msg = await FollowupAsync(embed: embed, components: components);
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
            props.PermissionOverwrites = new Optional<IEnumerable<Overwrite>>(channel.PermissionOverwrites.ToList());
        });
        await channel.DeleteAsync();
        await newChannel.SendMessageAsync($".");
    }
}
