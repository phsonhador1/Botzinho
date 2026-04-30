using Discord;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Handlers
{
    public class AnonymousChannelHandler
    {
        private readonly DiscordSocketClient _client;

        // ⚠️ Substitua pelo ID REAL do seu canal anônimo
        private readonly ulong _canalAlvoId = 1498013207686811798;

        public AnonymousChannelHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += InterceptarMensagemAsync;
        }

        private async Task InterceptarMensagemAsync(SocketMessage msg)
        {
            // 1. Prevenção de loop: Ignora bots e Webhooks
            if (msg.Author.IsBot || msg.Author.IsWebhook) return;

            // 2. Valida o canal e o tipo de mensagem
            if (msg.Channel.Id != _canalAlvoId || msg is not SocketUserMessage userMsg) return;

            try
            {
                // 3. Apaga a mensagem do usuário imediatamente
                await userMsg.DeleteAsync();

                // 4. Constrói o Embed Premium (Barrinha Vermelha de separação)
                var embed = new EmbedBuilder()
                    .WithColor(new Color(220, 40, 40)); // Vermelho ZeusBot

                bool temConteudo = false;

                // 5. Verifica se tem texto e adiciona
                if (!string.IsNullOrWhiteSpace(userMsg.Content))
                {
                    embed.WithDescription(userMsg.Content);
                    temConteudo = true;
                }

                // 6. Verifica se o usuário mandou alguma imagem/anexo
                if (userMsg.Attachments.Any())
                {
                    var anexo = userMsg.Attachments.First();

                    // Se for imagem, exibe grandona no embed
                    if (anexo.ContentType != null && anexo.ContentType.StartsWith("image/"))
                    {
                        embed.WithImageUrl(anexo.Url);
                    }
                    else
                    {
                        // Se for um arquivo (ex: .zip, .txt), coloca como link
                        embed.AddField("📎 Anexo", $"[Clique aqui para baixar o arquivo]({anexo.Url})");
                    }
                    temConteudo = true;
                }

                // 7. Só envia se a mensagem não for um "fantasma"
                if (temConteudo)
                {
                    await msg.Channel.SendMessageAsync(embed: embed.Build());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Erro Canal Anonimo]: {ex.Message}");
            }
        }
    }
}
