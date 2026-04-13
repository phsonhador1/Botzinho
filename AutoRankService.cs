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
        private const ulong ID_CANAL_RANK = 11111111111111111;

        public static void Iniciar(DiscordSocketClient client)
        {
            // Inicia o loop em uma thread separada para não travar o bot
            _ = Task.Run(() => LoopRank(client));
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
    }
}
