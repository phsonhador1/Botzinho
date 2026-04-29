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

        // Cor verde estilo "sucesso"
        private static readonly Color VerdeSucesso = new Color(67, 181, 129);

        // Random global pro sorteio dos gifs
        private static readonly Random _random = new Random();

        // ★★★ BIBLIOTECA DE GIFS - URLs DIRETAS DO DISCORD CDN ★★★
        private static readonly string[] _gifsBeijo = new[]
        {
            "https://cdn.discordapp.com/attachments/1496243404114235554/1498852930756018217/image0.gif"
        };

        private static readonly string[] _gifsTapa = new string[]
        {
            // TODO: adicionar URLs do Discord CDN
        };

        private static readonly string[] _gifsAbraco = new string[]
        {
            // TODO: adicionar URLs do Discord CDN
        };

        public RoleplayHandler(DiscordSocketClient client)
        {
            _client = client;
            _client.MessageReceived += HandleMessage;

            // Cria tabela de cooldown se não existir
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

        // Retorna a última vez que o user usou esse comando NESSE servidor (ou null se nunca)
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

        // Salva a hora atual como último uso desse comando (UPSERT)
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

                    // ★ COOLDOWN PERSISTENTE NO BANCO (por servidor + user + ação)
                    var ultimoUso = GetCooldown(user.Guild.Id, user.Id, acao);
                    if (ultimoUso.HasValue)
                    {
                        var passou = DateTime.UtcNow - ultimoUso.Value;
                        if (passou < TempoEspera)
                        {
                            var falta = TempoEspera - passou;
                            string tempoFormatado = FormatarTempo(falta);

                            var aviso = await msg.Channel.SendMessageAsync(
                                $"<:erro:1493078898462949526> {user.Mention}, você já usou **z{acao}** recentemente! " +
                                $"Aguarde mais **{tempoFormatado}** pra usar de novo."
                            );
                            _ = Task.Delay(8000).ContinueWith(_ => aviso.DeleteAsync());
                            return;
                        }
                    }

                    // Pega o mencionado
                    var mencionado = msg.MentionedUsers.FirstOrDefault() as SocketGuildUser;
                    if (mencionado == null)
                    {
                        await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, mencione alguém! Exemplo: `z{acao} @fulano`");
                        return;
                    }

                    if (mencionado.Id == user.Id)
                    {
                        await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, você não pode fazer isso consigo mesmo!");
                        return;
                    }

                    if (mencionado.IsBot)
                    {
                        await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, não posso participar disso, sou apenas um bot!");
                        return;
                    }

                    // ★ Salva o cooldown no banco DEPOIS de validar tudo
                    SetCooldown(user.Guild.Id, user.Id, acao);

                    await ExecutarAcao(msg, user, mencionado, acao);
                }
                catch (Exception ex) { Console.WriteLine($"[Roleplay Error]: {ex.Message}\n{ex.StackTrace}"); }
            });
            return Task.CompletedTask;
        }

        private async Task ExecutarAcao(SocketMessage msg, SocketGuildUser user, SocketGuildUser alvo, string acao)
        {
            string gifUrl = SortearGif(acao);
            long recompensa = _random.NextInt64(50_000, 500_001);

            // Adiciona o saldo
            try
            {
                EconomyHelper.AdicionarSaldo(user.Guild.Id, user.Id, recompensa);
                EconomyHelper.RegistrarTransacao(user.Guild.Id, _client.CurrentUser.Id, user.Id, recompensa, $"ROLEPLAY_{acao.ToUpper()}");
                Console.WriteLine($"[Roleplay] {user.Username} ganhou {recompensa} fazendo '{acao}' em {alvo.Username}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Roleplay AdicionarSaldo Error]: {ex.Message}");
                await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> Erro ao adicionar saldo: `{ex.Message}`");
                return;
            }

            string textoMsg = acao switch
            {
                "beijar" => $"<a:sucess:1494692628372132013> **Beijo apaixonado!** Ao beijar {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                "tapa" => $"<a:sucess:1494692628372132013> **Tapa amigável!** Ao dar um tapinha em {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                "abracar" => $"<a:sucess:1494692628372132013> **Abraço fortinho!** Ao abraçar {alvo.Mention}, você recebeu carinho em dobro e ganhou: <:maiszoe:1494070196871364689> **{EconomyHelper.FormatarSaldo(recompensa)}**",
                _ => ""
            };

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

        private string SortearGif(string acao)
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
