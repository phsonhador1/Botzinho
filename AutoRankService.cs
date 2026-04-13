using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Botzinho.Core
{
    public static class AutoRankService
    {
        // ⚠️ COLOQUE O ID DO CANAL AQUI
        private const ulong ID_CANAL_RANK = 1492995092166869002;

        // Guarda a mensagem do último sorteio para apagar quando começar um novo
        private static IUserMessage _ultimaMsgSorteio = null;

        public static void Iniciar(DiscordSocketClient client)
        {
            // Inicia o loop do rank em uma thread separada
            _ = Task.Run(() => LoopRank(client));

            // Inicia o loop do sorteio automático em outra thread separada
            _ = Task.Run(() => LoopSorteio(client));
        }

        private static async Task LoopRank(DiscordSocketClient client)
        {
            // Aguarda o bot estar totalmente pronto antes de começar
            while (client.ConnectionState != ConnectionState.Connected)
                await Task.Delay(5000);

            while (true)
            {
                try
                {
                    // 1. Procura o canal pelo ID (AQUI MUDAMOS PARA SocketTextChannel)
                    var channel = client.GetChannel(ID_CANAL_RANK) as SocketTextChannel;

                    if (channel != null)
                    {
                        var guild = channel.Guild;
                        var top10 = EconomyHelper.GetTop10(guild.Id);

                        // Gera a imagem do rank
                        string path = await EconomyImageHelper.GerarImagemRank(guild, top10);

                        // 3. Envia a mensagem com a formatação idêntica à foto
                        // Usei o emoji de troféu padrão, mas se você tiver um customizado, pode trocar o ID
                        var msg = await channel.SendFileAsync(path,
                            "<a:trofeu:1493063952060387479> **Top Ricos Do Servidor**\n" +
                            "<:whitemoney:1493119805534900346> Confira quem são os membros mais <:coroa:1493119946547396689> **Magnatas** do momento!");

                        // Deleta o arquivo temporário
                        if (File.Exists(path)) File.Delete(path);

                        // 4. Agenda a exclusão para daqui a 5 minutos
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

                // 5. Espera 30 minutos para a próxima execução
                await Task.Delay(TimeSpan.FromMinutes(30));
            }
        }

        // --- SISTEMA DE SORTEIO AUTOMÁTICO ---
        private static async Task LoopSorteio(DiscordSocketClient client)
        {
            // Aguarda o bot conectar
            while (client.ConnectionState != ConnectionState.Connected)
                await Task.Delay(5000);

            while (true)
            {
                try
                {
                    var channel = client.GetChannel(ID_CANAL_RANK) as SocketTextChannel;

                    if (channel != null)
                    {
                        // 1. Apaga o ganhador do sorteio passado (se existir)
                        if (_ultimaMsgSorteio != null)
                        {
                            try { await _ultimaMsgSorteio.DeleteAsync(); } catch { }
                        }

                        // 2. Manda a mensagem de suspense
                        var msgStatus = await channel.SendMessageAsync("✨ **Sorteando...** Vamos ver quem vai ser o sortudo.");

                        // 3. Aguarda 5 segundos para gerar expectativa
                        await Task.Delay(5000);

                        // 4. Pega todos os membros do servidor ignorando os bots
                        var membros = channel.Guild.Users.Where(u => !u.IsBot).ToList();

                        if (membros.Count > 0)
                        {
                            var random = new Random();
                            var ganhador = membros[random.Next(membros.Count)];
                            long valorSorteado = random.Next(50000, 100001); // Entre 50k e 100k

                            // 5. Deposita o prêmio no banco do ganhador
                            EconomyHelper.AdicionarBanco(channel.Guild.Id, ganhador.Id, valorSorteado);

                            // 6. Registra no extrato (ztransacoes)
                            EconomyHelper.RegistrarTransacao(channel.Guild.Id, client.CurrentUser.Id, ganhador.Id, valorSorteado, "DAILY");

                            // 7. Apaga a mensagem de "Sorteando..."
                            try { await msgStatus.DeleteAsync(); } catch { }

                            // 8. Anuncia o ganhador e guarda a mensagem para ser apagada no próximo loop
                            _ultimaMsgSorteio = await channel.SendMessageAsync(
                                $"🎉 **SORTEIO CONCLUÍDO!**\n" +
                                $"• O sortudo da vez foi {ganhador.Mention}!\n" +
                                $"• Acabou de ganhar `{EconomyHelper.FormatarSaldo(valorSorteado)}` coins direto no banco.");
                        }
                        else
                        {
                            try { await msgStatus.DeleteAsync(); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Erro Sorteio]: {ex.Message}");
                }

                // 9. Espera 25 minutos para fazer o próximo sorteio
                await Task.Delay(TimeSpan.FromMinutes(25));
            }
        }
    }
}
