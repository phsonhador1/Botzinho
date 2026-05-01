using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Utility
{
    public class UtilityHandler
    {
        private readonly DiscordSocketClient _client;
        private static readonly Color CorEmbed = new Color(255, 71, 87);

        public UtilityHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;
            Console.WriteLine("[Utility] Handler iniciado.");
        }

        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage userMsg) return;

                    var content = msg.Content.Trim();
                    var contentLower = content.ToLower();

                    if (!contentLower.StartsWith("zavatar") && !contentLower.StartsWith("zav"))
                        return;

                    var autor = msg.Author as SocketGuildUser;
                    if (autor == null) return;

                    string[] partes = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    IUser alvo = autor; // padrão = quem usou

                    // 1. Se mencionou alguém
                    if (msg.MentionedUsers.Count > 0)
                    {
                        alvo = msg.MentionedUsers.First();
                    }
                    // 2. Se passou um ID (busca em qualquer servidor via API)
                    else if (partes.Length >= 2 && ulong.TryParse(partes[1], out ulong idFornecido))
                    {
                        try
                        {
                            // tenta no servidor primeiro
                            var noServer = autor.Guild.GetUser(idFornecido);
                            if (noServer != null) alvo = noServer;
                            else
                            {
                                // busca via REST (funciona pra qualquer user, mesmo fora do server)
                                var rest = await _client.Rest.GetUserAsync(idFornecido);
                                if (rest != null) alvo = rest;
                                else
                                {
                                    await msg.Channel.SendMessageAsync(
                                        embed: CriarEmbedErro($"Usuário com ID `{idFornecido}` não encontrado."));
                                    return;
                                }
                            }
                        }
                        catch
                        {
                            await msg.Channel.SendMessageAsync(
                                embed: CriarEmbedErro($"Não foi possível encontrar o usuário com ID `{idFornecido}`."));
                            return;
                        }
                    }

                    // Pega URL do avatar em alta resolução
                    string avatarUrl = alvo.GetAvatarUrl(ImageFormat.Auto, 1024)
                                       ?? alvo.GetDefaultAvatarUrl();

                    string nomeExibicao = (alvo as SocketGuildUser)?.DisplayName ?? alvo.GlobalName ?? alvo.Username;

                    var embed = new EmbedBuilder()
                        .WithColor(CorEmbed)
                        .WithAuthor($"Avatar de {nomeExibicao}", alvo.GetAvatarUrl() ?? alvo.GetDefaultAvatarUrl())
                        .WithDescription($"<:seta:1493089125979656385> [Clique aqui para baixar]({avatarUrl})")
                        .WithImageUrl(avatarUrl)
                        .WithFooter($"ID: {alvo.Id} • Solicitado por {autor.Username}",
                                    autor.GetAvatarUrl() ?? autor.GetDefaultAvatarUrl())
                        .WithCurrentTimestamp()
                        .Build();

                    await msg.Channel.SendMessageAsync(embed: embed);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Utility Error]: {ex.Message}");
                }
            });

            return Task.CompletedTask;
        }

        private static Embed CriarEmbedErro(string mensagem)
        {
            return new EmbedBuilder()
                .WithColor(CorEmbed)
                .WithDescription($"<:erro:1493078898462949526> {mensagem}")
                .Build();
        }
    }
}