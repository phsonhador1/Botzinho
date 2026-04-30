using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Core
{
    public class HelpModule
    {
        private readonly DiscordSocketClient _client;

        // Cooldown exclusivo para o painel de ajuda (2 segundos)
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();

        public HelpModule(DiscordSocketClient client)
        {
            _client = client;

            // Assinamos os eventos UMA ÚNICA VEZ aqui no construtor
            _client.MessageReceived += HandleMessage;
            _client.SelectMenuExecuted += HandleSelectMenu;
        }

        private async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot || msg is not SocketUserMessage userMsg) return;
            var content = msg.Content.ToLower().Trim();

            // Verifica se digitou zhelp ou zajuda
            if (content == "zhelp" || content == "zajuda")
            {
                var user = msg.Author;

                // --- TRAVA DE 2 SEGUNDOS ---
                if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 2)
                {
                    var aviso = await msg.Channel.SendMessageAsync($"<:bloquearzeus:1499124950596849664> Aguarde **2 segundos** para abrir o **zhelp** novamente.");
                    _ = Task.Delay(2000).ContinueWith(_ => aviso.DeleteAsync());
                    return;
                }
                _cooldowns[user.Id] = DateTime.UtcNow;
                // ---------------------------

                var botUser = _client.CurrentUser;

                // Embed principal
                var eb = new EmbedBuilder()
                    .WithAuthor($"Ajuda | Zeus Bot", botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                    .WithColor(new Color(255, 0, 0)) // Vermelho forte
                    .WithThumbnailUrl(botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                    .WithDescription($"• Bem-vindo(a) **{user.Username}** ao **painel de comandos/ajuda - Zeus Bot**\n\n **Selecione uma categoria abaixo** para ver os **comandos disponíveis** do **bot**.")
                    .WithFooter($"Executado por: {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

                // Criação do Menu Dropdown
                var menuBuilder = new SelectMenuBuilder()
                    .WithCustomId($"help_menu_{user.Id}")
                    .WithPlaceholder("Selecione uma categoria")
                    .AddOption("Economia", "help_economia", "Comandos de economia", Emote.Parse("<:moneyzeus:1499549856036032622>"))
                    .AddOption("Cassino", "help_cassino", "Comandos de apostas e jogos", Emote.Parse("<:botzeus:1499548718377205921>"))
                    .AddOption("Moderação", "help_moderacao", "Comandos de moderação", Emote.Parse("<:botzeus:1499548718377205921>"));

                var cb = new ComponentBuilder().WithSelectMenu(menuBuilder);

                await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
            }
        }

        // --- SISTEMA QUE RESPONDE QUANDO O USUÁRIO CLICA NO MENU ---
        private async Task HandleSelectMenu(SocketMessageComponent component)
        {
            var customId = component.Data.CustomId;

            if (!customId.StartsWith("help_menu_")) return;

            var userIdString = customId.Replace("help_menu_", "");
            if (ulong.TryParse(userIdString, out ulong userId))
            {
                if (component.User.Id != userId)
                {
                    await component.RespondAsync("<:erro:1493078898462949526> Este painel não é seu! Digite **zhelp** para abrir o seu próprio menu.", ephemeral: true);
                    return;
                }

                await component.DeferAsync();

                var selected = component.Data.Values.First();
                var botUser = _client.CurrentUser;
                var user = component.User;

                var eb = new EmbedBuilder()
                    .WithAuthor($"Ajuda | Zeus Bot", botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                    .WithColor(new Color(255, 0, 0)) // Vermelho forte
                    .WithThumbnailUrl(botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                    .WithFooter($"executado por: {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

                // Legenda padrão para o topo das categorias
                string legenda = "**Confira os comandos abaixo:**\n\n";

                // Design limpo com bolinha preenchida e vazada (sem setas)
                if (selected == "help_economia")
                {
                    eb.WithTitle("Economia")
                      .WithDescription(legenda +
                        "• **zsaldo**:\n" +
                        " ◦ Veja sua carteira e banco.\n\n" +
                        "• **zdaily**:\n" +
                        " ◦ Resgate seu bônus diário.\n\n" +
                        "• **zdep [valor/all]**:\n\n" +
                        " ◦ Guarde moedas no banco.\n\n" +
                        "• **zpay [@user] [valor]**:\n" +
                        " ◦ Transfira dinheiro para alguém.\n\n" +
                        "• **ztransacoes**:\n" +
                        " ◦ Veja seu extrato bancário detalhado.\n\n" +
                        "• **zrank**:\n" +
                        " ◦ Veja os membros mais ricos do servidor.\n\n");
                }
                else if (selected == "help_cassino")
                {
                    eb.WithTitle("Cassino")
                      .WithDescription(legenda +
                        "• **zroleta [valor/all]**:\n" +
                        " ◦ Aposte na roleta (Branco, Preto ou Vermelho).\n\n" +
                        "• **zcf [valor/all]**:\n" +
                        " ◦ Aposte no cara ou coroa (Coinflip).\n\n" +
                        "• **zbj [valor/all]**:\n" +
                        " ◦ Jogue Blackjack contra o Dealer (21).\n\n" +
                        "• **zapostar [@user] [valor]**:\n" +
                        " ◦ Desafie alguém para um Duelo.\n\n" +
                        "• **zcrash [valor/all]**:\n" +
                        " ◦ Aposte no Crash do servidor.\n");
                }
                else if (selected == "help_moderacao")
                {
                    eb.WithTitle("Moderação")
                      .WithDescription(legenda +
                        "• **zclear [quant]**:\n" +
                        " ◦ Apaga mensagens do canal.\n\n" +
                        "• **zlock** e **zunlock**:\n" +
                        " ◦ Tranca ou destranca o canal atual.\n\n" +
                        "• **zslowmode [segundos]**:\n" +
                        " ◦ Define o modo lento do canal.\n" +
                        "• **zban [@user] (motivo)**:\n" +
                        " ◦ Bane um usuário.\n\n" +
                        "• **zunban [id]**:\n" +
                        " ◦ Desbane um usuário.\n\n" +
                        "• **zkick [@user] (motivo)**:\n" +
                        " ◦ Expulsa um usuário.\n\n" +
                        "• **zmute [@user] [tempo]**:\n" +
                        " ◦ Silencia um usuário (ex: 10m, 1h).\n\n" +
                        "• **zunmute [@user]**:\n" +
                        " ◦ Remove o silenciamento.\n");
                }

                var menuBuilder = new SelectMenuBuilder()
                    .WithCustomId($"help_menu_{user.Id}")
                    .WithPlaceholder("Selecione uma categoria")
                    .AddOption("Economia", "help_economia", "Comandos de economia", Emote.Parse("<:moneyzeus:1499549856036032622>"))
                    .AddOption("Cassino", "help_cassino", "Comandos de apostas e jogos", Emote.Parse("<:botzeus:1499548718377205921>"))
                    .AddOption("Moderação", "help_moderacao", "Comandos de moderação", Emote.Parse("<:botzeus:1499548718377205921>"));

                var cb = new ComponentBuilder().WithSelectMenu(menuBuilder);

                await component.ModifyOriginalResponseAsync(x => {
                    x.Embed = eb.Build();
                    x.Components = cb.Build();
                });
            }
        }
    }
}
