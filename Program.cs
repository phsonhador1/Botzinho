using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Botzinho.Admins;
using Botzinho.Moderation;
using Botzinho.Economy;
using Botzinho.Handlers;

// ==============================================================
// CLIENTE DISCORD
// ==============================================================
var client = new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.AllUnprivileged |
                     GatewayIntents.MessageContent |
                     GatewayIntents.GuildMembers,
    AlwaysDownloadUsers = true,
    MessageCacheSize = 100
});

// ★ CommandService para comandos com prefixo (zmute, zlock, etc)
var commandService = new CommandService(new CommandServiceConfig
{
    DefaultRunMode = Discord.Commands.RunMode.Async,
    CaseSensitiveCommands = false,
    LogLevel = LogSeverity.Info
});

// ==============================================================
// HOST (DI simples)
// ==============================================================
var hostBuilder = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddSingleton(client);
        services.AddSingleton<DiscordSocketClient>(client);
        services.AddSingleton<BaseSocketClient>(client);
        services.AddSingleton(commandService);

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
    });

var host = hostBuilder.Build();
var services = host.Services;

// ==============================================================
// INICIALIZAÇÃO DOS MÓDULOS
// ==============================================================
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

var roleplay = new Botzinho.Roleplay.RoleplayHandler(client);

var anonymousHandler = new AnonymousChannelHandler(client);

var utility = new Botzinho.Utility.UtilityHandler(client);

client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };
commandService.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };

// ★ REGISTRA OS MÓDULOS DE COMANDO COM PREFIXO
await commandService.AddModulesAsync(Assembly.GetEntryAssembly(), services);
Console.WriteLine($"[CommandService] {commandService.Commands.Count()} comandos prefixo carregados.");

// ★★★ DICIONÁRIO DE USOS CORRETOS DOS COMANDOS ★★★
var usoCorreto = new Dictionary<string, string>
{
    { "ban", "zban @usuario (motivo)" },
    { "unban", "zunban [ID do usuário]" },
    { "kick", "zkick @usuario (motivo)" },
    { "mute", "zmute @usuario [duração] (motivo)\n*Exemplo:* `zmute @fulano 10m spammer`\n*Durações:* `10m`, `1h`, `1d`" },
    { "unmute", "zunmute @usuario" },
    { "clear", "zclear [quantidade]\n*Exemplo:* `zclear 50`" },
    { "slowmode", "zslowmode [segundos]\n*Exemplo:* `zslowmode 5`" },
    { "lock", "zlock" },
    { "unlock", "zunlock" }
};

// ★ HANDLER QUE PROCESSA MENSAGENS COM PREFIXO 'z'
client.MessageReceived += async (rawMsg) =>
{
    if (rawMsg is not SocketUserMessage msg) return;
    if (msg.Author.IsBot) return;

    int argPos = 0;
    if (!msg.HasCharPrefix('z', ref argPos) &&
        !msg.HasCharPrefix('Z', ref argPos)) return;

    var ctx = new SocketCommandContext(client, msg);
    var result = await commandService.ExecuteAsync(ctx, argPos, services);

    if (result.IsSuccess) return;

    // ★ TRATAMENTO DE ERROS COM EMBED BONITO ★
    var guild = ctx.Guild as SocketGuild;
    if (guild == null) return;

    string nomeComando = ExtrairNomeComando(msg.Content);

    switch (result.Error)
    {
        // Argumentos errados ou faltando
        case CommandError.BadArgCount:
        case CommandError.ParseFailed:
        case CommandError.ObjectNotFound:
            if (usoCorreto.TryGetValue(nomeComando, out var uso))
            {
                var embedUso = new EmbedBuilder()
                    .WithColor(ModerationHelper.CorEmbed)
                    .WithDescription($"<:erro:1493078898462949526> **Uso incorreto do comando!**\n\n**Uso correto:** `{uso}`")
                    .WithFooter(ModerationHelper.RodapePadrao(guild))
                    .Build();

                await msg.Channel.SendMessageAsync(embed: embedUso);
            }
            break;

        // Comando não existe → ignora silenciosamente (pra não responder qualquer "z" digitado)
        case CommandError.UnknownCommand:
            break;

        // Permissão do bot insuficiente
        case CommandError.UnmetPrecondition:
            await msg.Channel.SendMessageAsync(
                embed: ModerationHelper.CriarEmbedErro($"Não foi possível executar: {result.ErrorReason}", guild));
            break;

        // Outros erros
        default:
            Console.WriteLine($"[Cmd Error] {result.Error}: {result.ErrorReason}");
            break;
    }
};

// Extrai o nome do comando da mensagem (ex: "zmute @user" -> "mute")
static string ExtrairNomeComando(string conteudo)
{
    if (string.IsNullOrEmpty(conteudo) || conteudo.Length < 2) return "";
    var primeiraPalavra = conteudo.Split(' ')[0].ToLower();
    return primeiraPalavra.StartsWith("z") ? primeiraPalavra.Substring(1) : primeiraPalavra;
}

// ==============================================================
// EVENTO READY
// ==============================================================
client.Ready += async () =>
{
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
                        statusTop1 = $"👑 O Magnata Rico - {(usuario != null ? usuario.Username : "Desconhecido")} com {EconomyHelper.FormatarSaldo(top1.Total)}";
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"Erro status: {ex.Message}"); }

            string[] statusAtual = { $"Estou em {client.Guilds.Count} servidores", "Online | Use zhelp", "discord.gg/senzala", statusTop1 };
            foreach (var st in statusAtual)
            {
                await client.SetStatusAsync(UserStatus.Online);
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

// ==============================================================
// LOGIN & START
// ==============================================================
var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN")
    ?? throw new Exception("TOKEN MISSING");

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();

await host.StartAsync();
Console.WriteLine("[Boot] Host iniciado.");
await host.WaitForShutdownAsync();


// ==============================================================
// CLASSES DE COMANDOS SLASH (mantidos)
// ==============================================================
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
        var resultado = AdminModule.ChecarPermissaoCompleta(Context.Guild.Id, user, "nuke", GuildPermission.ManageChannels);
        if (resultado != null) { await RespondAsync(resultado, ephemeral: true); return; }
        await DeferAsync(ephemeral: true);
        var channel = (ITextChannel)Context.Channel;
        var newChannel = await channel.Guild.CreateTextChannelAsync(channel.Name, props =>
        {
            props.Topic = channel.Topic;
            props.CategoryId = channel.CategoryId;
            props.Position = channel.Position;
            props.IsNsfw = channel.IsNsfw;
            props.PermissionOverwrites = new Optional<IEnumerable<Overwrite>>(channel.PermissionOverwrites.ToList());
        });
        await channel.DeleteAsync();
        await newChannel.SendMessageAsync($".");
    }
}
