using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Npgsql;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
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
            var indices = Enumerable.Range(0, 16).OrderBy(x => random.Next()).ToList();
            for (int i = 0; i < MinesCount; i++)
            {
                IsMine[indices[i]] = true;
            }
        }

        public double GetNextMultiplier()
        {
            int totalSpots = 16;
            return Multiplier * ((double)(totalSpots - DiamondsFound) / (totalSpots - DiamondsFound - MinesCount));
        }
    }

    // === COMANDOS DO DISCORD ===
    public class EconomyModule : InteractionModuleBase<SocketInteractionContext>
    {
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
            EconomyHelper.AddBalance(Context.User.Id, 1000000);
            await RespondAsync("🎁 **+1M moedas** adicionadas na sua conta para testes!", ephemeral: true);
        }

        [SlashCommand("mines", "Aposte no jogo das minas")]
        public async Task MinesAsync(
            [Summary("aposta", "Valor da aposta")] long aposta,
            [Summary("minas", "Quantidade de minas (1-15)")] int minas = 3)
        {
            if (aposta <= 0) { await RespondAsync("❌ Aposta inválida.", ephemeral: true); return; }
            if (minas < 1 || minas > 15) { await RespondAsync("❌ Escolha entre 1 e 15 minas.", ephemeral: true); return; }

            if (ActiveMines.ContainsKey(Context.User.Id)) { await RespondAsync("⚠️ Termine o jogo anterior primeiro.", ephemeral: true); return; }
            if (!EconomyHelper.RemoveBalance(Context.User.Id, aposta)) { await RespondAsync("💸 Saldo insuficiente.", ephemeral: true); return; }

            var session = new MinesSession(Context.User.Id, aposta, minas);
            ActiveMines[Context.User.Id] = session;

            var (embed, attachment) = await BuildMinesImageEmbed(Context.User, session);
            var components = BuildMinesComponents(session);

            await RespondWithFileAsync(attachment.Stream, attachment.FileName, embed: embed, components: components);
        }

        [ComponentInteraction("mine_click_*")]
        public async Task MineClickAsync(int index)
        {
            var userId = Context.User.Id;
            if (!ActiveMines.TryGetValue(userId, out var session) || session.IsGameOver || session.Revealed[index])
            {
                await RespondAsync("❌ Ação inválida ou jogo já finalizado.", ephemeral: true);
                return;
            }

            session.Revealed[index] = true;

            if (session.IsMine[index])
            {
                session.IsGameOver = true;
                ActiveMines.Remove(userId);
                var (embed, attachment) = await BuildMinesImageEmbed(Context.User, session, exploded: true);
                var components = BuildMinesComponents(session);
                await ((SocketMessageComponent)Context.Interaction).UpdateAsync(x => { x.Embed = embed; x.Components = components; x.Attachments = new List<FileAttachment> { attachment }; });
            }
            else
            {
                session.DiamondsFound++;
                session.Multiplier = session.GetNextMultiplier();

                if (session.DiamondsFound == (16 - session.MinesCount))
                {
                    session.IsGameOver = true;
                    long ganho = (long)(session.BetAmount * session.Multiplier);
                    EconomyHelper.AddBalance(userId, ganho);
                    ActiveMines.Remove(userId);
                    var (embed, attachment) = await BuildMinesImageEmbed(Context.User, session, cashedOut: true, finalWin: ganho);
                    var components = BuildMinesComponents(session);
                    await ((SocketMessageComponent)Context.Interaction).UpdateAsync(x => { x.Embed = embed; x.Components = components; x.Attachments = new List<FileAttachment> { attachment }; });
                }
                else
                {
                    var (embed, attachment) = await BuildMinesImageEmbed(Context.User, session);
                    var components = BuildMinesComponents(session);
                    await ((SocketMessageComponent)Context.Interaction).UpdateAsync(x => { x.Embed = embed; x.Components = components; x.Attachments = new List<FileAttachment> { attachment }; });
                }
            }
        }

        [ComponentInteraction("mine_cashout")]
        public async Task MineCashoutAsync()
        {
            var userId = Context.User.Id;
            if (!ActiveMines.TryGetValue(userId, out var session)) { await RespondAsync("❌ Jogo não encontrado.", ephemeral: true); return; }

            session.IsGameOver = true;
            long ganho = (long)(session.BetAmount * session.Multiplier);
            EconomyHelper.AddBalance(userId, ganho);
            ActiveMines.Remove(userId);

            var (embed, attachment) = await BuildMinesImageEmbed(Context.User, session, cashedOut: true, finalWin: ganho);
            var components = BuildMinesComponents(session);
            await ((SocketMessageComponent)Context.Interaction).UpdateAsync(x => { x.Embed = embed; x.Components = components; x.Attachments = new List<FileAttachment> { attachment }; });
        }

        // --- RENDERIZADOR GRAFICO DO TABULEIRO DO MINES ---
        private async Task<(Embed, FileAttachment)> BuildMinesImageEmbed(IUser user, MinesSession session, bool exploded = false, bool cashedOut = false, long finalWin = 0)
        {
            // 1. Configuração do Canvas (Tamanho 500x500 pixels)
            int imageSize = 500;
            using var surface = SKSurface.Create(new SKImageInfo(imageSize, imageSize));
            var canvas = surface.Canvas;

            // 2. Carregar imagens dos Assets (Certifique-se que o caminho está correto)
            string path = Path.Combine(AppContext.BaseDirectory, "Assets");
            using var imgVazio = SKBitmap.Decode(Path.Combine(path, "vazio.png"));
            using var imgDiamante = SKBitmap.Decode(Path.Combine(path, "diamante.png"));
            using var imgBomba = SKBitmap.Decode(Path.Combine(path, "bomba.png.jpg"));

            if (imgVazio == null || imgDiamante == null || imgBomba == null)
                throw new Exception("❌ Falha ao carregar imagens da pasta Assets. Verifique os arquivos.");

            // 3. Desenhar o Fundo Verde Vibrante Arredondado (idêntico à foto)
            canvas.Clear(SKColors.Transparent);
            var paintBg = new SKPaint { Color = new SKColor(0x2ecc71), IsAntialias = true }; // Verde Vibrante
            int paddingOuter = 20;
            canvas.DrawRoundRect(new SKRect(paddingOuter, paddingOuter, imageSize - paddingOuter, imageSize - paddingOuter), 25, 25, paintBg);

            // 4. Desenhar o Grid 4x4
            int gridPadding = 20;
            int gridStart = paddingOuter + gridPadding;
            int availableSize = imageSize - (gridStart * 2);
            int cellSize = availableSize / 4;
            int innerPadding = 10;

            for (int i = 0; i < 16; i++)
            {
                int row = i / 4;
                int col = i % 4;
                int x = gridStart + (col * cellSize) + innerPadding;
                int y = gridStart + (row * cellSize) + innerPadding;
                int rectSize = cellSize - (innerPadding * 2);

                SKBitmap imagemParaDesenhar;
                // Se o jogo acabou (bomba ou cashout), revela tudo
                bool revelarParaRender = exploded || cashedOut || session.Revealed[i];

                if (!revelarParaRender)
                {
                    imagemParaDesenhar = imgVazio;
                }
                else
                {
                    imagemParaDesenhar = session.IsMine[i] ? imgBomba : imgDiamante;
                }

                canvas.DrawBitmap(imagemParaDesenhar, new SKRect(x, y, x + rectSize, y + rectSize));
            }

            // 5. Salvar em Stream
            var imageStream = new MemoryStream();
            surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100).SaveTo(imageStream);
            imageStream.Position = 0;

            string fileName = $"mines_{Guid.NewGuid()}.png";
            var attachment = new FileAttachment(imageStream, fileName);

            // 6. Montar Embed
            long possivelGanho = (long)(session.BetAmount * session.Multiplier);
            if (session.DiamondsFound == 0) possivelGanho = 0;

            string statusText = "";
            if (exploded) statusText = "\n\n💥 **BOOM! Você pisou numa mina!**";
            if (cashedOut) statusText = $"\n\n✅ **Saque realizado! Ganho final: {FormatNumber(finalWin)}**";

            var embed = new EmbedBuilder()
                .WithAuthor($"Mines | {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithDescription(
                    $"💸 **Aposta:** {FormatNumber(session.BetAmount)}\n" +
                    $"💵 **Possível ganho:** {FormatNumber(cashedOut ? finalWin : possivelGanho)}\n" +
                    $"💣 **Minas:** {session.MinesCount}" +
                    statusText
                )
                .WithImageUrl($"attachment://{fileName}") // Exibe a imagem gerada
                .WithColor(exploded ? Color.Red : (cashedOut ? Color.Green : new Color(0x2ecc71)))
                .WithFooter($"Apostador: {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .Build();

            return (embed, attachment);
        }

        private MessageComponent BuildMinesComponents(MinesSession session)
        {
            var builder = new ComponentBuilder();
            // 4 Linhas para o Grid
            for (int row = 0; row < 4; row++)
            {
                var actionRow = new ActionRowBuilder();
                for (int col = 0; col < 4; col++)
                {
                    int index = row * 4 + col;
                    // Os botões ficam cinzas e vazios pra quem está jogando
                    actionRow.AddComponent(new ButtonBuilder()
                        .WithCustomId($"mine_click_{index}")
                        .WithStyle(ButtonStyle.Secondary)
                        .WithLabel("\u200B") // Caractere invisível
                        .WithDisabled(session.Revealed[index] || session.IsGameOver)
                        .Build());
                }
                builder.AddRow(actionRow);
            }

            // 5ª Linha: Cashout
            long possivelGanho = (long)(session.BetAmount * session.Multiplier);
            var cashoutRow = new ActionRowBuilder();
            cashoutRow.AddComponent(new ButtonBuilder()
                .WithCustomId("mine_cashout")
                .WithLabel(session.IsGameOver ? (session.DiamondsFound == (16 - session.MinesCount) ? $"Ganhou {FormatNumber(possivelGanho)}" : $"Perdeu {FormatNumber(session.BetAmount)}") : $"Retirar {FormatNumber(possivelGanho)}")
                .WithEmote(new Emoji("💸"))
                .WithStyle(session.IsGameOver ? ButtonStyle.Secondary : ButtonStyle.Success)
                .WithDisabled(session.IsGameOver || session.DiamondsFound == 0)
                .Build());
            builder.AddRow(cashoutRow);

            return builder.Build();
        }

        public static string FormatNumber(long num)
        {
            if (num >= 1000000) return (num / 1000000D).ToString("0.#") + "M";
            if (num >= 1000) return (num / 1000D).ToString("0.#") + "K";
            return num.ToString();
        }
    }
}
