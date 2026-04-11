using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Botzinho.Cassino
{
    // === HELPER DO BANCO DE DADOS DA ECONOMIA ===
    public static class EconomyHelper
    {
        private static string GetConnectionString() => Environment.GetEnvironmentVariable("DATABASE_URL");

        public static void InicializarTabelas()
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS economy (
                    user_id TEXT PRIMARY KEY,
                    balance BIGINT DEFAULT 0
                );
            ";
            cmd.ExecuteNonQuery();
        }

        public static long GetBalance(ulong userId)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT balance FROM economy WHERE user_id = @uid";
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            var result = cmd.ExecuteScalar();
            return result == null ? 0 : Convert.ToInt64(result);
        }

        public static void AddBalance(ulong userId, long amount)
        {
            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO economy (user_id, balance) VALUES (@uid, @amt)
                ON CONFLICT (user_id) DO UPDATE SET balance = economy.balance + @amt;
            ";
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@amt", amount);
            cmd.ExecuteNonQuery();
        }

        public static bool RemoveBalance(ulong userId, long amount)
        {
            var current = GetBalance(userId);
            if (current < amount) return false;

            using var conn = new NpgsqlConnection(GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE economy SET balance = balance - @amt WHERE user_id = @uid";
            cmd.Parameters.AddWithValue("@uid", userId.ToString());
            cmd.Parameters.AddWithValue("@amt", amount);
            cmd.ExecuteNonQuery();
            return true;
        }
    }

    // === LÓGICA DO JOGO MINES ===
    public class MinesSession
    {
        public ulong UserId { get; set; }
        public long BetAmount { get; set; }
        public int MinesCount { get; set; }
        public bool[] IsMine { get; set; } = new bool[16];
        public bool[] Revealed { get; set; } = new bool[16];
        public int DiamondsFound { get; set; } = 0;
        public double Multiplier { get; set; } = 1.0;
        public bool IsGameOver { get; set; } = false;

        public MinesSession(ulong userId, long bet, int mines)
        {
            UserId = userId;
            BetAmount = bet;
            MinesCount = mines;
            GenerateBoard();
        }

        private void GenerateBoard()
        {
            var random = new Random();
            // Embaralha 16 posições e pega as primeiras para serem as bombas
            var indices = Enumerable.Range(0, 16).OrderBy(x => random.Next()).ToList();
            for (int i = 0; i < MinesCount; i++)
            {
                IsMine[indices[i]] = true;
            }
        }

        // Calcula o próximo multiplicador baseado na probabilidade real
        public double GetNextMultiplier()
        {
            int totalSpots = 16;
            return Multiplier * ((double)(totalSpots - DiamondsFound) / (totalSpots - DiamondsFound - MinesCount));
        }
    }

    // === COMANDOS DO DISCORD ===
    public class EconomyModule : InteractionModuleBase<SocketInteractionContext>
    {
        // Memória temporária para guardar os jogos em andamento
        public static Dictionary<ulong, MinesSession> ActiveMines = new();

        [SlashCommand("saldo", "Veja o seu saldo ou o de outra pessoa")]
        public async Task SaldoAsync([Summary("usuario", "Usuário para ver o saldo")] SocketGuildUser alvo = null)
        {
            var user = alvo ?? (SocketGuildUser)Context.User;
            var balance = EconomyHelper.GetBalance(user.Id);
            await RespondAsync($"💳 O saldo de {user.Mention} é de **{FormatNumber(balance)}** moedas.");
        }

        [SlashCommand("daily", "Pegue suas moedas diárias para apostar!")]
        public async Task DailyAsync()
        {
            // Sistema simples pra você ter dinheiro pra testar!
            EconomyHelper.AddBalance(Context.User.Id, 500000); // Dá 500K 
            await RespondAsync("🎁 Você resgatou seu prêmio de testes! **+500K moedas** adicionadas na sua conta.", ephemeral: true);
        }

        [SlashCommand("mines", "Aposte no jogo das minas")]
        public async Task MinesAsync(
            [Summary("aposta", "Valor da aposta")] long aposta,
            [Summary("minas", "Quantidade de minas (1-15)")] int minas = 3)
        {
            if (aposta <= 0) { await RespondAsync("❌ Aposta deve ser maior que 0.", ephemeral: true); return; }
            if (minas < 1 || minas > 15) { await RespondAsync("❌ Escolha entre 1 e 15 minas.", ephemeral: true); return; }

            if (ActiveMines.ContainsKey(Context.User.Id))
            {
                await RespondAsync("⚠️ Você já tem um jogo de Mines em andamento! Termine-o primeiro.", ephemeral: true);
                return;
            }

            if (!EconomyHelper.RemoveBalance(Context.User.Id, aposta))
            {
                await RespondAsync("💸 Você não tem saldo suficiente para essa aposta.", ephemeral: true);
                return;
            }

            var session = new MinesSession(Context.User.Id, aposta, minas);
            ActiveMines[Context.User.Id] = session;

            var embed = BuildMinesEmbed(Context.User, session);
            var components = BuildMinesComponents(session);

            await RespondAsync(embed: embed, components: components);
        }

        [ComponentInteraction("mine_click_*")]
        public async Task MineClickAsync(int index)
        {
            var userId = Context.User.Id;

            // Verifica se o jogo existe e pertence a quem clicou
            if (!ActiveMines.TryGetValue(userId, out var session))
            {
                await RespondAsync("❌ Este jogo não existe mais ou pertence a outra pessoa.", ephemeral: true);
                return;
            }

            if (session.IsGameOver) return;
            if (session.Revealed[index]) return;

            session.Revealed[index] = true;

            if (session.IsMine[index])
            {
                // BOOM! GAME OVER
                session.IsGameOver = true;
                ActiveMines.Remove(userId);

                var embed = BuildMinesEmbed(Context.User, session, exploded: true);
                var components = BuildMinesComponents(session, revealAll: true);

                await ((SocketMessageComponent)Context.Interaction).UpdateAsync(x => { x.Embed = embed; x.Components = components; });
            }
            else
            {
                // SEGURO!
                session.DiamondsFound++;
                session.Multiplier = session.GetNextMultiplier();

                // Verifica se o cara ganhou automaticamente (abriu todos os seguros)
                if (session.DiamondsFound == (16 - session.MinesCount))
                {
                    session.IsGameOver = true;
                    long ganho = (long)(session.BetAmount * session.Multiplier);
                    EconomyHelper.AddBalance(userId, ganho);
                    ActiveMines.Remove(userId);

                    var embed = BuildMinesEmbed(Context.User, session, cashedOut: true, finalWin: ganho);
                    var components = BuildMinesComponents(session, revealAll: true);

                    await ((SocketMessageComponent)Context.Interaction).UpdateAsync(x => { x.Embed = embed; x.Components = components; });
                }
                else
                {
                    // Continua o jogo atualizando a interface
                    var embed = BuildMinesEmbed(Context.User, session);
                    var components = BuildMinesComponents(session);

                    await ((SocketMessageComponent)Context.Interaction).UpdateAsync(x => { x.Embed = embed; x.Components = components; });
                }
            }
        }

        [ComponentInteraction("mine_cashout")]
        public async Task MineCashoutAsync()
        {
            var userId = Context.User.Id;
            if (!ActiveMines.TryGetValue(userId, out var session))
            {
                await RespondAsync("❌ Este jogo não existe mais ou pertence a outra pessoa.", ephemeral: true);
                return;
            }

            session.IsGameOver = true;
            long ganho = (long)(session.BetAmount * session.Multiplier);
            EconomyHelper.AddBalance(userId, ganho); // Entrega o premio!
            ActiveMines.Remove(userId);

            var embed = BuildMinesEmbed(Context.User, session, cashedOut: true, finalWin: ganho);
            var components = BuildMinesComponents(session, revealAll: true);

            await ((SocketMessageComponent)Context.Interaction).UpdateAsync(x => { x.Embed = embed; x.Components = components; });
        }

        // --- MÉTODOS VISUAIS PARA CRIAR O EMBED E OS BOTÕES ---
        private Embed BuildMinesEmbed(IUser user, MinesSession session, bool exploded = false, bool cashedOut = false, long finalWin = 0)
        {
            long possivelGanho = (long)(session.BetAmount * session.Multiplier);
            if (session.DiamondsFound == 0) possivelGanho = 0;

            string statusText = "";
            if (exploded) statusText = "\n\n💥 **BOOM! Você pisou numa mina e perdeu tudo!**";
            if (cashedOut) statusText = $"\n\n✅ **Saque realizado com sucesso! Ganho final: {FormatNumber(finalWin)}**";

            return new EmbedBuilder()
                .WithAuthor($"Mines | {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithDescription(
                    $"💸 **Aposta:** {FormatNumber(session.BetAmount)}\n" +
                    $"💵 **Possível ganho:** {FormatNumber(cashedOut ? finalWin : possivelGanho)}\n" +
                    $"💣 **Minas:** {session.MinesCount}" +
                    statusText
                )
                .WithColor(exploded ? Color.Red : (cashedOut ? Color.Green : new Color(0x9B59B6))) // Cor roxa igual da foto
                .WithFooter($"Apostador: {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .Build();
        }

        private MessageComponent BuildMinesComponents(MinesSession session, bool revealAll = false)
        {
            var builder = new ComponentBuilder();

            // 4 Linhas para formar o Grid 4x4
            for (int row = 0; row < 4; row++)
            {
                var actionRow = new ActionRowBuilder();
                for (int col = 0; col < 4; col++)
                {
                    int index = row * 4 + col;
                    bool revealed = session.Revealed[index] || revealAll;
                    bool isMine = session.IsMine[index];

                    var button = new ButtonBuilder()
                        .WithCustomId($"mine_click_{index}")
                        .WithDisabled(revealAll || session.Revealed[index]);

                    if (!revealed)
                    {
                        button.WithStyle(ButtonStyle.Secondary);
                        button.WithLabel("\u200B"); // Caractere invisível para deixar o botão esticadinho
                    }
                    else
                    {
                        if (isMine)
                        {
                            button.WithStyle(ButtonStyle.Danger).WithEmote(new Emoji("💣"));
                        }
                        else
                        {
                            button.WithStyle(ButtonStyle.Success).WithEmote(new Emoji("💎"));
                        }
                    }

                    actionRow.AddComponent(button.Build());
                }
                builder.AddRow(actionRow);
            }

            // 5ª Linha: Botão de Cashout
            var cashoutRow = new ActionRowBuilder();
            long possivelGanho = (long)(session.BetAmount * session.Multiplier);

            cashoutRow.AddComponent(new ButtonBuilder()
                .WithCustomId("mine_cashout")
                .WithLabel($"Retirar {FormatNumber(possivelGanho)}")
                
                .WithStyle(ButtonStyle.Success)
                .WithDisabled(revealAll || session.DiamondsFound == 0) // Só retira se achou 1 diamante no minimo
                .Build());

            builder.AddRow(cashoutRow);
            return builder.Build();
        }

        // Formata os números igualzinho a foto (138K, 1M, etc)
        public static string FormatNumber(long num)
        {
            if (num >= 1000000) return (num / 1000000D).ToString("0.#") + "M";
            if (num >= 1000) return (num / 1000D).ToString("0.#") + "K";
            return num.ToString();
        }
    }
}