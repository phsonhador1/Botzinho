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
        private const ulong ID_CANAL_RANK = 1487905632261505024;

        public static void Iniciar(DiscordSocketClient client)
        {
            _ = Task.Run(() => LoopRank(client));
            _ = Task.Run(() => LoopSorteio(client));
        }

        private static async Task LoopRank(DiscordSocketClient client)
        {
            while (client.ConnectionState != ConnectionState.Connected)
                await Task.Delay(5000);

            // Atraso de inicialização para evitar spam no redeploy
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

        // --- SISTEMA DE SORTEIO AUTOMÁTICO (VERSÃO PROFISSIONAL) ---
        private static async Task LoopSorteio(DiscordSocketClient client)
        {
            while (client.ConnectionState != ConnectionState.Connected)
                await Task.Delay(5000);

            // Atraso de 10 minutos após o bot ligar para não disparar instantaneamente
            await Task.Delay(TimeSpan.FromMinutes(10));

            while (true)
            {
                try
                {
                    var channel = client.GetChannel(ID_CANAL_RANK) as SocketTextChannel;

                    if (channel != null)
                    {
                        // 1. LIMPEZA PROFISSIONAL: Lê as últimas 30 mensagens do canal
                        var mensagensAntigas = await channel.GetMessagesAsync(30).FlattenAsync();

                        // 2. Filtra as mensagens antigas de sorteio da própria Zoe
                        var lixoParaApagar = mensagensAntigas.Where(m =>
                            m.Author.Id == client.CurrentUser.Id &&
                            (m.Content.Contains("SORTEIO CONCLUÍDO!") || m.Content.Contains("✨ **Sorteando...**"))
                        );

                        // 3. Apaga qualquer resquício de sorteios passados (sobrevive a redeploys)
                        foreach (var msgAntiga in lixoParaApagar)
                        {
                            try { await msgAntiga.DeleteAsync(); } catch { }
                        }

                        // 4. Inicia o novo sorteio
                        var msgStatus = await channel.SendMessageAsync(" **Sorteando...** Vamos ver quem vai ser o sortudo.");
                        await Task.Delay(5000); // Suspense

                        var listaUsuarios = await channel.Guild.GetUsersAsync().FlattenAsync();
                        var membros = listaUsuarios.Where(u => !u.IsBot).ToList();

                        if (membros.Count > 0)
                        {
                            var random = new Random();
                            var ganhador = membros[random.Next(membros.Count)];
                            long valorSorteado = random.Next(50000, 100001);

                            EconomyHelper.AdicionarBanco(channel.Guild.Id, ganhador.Id, valorSorteado);
                            EconomyHelper.RegistrarTransacao(channel.Guild.Id, client.CurrentUser.Id, ganhador.Id, valorSorteado, "SORTEIO_AUTO");

                            try { await msgStatus.DeleteAsync(); } catch { }

                            // Manda a mensagem nova que ficará fixa até o próximo loop
                            await channel.SendMessageAsync($"<a:ganhador:1493088070923452599> O magnata sortudo desta vez foi: <@{ganhador.Id}>, ganhou `{EconomyHelper.FormatarSaldo(valorSorteado)}` direto no banco!");
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

                // 5. Aguarda 25 minutos
                await Task.Delay(TimeSpan.FromMinutes(15));
            }
        }
    }
}
