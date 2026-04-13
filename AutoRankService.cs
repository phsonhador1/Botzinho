using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Botzinho.Core
{
    public static class AutoRankService
    {
        // ⚠️ ID DO CANAL DE RANK (PÚBLICO)
        private const ulong ID_CANAL_RANK = 1487905632261505024;

        // ⚠️ COLOQUE O ID DO SEU CANAL DE LOGS PRIVADO AQUI
        private const ulong ID_CANAL_LOGS = 1492995092166869002;

        public static void Iniciar(DiscordSocketClient client)
        {
            _ = Task.Run(() => LoopRank(client));
            _ = Task.Run(() => LoopSorteio(client));
        }

        private static async Task LoopRank(DiscordSocketClient client)
        {
            while (client.ConnectionState != ConnectionState.Connected)
                await Task.Delay(5000);

            await Task.Delay(TimeSpan.FromMinutes(5));

            while (true)
            {
                try
                {
                    var channel = client.GetChannel(ID_CANAL_RANK) as SocketTextChannel;

                    if (channel != null)
                    {
                        var guild = channel.Guild;
                        var top10 = EconomyHelper.GetTop10(guild.Id);

                        string path = await EconomyImageHelper.GerarImagemRank(guild, top10);

                        var msg = await channel.SendFileAsync(path,
                            "<a:trofeu:1493063952060387479> **Top Ricos Do Servidor**\n" +
                            "<:whitemoney:1493119805534900346> Confira quem são os membros mais <:coroa:1493119946547396689> **Magnatas** do momento!");

                        if (File.Exists(path)) File.Delete(path);

                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromMinutes(5));
                            try { await msg.DeleteAsync(); } catch { }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Erro AutoRank]: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMinutes(30));
            }
        }

        private static async Task LoopSorteio(DiscordSocketClient client)
        {
            while (client.ConnectionState != ConnectionState.Connected)
                await Task.Delay(5000);

            var idsPermitidos = new List<ulong>
            {
                1431655151105474755,
                1472642376970404002,
                1437491644286107838,
                1469449943390617714,
                1187711938805907527,
                877026652167753761,
                1489775731667107883,
                1465039524508864848,
                1491088346909249697,
                1445779233052823604
            };

            // Log de inicialização privado
            await EnviarLogPrivado(client, "🟢 **Monitoramento Ativo:** Primeiro sorteio em 10 minutos.");

            // --- ADIÇÃO: MENSAGEM DO PRIMEIRO SORTEIO COM CONTADOR ---
            var channelInit = client.GetChannel(ID_CANAL_RANK) as SocketTextChannel;
            if (channelInit != null)
            {
                long unixInit = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
                await channelInit.SendMessageAsync($"⏳ **Monitoramento iniciado!** O primeiro sorteio acontecerá <t:{unixInit}:R>.");
            }

            await Task.Delay(TimeSpan.FromMinutes(10));

            while (true)
            {
                try
                {
                    var channel = client.GetChannel(ID_CANAL_RANK) as SocketTextChannel;

                    if (channel != null)
                    {
                        var mensagensAntigas = await channel.GetMessagesAsync(30).FlattenAsync();

                        // Atualizamos a lista de limpeza para apagar os contadores antigos também
                        var lixoParaApagar = mensagensAntigas.Where(m =>
                            m.Author.Id == client.CurrentUser.Id &&
                            (m.Content.Contains("O magnata sortudo") || 
                             m.Content.Contains("Sorteando...") || 
                             m.Content.Contains("SORTEIO CONCLUÍDO!") ||
                             m.Content.Contains("O próximo sorteio acontecerá") ||
                             m.Content.Contains("O primeiro sorteio acontecerá"))
                        );

                        foreach (var msgAntiga in lixoParaApagar)
                        {
                            try { await msgAntiga.DeleteAsync(); } catch { }
                        }

                        var msgStatus = await channel.SendMessageAsync("<a:carregandoportal:1492944498605686844> **Sorteando...** Vamos ver quem vai ser o sortudo.");
                        await Task.Delay(5000);

                        var listaUsuarios = await channel.Guild.GetUsersAsync().FlattenAsync();
                        var membros = listaUsuarios.Where(u => !u.IsBot && idsPermitidos.Contains(u.Id)).ToList();

                        if (membros.Count > 0)
                        {
                            var random = new Random();
                            var ganhador = membros[random.Next(membros.Count)];
                            long valorSorteado = random.Next(50000, 100001);

                            EconomyHelper.AdicionarBanco(channel.Guild.Id, ganhador.Id, valorSorteado);
                            EconomyHelper.RegistrarTransacao(channel.Guild.Id, client.CurrentUser.Id, ganhador.Id, valorSorteado, "SORTEIO_AUTO");

                            try { await msgStatus.DeleteAsync(); } catch { }

                            // Mensagem do vencedor
                            await channel.SendMessageAsync($"<a:ganhador:1493088070923452599> O magnata sortudo desta vez foi: <@{ganhador.Id}>, ganhou <:mais:1493267829611303023> `{EconomyHelper.FormatarSaldo(valorSorteado)}` direto no banco!");

                            // --- ADIÇÃO: MENSAGEM DO PRÓXIMO SORTEIO COM CONTADOR ANIMADO ---
                            long unixProximo = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
                            await channel.SendMessageAsync($"⏳ **Fique de olho!** O próximo sorteio acontecerá <t:{unixProximo}:R>.");

                            // Envia log para o seu servidor privado
                            var proximo = DateTime.Now.AddMinutes(15);
                            await EnviarLogPrivado(client, $"✅ **Sorteio Realizado!**\n🏆 Ganhador: <@{ganhador.Id}>\n⏰ Próximo sorteio às: **{proximo:HH:mm:ss}**");
                        }
                        else
                        {
                            try { await msgStatus.DeleteAsync(); } catch { }
                            await EnviarLogPrivado(client, "⚠️ **Aviso:** Nenhum ID da whitelist encontrado para o sorteio.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Erro Sorteio]: {ex.Message}");
                    await EnviarLogPrivado(client, $"❌ **Erro no Loop de Sorteio:** {ex.Message}");
                }

                // O bot espera os 15 minutos, enquanto o Discord conta visualmente na tela dos usuários!
                await Task.Delay(TimeSpan.FromMinutes(15));
            }
        }

        // Método para enviar os Logs para o seu servidor separado
        private static async Task EnviarLogPrivado(DiscordSocketClient client, string texto)
        {
            try
            {
                var logChannel = client.GetChannel(ID_CANAL_LOGS) as SocketTextChannel;
                if (logChannel != null)
                {
                    await logChannel.SendMessageAsync($"[LOG ZOE] | {texto}");
                }
            }
            catch { }
        }
    }
}
