using Discord;
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
}
