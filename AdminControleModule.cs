using Discord;
using Discord.Interactions; // Adicionado para o Slash Command funcionar
using Discord.WebSocket;
using Botzinho.Economy;
using Botzinho.Core;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Admin
{
    public class AdminControleModule
    {
        private readonly DiscordSocketClient _client;

        // ⚠️ COLOQUE O ID DA SUA GUILDA (SERVIDOR) PÚBLICA AQUI
        private const ulong ID_SERVIDOR_PUBLICO = 148724449427220077;

        public AdminControleModule(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleAdminCommands;
        }

        private Task HandleAdminCommands(SocketMessage msg)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage userMsg) return;

                    var content = msg.Content.ToLower().Trim();

                    // Bloqueia quem não tem ID Autorizado
                    if (!EconomyHelper.IDsAutorizados.Contains(msg.Author.Id)) return;

                    // --- COMANDO MANUAL DE SORTEIO ---
                    if (content == "zsortear")
                    {
                        var guildPublica = _client.GetGuild(ID_SERVIDOR_PUBLICO);
                        if (guildPublica == null) { await msg.Channel.SendMessageAsync("❌ Erro: Configure o ID do servidor público no código."); return; }

                        var channelPublico = guildPublica.GetTextChannel(AutoRankService.ID_CANAL_RANK);
                        if (channelPublico == null) { await msg.Channel.SendMessageAsync("❌ Erro: O bot não encontrou o canal de rank no servidor público."); return; }

                        await msg.Channel.SendMessageAsync("⚡ **Sorteio forçado iniciado.** Ele aparecerá no servidor público idêntico ao automático.");

                        var msgStatus = await channelPublico.SendMessageAsync("<a:carregandoportal:1492944498605686844> **Sorteando...** Vamos ver quem vai ser o sortudo.");
                        await Task.Delay(5000);

                        var listaUsuarios = await guildPublica.GetUsersAsync().FlattenAsync();
                        var membros = listaUsuarios.Where(u => !u.IsBot && AutoRankService.IdsPermitidos.Contains(u.Id)).ToList();

                        if (membros.Count > 0)
                        {
                            var random = new Random();
                            var ganhador = membros[random.Next(membros.Count)];
                            long valorSorteado = new Random().Next(10000, 23000);

                            EconomyHelper.AdicionarBanco(guildPublica.Id, ganhador.Id, valorSorteado);
                            EconomyHelper.RegistrarTransacao(guildPublica.Id, _client.CurrentUser.Id, ganhador.Id, valorSorteado, "SORTEIO_AUTO");

                            try { await msgStatus.DeleteAsync(); } catch { }

                            await channelPublico.SendMessageAsync($"<a:ganhador:1493088070923452599> O magnata sortudo desta vez foi: <@{ganhador.Id}>, ganhou <:mais:1493267829611303023> `{EconomyHelper.FormatarSaldo(valorSorteado)}` direto no banco!");

                            await msg.Channel.SendMessageAsync($"✅ **Sucesso!** Ganhador do sorteio forçado: <@{ganhador.Id}>.");
                        }
                        else
                        {
                            try { await msgStatus.DeleteAsync(); } catch { }
                            await msg.Channel.SendMessageAsync("⚠️ **Falhou:** Nenhum ID da whitelist foi encontrado no servidor público.");
                        }
                    }

                    // --- PAINEL DE MONITORAMENTO ---
                    else if (content == "zpainel")
                    {
                        string tempoSorteio = AutoRankService.UnixProximoSorteio > 0 ? $"<t:{AutoRankService.UnixProximoSorteio}:R>" : "`Calculando ou Desligado`";
                        string tempoRank = AutoRankService.UnixProximoRank > 0 ? $"<t:{AutoRankService.UnixProximoRank}:R>" : "`Calculando ou Desligado`";

                        var eb = new EmbedBuilder()
                            .WithAuthor("⚙️ Painel de Controle da Zoe")
                            .WithDescription("Monitoramento ao vivo dos sistemas no servidor público:")
                            .AddField("🎁 Próximo Sorteio Auto", tempoSorteio, inline: true)
                            .AddField("🏆 Próximo Rank Auto", tempoRank, inline: true)
                            .WithColor(new Color(160, 80, 220))
                            .WithFooter("Acesso Exclusivo Staff");

                        await msg.Channel.SendMessageAsync(embed: eb.Build());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Erro Admin Module]: {ex.Message}");
                }
            }); return Task.CompletedTask;
        }
    }

    // --- NOVO SISTEMA ADICIONADO AQUI ---
    public class OwnerModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("zavisar_global", "Envia uma mensagem em um canal (Apenas Dono)")]
        public async Task AvisarGlobal(
            [Summary("canal_id", "ID do canal de destino")] string canalId,
            [Summary("mensagem", "A mensagem que a Zoe vai enviar")] string mensagem)
        {
            // Bloqueio de Segurança: Apenas o seu ID pode usar
            if (Context.User.Id != 1472642376970404002)
            {
                await RespondAsync("❌ Você não tem permissão para usar comandos de desenvolvedor.", ephemeral: true);
                return;
            }

            // Tenta converter o texto para um ID de canal válido
            if (ulong.TryParse(canalId, out ulong channelIdFinal))
            {
                // Pega o canal em qualquer servidor que a Zoe esteja
                var channel = Context.Client.GetChannel(channelIdFinal) as ITextChannel;

                if (channel != null)
                {
                    await channel.SendMessageAsync(mensagem);
                    await RespondAsync($"✅ Mensagem enviada com sucesso no canal **{channel.Name}**!", ephemeral: true);
                }
                else
                {
                    await RespondAsync("❌ Canal não encontrado. Verifique se o ID está correto e se a Zoe está no servidor.", ephemeral: true);
                }
            }
            else
            {
                await RespondAsync("❌ O ID do canal é inválido. Digite apenas números.", ephemeral: true);
            }
        }
    }
}
