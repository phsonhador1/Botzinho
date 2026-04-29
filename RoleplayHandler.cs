using Discord;
using Discord.WebSocket;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Botzinho.Economy;

namespace Botzinho.Roleplay
{
    public class RoleplayHandler
    {
        private readonly DiscordSocketClient _client;

        // Tempo de cooldown: 1 HORA por comando
        private static readonly TimeSpan TempoEspera = TimeSpan.FromHours(1);

        // Cores
        private static readonly Color VerdeSucesso = new Color(67, 181, 129);
        private static readonly Color RoxoZoe = new Color(160, 80, 220);

        // Random global
        private static readonly Random _random = new Random();

        // ★★★ CONFIGURAÇÃO DOS GIFS ★★★
        private const string BASE_URL = "https://cdn.zanybot.cc/gifs_gif";
        private const int QTD_GIFS_BEIJO = 25;
        private const int QTD_GIFS_TAPA = 25;
        private const int QTD_GIFS_ABRACO = 25;

        private static readonly string[] _gifsBeijo = GerarLinks("kiss", QTD_GIFS_BEIJO);
        private static readonly string[] _gifsTapa = GerarLinks("slap", QTD_GIFS_TAPA);
        private static readonly string[] _gifsAbraco = GerarLinks("hug", QTD_GIFS_ABRACO);

        // ★★★ MENSAGENS ALEATÓRIAS ★★★
        // Use {alvo} pra mencionar o usuário e {valor} pra recompensa formatada
        private static readonly string[] _msgsBeijo = new[]
        {
            "<a:sucess:1494692628372132013> **Beijo apaixonado!** Ao beijar {alvo}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Beijo de cinema!** Você roubou um beijo de {alvo} e levou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Que beijo escandaloso!** {alvo} ficou sem reação e você ainda ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Beijo de novela!** Você selou os lábios em {alvo} e foi recompensado com: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Aff, arruma um quarto!** Você beijou {alvo} sem vergonha e ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Romance no ar!** Você deu um selinho em {alvo} e levou pra casa: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Tipo Bonnie & Clyde!** Você beijou {alvo} com paixão e faturou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Coração disparou!** Beijo trocado com {alvo} e bônus de: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Beijo de tirar o fôlego!** {alvo} ficou tonto e você lucrou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Que momento fofo!** Você beijou {alvo} e o universo te deu: <:maiszoe:1494070196871364689> **{valor}**"
        };

        private static readonly string[] _msgsTapa = new[]
        {
            "<a:sucess:1494692628372132013> **Tapa amigável!** Ao dar um tapinha em {alvo}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **PEGOU GERAL!** Você meteu uma na cara de {alvo} e ainda ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Mão pesada!** Você acertou um tapa em {alvo} e levou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Toma tapa!** {alvo} virou alvo da sua mão e você ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **PAFT!** O tapa em {alvo} ecoou pelo servidor e te rendeu: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Tapa educativo!** {alvo} aprendeu a lição e você lucrou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Cinco dedos no rosto!** {alvo} sentiu firme e você ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Tapa ou carinho?** Confundiu {alvo} e ainda ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Mão coçando!** Resolveu em {alvo} e foi premiado com: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Tapa cinematográfico!** {alvo} virou meme e você ganhou: <:maiszoe:1494070196871364689> **{valor}**"
        };

        private static readonly string[] _msgsAbraco = new[]
        {
            "<a:sucess:1494692628372132013> **Abraço fortinho!** Ao abraçar {alvo}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Abraço de urso!** Você apertou {alvo} com carinho e ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Que aconchego!** Abraço gostoso em {alvo} te rendeu: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Calorzinho do coração!** Você abraçou {alvo} e foi recompensado com: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Abraço apertado!** {alvo} sorriu de volta e você ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Que momento fofo!** Abraço com {alvo} e bônus de: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Abraço terapêutico!** Você acolheu {alvo} e levou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Sentiu o amor?** Abraço sincero em {alvo} e você lucrou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Conforto puro!** {alvo} se derreteu nos seus braços e você ganhou: <:maiszoe:1494070196871364689> **{valor}**",
            "<a:sucess:1494692628372132013> **Aquele abraço!** Você grudou em {alvo} e ainda lucrou: <:maiszoe:1494070196871364689> **{valor}**"
        };

        public RoleplayHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;

            InicializarTabela();

            Console.WriteLine($"[Roleplay] Handler INICIALIZADO! {_gifsBeijo.Length} gifs beijo, {_gifsTapa.Length} tapa, {_gifsAbraco.Length} abraço");
        }

        // ============================================================
        // BANCO DE DADOS - Cooldown persistente
        // ============================================================
        private static void InicializarTabela()
        {
            try
            {
                using var conn = new NpgsqlConnection(EconomyHelper.GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS roleplay_cooldowns (
                        guild_id TEXT,
                        user_id TEXT,
                        acao TEXT,
                        usado_em TIMESTAMP,
                        PRIMARY KEY (guild_id, user_id, acao));";
                cmd.ExecuteNonQuery();
                Console.WriteLine("[Roleplay] Tabela 'roleplay_cooldowns' verificada.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roleplay] Erro criando tabela: {ex.Message}");
            }
        }

        private static DateTime? GetCooldown(ulong guildId, ulong userId, string acao)
        {
            using var conn = new NpgsqlConnection(EconomyHelper.GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT usado_em FROM roleplay_cooldowns WHERE guild_id = @gid AND user_id = @uid AND acao = @acao";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@acao", acao);
            var res = cmd.ExecuteScalar();
            if (res != null && res != DBNull.Value) return Convert.ToDateTime(res);
            return null;
        }

        private static void SetCooldown(ulong guildId, ulong userId, string acao)
        {
            using var conn = new NpgsqlConnection(EconomyHelper.GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO roleplay_cooldowns (guild_id, user_id, acao, usado_em) VALUES (@gid, @uid, @acao, @dt)
                                ON CONFLICT (guild_id, user_id, acao) DO UPDATE SET usado_em = @dt";
            cmd.Parameters.AddWithValue("@gid", guildId.ToString());
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@acao", acao);
            cmd.Parameters.AddWithValue("@dt", DateTime.UtcNow);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // HANDLER DE MENSAGENS
        // ============================================================
        private Task HandleMessage(SocketMessage msg)
        {
            _ = Task.Run(async () => {
                try
                {
                    if (msg.Author.IsBot || msg is not SocketUserMessage) return;
                    var user = msg.Author as SocketGuildUser;
                    if (user == null) return;

                    var content = msg.Content.Trim();
                    var contentLower = content.ToLower();

                    // ★ COMANDO ZTEMPO
                    if (contentLower == "ztempo")
                    {
                        await ExecutarZTempo(msg, user);
                        return;
                    }

                    string acao = null;
                    if (contentLower.StartsWith("zbeijar") || contentLower.StartsWith("zbeijo"))
                        acao = "beijar";
                    else if (contentLower.StartsWith("ztapa") || contentLower.StartsWith("zslap"))
                        acao = "tapa";
                    else if (contentLower.StartsWith("zabracar") || contentLower.StartsWith("zabraco") || contentLower.StartsWith("zhug"))
                        acao = "abracar";
                    else
                        return;

                    Console.WriteLine($"[Roleplay] Comando '{content}' por {user.Username} em {user.Guild.Name}");

                    // Cooldown persistente
                    var ultimoUso = GetCooldown(user.Guild.Id, user.Id, acao);
                    if (ultimoUso.HasValue)
                    {
                        var passou = DateTime.UtcNow - ultimoUso.Value;
                        if (passou < TempoEspera)
                        {
                            var falta = TempoEspera - passou;
                            string tempoFormatado = FormatarTempo(falta);

                            var aviso = await msg.Channel.SendMessageAsync(
                                $"<:erro:1493078898462949526> {user.Mention}, Ihhhhh calma ae Puta! você já usou **z{acao}** recentemente! " +
                                $"Aguarde mais **{tempoFormatado}** pra usar de novo."
                            );
                            _ = Task.Delay(8000).ContinueWith(_ => aviso.DeleteAsync());
                            return;
                        }
                    }

                    var mencionado = msg.MentionedUsers.FirstOrDefault() as SocketGuildUser;
                    if (mencionado == null)
                    {
                        await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, mencione alguém! Exemplo: **z{acao} @putinhadoserver**");
                        return;
                    }

                    if (mencionado.Id == user.Id)
                    {
                        await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> Hahaha ta de brincadeira ne viadinho?{user.Mention}, você não pode fazer isso consigo mesmo!");
                        return;
                    }

                    if (mencionado.IsBot)
                    {
                        await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention} Deixa de ser animal Filho da Puta! Bots não podem participar disso.");
                        return;
                    }

                    SetCooldown(user.Guild.Id, user.Id, acao);

                    await ExecutarAcao(msg, user, mencionado, acao);
                }
                catch (Exception ex) { Console.WriteLine($"[Roleplay Error]: {ex.Message}\n{ex.StackTrace}"); }
            });
            return Task.CompletedTask;
        }

        // ============================================================
        // COMANDO ZTEMPO - Mostra cooldown de cada comando
        // ============================================================
        private async Task ExecutarZTempo(SocketMessage msg, SocketGuildUser user)
        {
            string descricao = $"<a:carregandoportal:1492944498605686844> **Segue o Tempo restante dos **comandos** de roleplay** \n\n";

            string[] acoes = { "beijar", "tapa", "abracar" };
            string[] nomesComando = { "zbeijar", "ztapa", "zabracar" };

            for (int i = 0; i < acoes.Length; i++)
            {
                var ultimoUso = GetCooldown(user.Guild.Id, user.Id, acoes[i]);

                if (!ultimoUso.HasValue)
                {
                    descricao += $" **{nomesComando[i]}** →  <a:sucess:1494692628372132013>  **Disponível para uso!**\n";
                }
                else
                {
                    var passou = DateTime.UtcNow - ultimoUso.Value;
                    if (passou >= TempoEspera)
                    {
                        descricao += $" **{nomesComando[i]}** →  <a:sucess:1494692628372132013>  **Disponível para uso!** \n";
                    }
                    else
                    {
                        var falta = TempoEspera - passou;
                        descricao += $" **{nomesComando[i]}** →  <:erro:1493078898462949526>  **{FormatarTempo(falta)}**\n";
                    }
                }
            }

            var embed = new EmbedBuilder()
                .WithColor(RoxoZoe)
                .WithDescription(descricao)
                .WithFooter($"Comandos zerados a cada 1 hora • {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .Build();

            await msg.Channel.SendMessageAsync(embed: embed);
        }

        // ============================================================
        // EXECUTA AÇÃO (beijar/tapa/abracar)
        // ============================================================
        private async Task ExecutarAcao(SocketMessage msg, SocketGuildUser user, SocketGuildUser alvo, string acao)
        {
            string gifUrl = SortearGif(acao);
            long recompensa = _random.NextInt64(50_000, 250_999);
            //                                  

            try
            {
                EconomyHelper.AdicionarSaldo(user.Guild.Id, user.Id, recompensa);
                EconomyHelper.RegistrarTransacao(user.Guild.Id, _client.CurrentUser.Id, user.Id, recompensa, $"ROLEPLAY_{acao.ToUpper()}");
                Console.WriteLine($"[Roleplay] {user.Username} ganhou {recompensa} fazendo '{acao}' em {alvo.Username} | Gif: {gifUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roleplay AdicionarSaldo Error]: {ex.Message}");
                await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> Erro ao adicionar saldo: `{ex.Message}`");
                return;
            }

            // ★ Sorteia mensagem aleatória
            string textoMsg = SortearMensagem(acao)
                .Replace("{alvo}", alvo.Mention)
                .Replace("{valor}", EconomyHelper.FormatarSaldo(recompensa));

            try
            {
                if (!string.IsNullOrWhiteSpace(gifUrl))
                {
                    var embed = new EmbedBuilder()
                        .WithColor(VerdeSucesso)
                        .WithImageUrl(gifUrl)
                        .Build();

                    await msg.Channel.SendMessageAsync(text: textoMsg, embed: embed);
                }
                else
                {
                    await msg.Channel.SendMessageAsync(text: textoMsg);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roleplay SendMessage Error]: {ex.Message}");
                try { await msg.Channel.SendMessageAsync(text: textoMsg); } catch { }
            }
        }

        // ============================================================
        // SORTEIOS
        // ============================================================
        private static string SortearGif(string acao)
        {
            string[] lista = acao switch
            {
                "beijar" => _gifsBeijo,
                "tapa" => _gifsTapa,
                "abracar" => _gifsAbraco,
                _ => null
            };

            if (lista == null || lista.Length == 0) return null;
            return lista[_random.Next(lista.Length)];
        }

        private static string SortearMensagem(string acao)
        {
            string[] lista = acao switch
            {
                "beijar" => _msgsBeijo,
                "tapa" => _msgsTapa,
                "abracar" => _msgsAbraco,
                _ => null
            };

            if (lista == null || lista.Length == 0) return "";
            return lista[_random.Next(lista.Length)];
        }

        private static string[] GerarLinks(string nome, int qtd)
        {
            var lista = new string[qtd];
            for (int i = 0; i < qtd; i++)
                lista[i] = $"{BASE_URL}/{nome}/{nome}_{i + 1}.gif";
            return lista;
        }

        private string FormatarTempo(TimeSpan t)
        {
            if (t.TotalMinutes < 1)
                return $"{(int)t.TotalSeconds}s";
            if (t.TotalHours < 1)
                return $"{t.Minutes}min e {t.Seconds}s";
            return $"{(int)t.TotalHours}h e {t.Minutes}min";
        }
    }
}
