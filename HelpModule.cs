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
            if (content == "zhelp" || content == "zajuda" || content == "top")
            {
                var user = msg.Author;

                // --- TRAVA DE 2 SEGUNDOS ---
                if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 2)
                {
                    var aviso = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> {user.Mention}, Vai com Calma viadinho, Aguarde **2 segundos** para abrir o **zhelp** novamente.");
                    _ = Task.Delay(2000).ContinueWith(_ => aviso.DeleteAsync());
                    return;
                }
                _cooldowns[user.Id] = DateTime.UtcNow;
                // ---------------------------

                var botUser = _client.CurrentUser;

                // Embed principal
                var eb = new EmbedBuilder()
          .WithAuthor($"Ajuda | Zeus Bot", botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
          .WithColor(new Color(160, 80, 220)) // Cor roxa da borda
                    .WithThumbnailUrl(botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl()) // Foto do Zeus Bot no canto
                    .WithDescription($"• Bem-vindo(a) {user.Username} ao **painel de comandos/ajuda - Zeus Bot**\n\n **Selecione uma categoria abaixo** para ver os **comandos disponíveis** do **bot**.")
          .WithFooter($"executado por: {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

                // Criação do Menu Dropdown (Select Menu)
                var menuBuilder = new SelectMenuBuilder()
          .WithCustomId($"help_menu_{user.Id}")
          .WithPlaceholder("Selecione uma categoria")
          .AddOption("Economia", "help_economia", "Comandos de economia", Emote.Parse("<:botportal:1492661012682248212>"))
          .AddOption("Cassino", "help_cassino", "Comandos de apostas e jogos", Emote.Parse("<:botportal:1492661012682248212>"))
          .AddOption("Moderação", "help_moderacao", "Comandos de moderação", Emote.Parse("<:botportal:1492661012682248212>"));

                var cb = new ComponentBuilder().WithSelectMenu(menuBuilder);

                await msg.Channel.SendMessageAsync(embed: eb.Build(), components: cb.Build());
            }
        }

        // --- SISTEMA QUE RESPONDE QUANDO O USUÁRIO CLICA NO MENU ---
        private async Task HandleSelectMenu(SocketMessageComponent component)
        {
            var customId = component.Data.CustomId;

            // Verifica se o ID do menu pertence ao sistema de ajuda
            if (!customId.StartsWith("help_menu_")) return;

            var userIdString = customId.Replace("help_menu_", "");
            if (ulong.TryParse(userIdString, out ulong userId))
            {
                // Trava de segurança: só quem digitou zhelp pode clicar no menu dele
                if (component.User.Id != userId)
                {
                    await component.RespondAsync("<:erro:1493078898462949526> Este painel não é seu Puta! Digite **zhelp** para abrir o seu próprio menu.", ephemeral: true);
                    return;
                }

                // CORREÇÃO: Avisa o Discord na hora que a seleção foi processada (Evita Interação Falhou)
                await component.DeferAsync();

                var selected = component.Data.Values.First(); // Pega a categoria selecionada
                var botUser = _client.CurrentUser;
                var user = component.User;

                var eb = new EmbedBuilder()
                  .WithAuthor($"Ajuda | Zeus Bot", botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                  .WithColor(new Color(160, 80, 220))
                  .WithThumbnailUrl(botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl())
                  .WithFooter($"Comando executado por: {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

                // Muda o conteúdo do Embed de acordo com o que foi clicado
                if (selected == "help_economia")
                {
                    eb.WithTitle("<:botportal:1492661012682248212> Economia")
                     .WithDescription(
                       "`zsaldo` - Veja sua carteira e banco\n" +
                       "`zdaily` - Resgate seu bônus diário\n" +
                       "`zdep [valor/all]` - Guarde moedas no banco\n" +
                       "`zpay [@user] [valor]` - Transfira dinheiro para alguém\n" +
                       "`ztransacoes` - Veja seu extrato bancário detalhado\n" +
                       "`zrank` - Veja os membros mais ricos do servidor\n" +
                       "`zrifa` - Mostra o prêmio acumulado e suas chances\n" +
                       "`zrifa comprar [valor]` - Compre participações na Rifa Semanal");
                }
                else if (selected == "help_cassino")
                {
                    eb.WithTitle("<:botportal:1492661012682248212> Cassino")
                     .WithDescription(
                       "`zroleta [valor/all]` - Aposte na roleta (Branco, Preto ou Vermelho)\n" +
                       "`zcf [valor/all]` - Aposte no cara ou coroa (Coinflip)\n" +
                       "`zbj [valor/all]` - Jogue Blackjack contra o Dealer (21)\n" +
                       "`zapostar [@user] [valor]` - Desafie alguém para um Duelo (X1)");
                }
                else if (selected == "help_moderacao")
                {
                    eb.WithTitle("<:botportal:1492661012682248212> Moderação")
                     .WithDescription(
                       "**Atenção:** Estes comandos usam `/` (Slash Commands).\n\n" +
                       "`/clear [quant]` - Apaga mensagens do canal\n" +
                       "`/lock` e `/unlock` - Tranca ou destranca o canal atual\n" +
                       "`/slowmode [segundos]` - Define o modo lento do canal\n" +
                       "`/ban [@user] [motivo]` - Bane um usuário\n" +
                       "`/unban [id]` - Desbane um usuário\n" +
                       "`/kick [@user] [motivo]` - Expulsa um usuário\n" +
                       "`/mute [@user] [tempo]` - Silencia um usuário (ex: 10m, 1h)\n" +
                       "`/unmute [@user]` - Remove o silenciamento");
                }

                // Reconstrói o menu para ele continuar funcionando (Mantendo o texto atualizado se desejar)
                var menuBuilder = new SelectMenuBuilder()
          .WithCustomId($"help_menu_{user.Id}")
          .WithPlaceholder("Selecione uma categoria")
          .AddOption("Economia", "help_economia", "Comandos de economia", Emote.Parse("<:botportal:1492661012682248212>"))
          .AddOption("Cassino", "help_cassino", "Comandos de apostas e jogos", Emote.Parse("<:botportal:1492661012682248212>"))
          .AddOption("Moderação", "help_moderacao", "Comandos de moderação", Emote.Parse("<:botportal:1492661012682248212>"));

                var cb = new ComponentBuilder().WithSelectMenu(menuBuilder);

                // CORREÇÃO: Usamos ModifyOriginalResponseAsync porque demos DeferAsync no topo!
                await component.ModifyOriginalResponseAsync(x => {
                    x.Embed = eb.Build();
                    x.Components = cb.Build();
                });
            }
        }
    }
}
