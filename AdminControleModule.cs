using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using Botzinho.Core; // Isso resolve o erro do AutoRankService!
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Admin
{
    public class AdminControleModule
    {
        private readonly DiscordSocketClient _client;

        // ⚠️ COLOQUE O ID DO SEU SERVIDOR PÚBLICO AQUI
        private const ulong ID_SERVIDOR_PUBLICO = 1487244494272200774;

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

                    // Comandos que só os IDs Autorizados podem usar (O seu ID)
                    if (!EconomyHelper.IDsAutorizados.Contains(msg.Author.Id)) return;

                    // --- 1. COMANDO PARA FORÇAR SORTEIO REMOTO ---
                    if (content == "zsortear")
                    {
                        var guildPublica = _client.GetGuild(ID_SERVIDOR_PUBLICO);
                        if (guildPublica == null) { await msg.Channel.SendMessageAsync("❌ Erro: Bot não encontrou o servidor público."); return; }

                        var channelPublico = guildPublica.GetTextChannel(AutoRankService.ID_CANAL_RANK);
                        if (channelPublico == null) { await msg.Channel.SendMessageAsync("❌ Erro: Bot não encontrou o canal de rank no servidor público."); return; }

                        // Avisa você no privado que o processo começou
                        await msg.Channel.SendMessageAsync("⚡ **Iniciando sorteio idêntico ao automático no servidor público...**");

                        // --- SIMULAÇÃO EXATA DO SISTEMA AUTOMÁTICO ---
                        var msgStatus = await channelPublico.SendMessageAsync("<a:carregandoportal:1492944498605686844> **Sorteando...** Vamos ver quem vai ser o sortudo.");
                        await Task.Delay(5000);

                        var listaUsuarios = await guildPublica.GetUsersAsync().FlattenAsync();
                        var membros = listaUsuarios.Where(u => !u.IsBot && AutoRankService.IdsPermitidos.Contains(u.Id)).ToList();

                        if (membros.Count > 0)
                        {
                            var random = new Random();
                            var ganhador = membros[random.Next(membros.Count)];
                            long valorSorteado = random.Next(50000, 100001);

                            EconomyHelper.AdicionarBanco(guildPublica.Id, ganhador.Id, valorSorteado);

                            // Até o log fica registrado como automático para não bagunçar seu extrato
                            EconomyHelper.RegistrarTransacao(guildPublica.Id, _client.CurrentUser.Id, ganhador.Id, valorSorteado, "SORTEIO_AUTO");

                            try { await msgStatus.DeleteAsync(); } catch { }

                            // Mensagem FINAL exata do seu AutoRankService
                            await channelPublico.SendMessageAsync($"<a:ganhador:1493088070923452599> O magnata sortudo desta vez foi: <@{ganhador.Id}>, ganhou <:mais:1493267829611303023> `{EconomyHelper.FormatarSaldo(valorSorteado)}` direto no banco!");

                            // Confirmação no seu privado
                            await msg.Channel.SendMessageAsync($"✅ **Sucesso!** Sorteio enviado sem ninguém perceber que foi manual. Ganhador: <@{ganhador.Id}>.");
                        }
                        else
                        {
                            try { await msgStatus.DeleteAsync(); } catch { }
                            await msg.Channel.SendMessageAsync("⚠️ **Aviso:** Nenhum ID da whitelist encontrado para o sorteio no servidor.");
                        }
                    }

                    // --- 2. COMANDO PARA VER OS CONTADORES EXATOS ---
                    else if (content == "zpainel")
                    {
                        string tempoSorteio = AutoRankService.UnixProximoSorteio > 0 ? $"<t:{AutoRankService.UnixProximoSorteio}:R>" : "`Calculando...`";
                        string tempoRank = AutoRankService.UnixProximoRank > 0 ? $"<t:{AutoRankService.UnixProximoRank}:R>" : "`Calculando...`";

                        var eb = new EmbedBuilder()
                            .WithAuthor("⚙️ Painel de Controle da Zoe")
                            .WithDescription("Status dos sistemas automáticos rodando no servidor público:")
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
}
