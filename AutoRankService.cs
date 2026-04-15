using Discord;
using Discord.WebSocket;
using Botzinho.Economy;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Core
{
    public static class AutoRankService
    {
        // Variáveis de tempo públicas para o Zpainel ler silenciosamente
        public static long UnixProximoSorteio = 0;
        public static long UnixProximoRank = 0;

        // Guarda apenas o último ganhador para evitar repetição seguida
        private static ulong _ultimoGanhador = 0;

        // Sua Whitelist
        public static readonly List<ulong> IdsPermitidos = new()
        {
            1431655151105474755, 1472642376970404002, 1437491644286107838,
            1469449943390617714, 1187711938805907527, 877026652167753761,
            1489775731667107883, 1465039524508864848, 1491088346909249697,
            1445779233052823604, 1469787135723831480
        };

        // ⚠️ CANAL PÚBLICO DO SERVIDOR (Para o Sorteio e Rank)
        public const ulong ID_CANAL_RANK = 148790563226150;

        // ⚠️ CANAL PRIVADO (SEU SERVIDOR DE LOGS)
        public const ulong ID_CANAL_LOGS = 1492995092166869002;

        public static void Iniciar(DiscordSocketClient client)
        {
            _ = Task.Run(() => LoopRank(client));
            _ = Task.Run(() => LoopSorteio(client));
        }

        private static async Task LoopRank(DiscordSocketClient client)
        {
            while (client.ConnectionState != ConnectionState.Connected)
                await Task.Delay(5000);

            UnixProximoRank = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
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

                UnixProximoRank = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
                await Task.Delay(TimeSpan.FromMinutes(30));
            }
        }

        private static async Task LoopSorteio(DiscordSocketClient client)
        {
            while (client.ConnectionState != ConnectionState.Connected)
                await Task.Delay(5000);

            // Envia o log privado para você saber que ligou
            await EnviarLogPrivado(client, "🟢 **Monitoramento Ativo:** Primeiro sorteio em 10 minutos.");

            // Apenas define a hora nos bastidores para o zpainel, SEM mandar mensagem no público
            UnixProximoSorteio = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
            await Task.Delay(TimeSpan.FromMinutes(10));

            while (true)
            {
                try
                {
                    var channel = client.GetChannel(ID_CANAL_RANK) as SocketTextChannel;

                    if (channel != null)
                    {
                        var mensagensAntigas = await channel.GetMessagesAsync(30).FlattenAsync();

                        // Limpa lixos antigos (caso ainda tenha sobrado algum contador de antes)
                        var lixoParaApagar = mensagensAntigas.Where(m =>
                            m.Author.Id == client.CurrentUser.Id &&
                            (m.Content.Contains("O magnata sortudo") ||
                             m.Content.Contains("Sorteando...") ||
                             m.Content.Contains("O próximo sorteio acontecerá") ||
                             m.Content.Contains("Monitoramento iniciado!"))
                        );

                        foreach (var msgAntiga in lixoParaApagar)
                        {
                            try { await msgAntiga.DeleteAsync(); } catch { }
                        }

                        // Animação de Sorteio
                        var msgStatus = await channel.SendMessageAsync("<a:carregandoportal:1492944498605686844> **Sorteando...** Vamos ver quem vai ser o sortudo.");
                        await Task.Delay(5000);

                        var listaUsuarios = await channel.Guild.GetUsersAsync().FlattenAsync();
                        var membros = listaUsuarios.Where(u => !u.IsBot && IdsPermitidos.Contains(u.Id)).ToList();

                        // --- EVITA REPETIÇÃO DO ÚLTIMO GANHADOR ---
                        if (membros.Count > 1 && _ultimoGanhador != 0)
                        {
                            membros.RemoveAll(u => u.Id == _ultimoGanhador);
                        }

                        if (membros.Count > 0)
                        {
                            var random = new Random();
                            var ganhador = membros[random.Next(membros.Count)];

                            // Atualiza a variável para o bot lembrar quem foi o sortudo dessa vez
                            _ultimoGanhador = ganhador.Id;

                            // ALTERADO: O prêmio agora é entre 10k (10000) e 25k (25000)
                            long valorSorteado = random.Next(10000, 23000);

                            EconomyHelper.AdicionarBanco(channel.Guild.Id, ganhador.Id, valorSorteado);
                            EconomyHelper.RegistrarTransacao(channel.Guild.Id, client.CurrentUser.Id, ganhador.Id, valorSorteado, "SORTEIO_AUTO");

                            try { await msgStatus.DeleteAsync(); } catch { }

                            // Anúncio do Ganhador
                            await channel.SendMessageAsync($"<a:ganhador:1493088070923452599> O magnata sortudo desta vez foi: <@{ganhador.Id}>, ganhou <:mais:1493267829611303023> `{EconomyHelper.FormatarSaldo(valorSorteado)}` direto no banco!");

                            // ALTERADO: Atualiza a variável silenciosamente para o zpainel (10 minutos)
                            UnixProximoSorteio = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();

                            // Loga no privado
                            var proximo = DateTime.Now.AddMinutes(10);
                            await EnviarLogPrivado(client, $"✅ **Sorteio Realizado!**\n🏆 Ganhador: <@{ganhador.Id}>\n⏰ Próximo sorteio às: **{proximo:HH:mm:ss}**");
                        }
                        else
                        {
                            try { await msgStatus.DeleteAsync(); } catch { }
                            await EnviarLogPrivado(client, "⚠️ **Aviso:** Nenhum ID da whitelist encontrado para o sorteio.");

                            // ALTERADO: Mantém o ciclo vivo silenciosamente (10 minutos)
                            UnixProximoSorteio = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Erro Sorteio]: {ex.Message}");
                }

                // ALTERADO: O delay do loop principal agora é de 10 minutos
                await Task.Delay(TimeSpan.FromMinutes(10));
            }
        }

        private static async Task EnviarLogPrivado(DiscordSocketClient client, string texto)
        {
            try
            {
                if (ID_CANAL_LOGS == ID_CANAL_RANK) return;

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
