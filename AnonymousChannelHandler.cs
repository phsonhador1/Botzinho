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

        // ID do seu canal anônimo
        private readonly ulong _canalAlvoId = 1497366111992418475;

        public AnonymousChannelHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += InterceptarMensagemAsync;
        }

        private async Task InterceptarMensagemAsync(SocketMessage msg)
        {
            if (msg.Author.IsBot || msg.Author.IsWebhook) return;
            if (msg.Channel.Id != _canalAlvoId || msg is not SocketUserMessage userMsg) return;

            try
            {
                var embed = new EmbedBuilder()
                    .WithColor(new Color(220, 40, 40));

                bool temConteudo = false;

                if (!string.IsNullOrWhiteSpace(userMsg.Content))
                {
                    embed.WithDescription(userMsg.Content);
                    temConteudo = true;
                }

                if (userMsg.Attachments.Any())
                {
                    var anexo = userMsg.Attachments.First();

                    if (anexo.ContentType != null && anexo.ContentType.StartsWith("image/"))
                    {
                        embed.WithImageUrl(anexo.Url);
                    }
                    else
                    {
                        embed.AddField("📎 Anexo", $"[Clique aqui para baixar o arquivo]({anexo.Url})");
                    }
                    temConteudo = true;
                }

                if (temConteudo)
                {
                    // 1. O bot envia o embed PRIMEIRO
                    await msg.Channel.SendMessageAsync(embed: embed.Build());

                    // 2. Micro-atraso de meio segundo (500ms) para evitar o bug de tela do Discord
                    await Task.Delay(500);

                    // 3. O bot apaga a mensagem original DEPOIS
                    await userMsg.DeleteAsync();
                }
            }
            catch (Exception ex)
            {
                // Se der erro aqui, 99% de chance de faltar a permissão "Gerenciar Mensagens" para o bot no canal
                Console.WriteLine($"[Erro Canal Anonimo]: {ex.Message}");
            }
        }
    }
}
