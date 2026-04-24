using Discord;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Botzinho.Music
{
    public class MusicHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IAudioService _audioService;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();

        // ★ Tema dark estilo Spotify (preto profundo, quase invisível na borda)
        private static readonly Color SpotifyDark = new Color(24, 24, 24);

        public MusicHandler(DiscordSocketClient client, IAudioService audioService)
        {
            _client = client;
            _audioService = audioService;
            _client.MessageReceived += HandleMessage;

            _ = Task.Run(() => VigilanteCallVazia());
        }

        private async Task VigilanteCallVazia()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(45));

                    foreach (var guild in _client.Guilds)
                    {
                        var botUser = guild.CurrentUser;
                        var voiceChannel = botUser?.VoiceChannel;
                        if (voiceChannel == null) continue;

                        int humanos = voiceChannel.ConnectedUsers.Count(u => !u.IsBot);

                        if (humanos == 0)
                        {
                            var player = await _audioService.Players
                                .GetPlayerAsync<QueuedLavalinkPlayer>(guild.Id);

                            if (player != null)
                            {
                                await player.DisconnectAsync();
                                await player.DisposeAsync();
                                Console.WriteLine($"[Music] Saí da call vazia em {guild.Name}");
                            }
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[VigilanteCall Error]: {ex.Message}"); }
            }
        }

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

                    string[] cmds = { "zplay", "zskip", "zqueue", "zfila", "zpause", "zpausar", "zresume", "zresumir", "zstop", "zparar", "znp", "ztocando" };
                    if (!cmds.Any(c => contentLower.StartsWith(c))) return;

                    if (_cooldowns.TryGetValue(user.Id, out var last) && (DateTime.UtcNow - last).TotalSeconds < 2)
                    {
                        var aviso = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> {user.Mention}, calma! Aguarde **2 segundos** entre comandos.");
                        _ = Task.Delay(2000).ContinueWith(_ => aviso.DeleteAsync());
                        return;
                    }
                    _cooldowns[user.Id] = DateTime.UtcNow;

                    if (contentLower.StartsWith("zplay"))
                        await ExecutarPlay(msg, user, content);
                    else if (contentLower == "zskip" || contentLower == "zpular")
                        await ExecutarSkip(msg, user);
                    else if (contentLower == "zqueue" || contentLower == "zfila")
                        await ExecutarQueue(msg, user);
                    else if (contentLower == "zpause" || contentLower == "zpausar")
                        await ExecutarPause(msg, user);
                    else if (contentLower == "zresume" || contentLower == "zresumir")
                        await ExecutarResume(msg, user);
                    else if (contentLower == "zstop" || contentLower == "zparar")
                        await ExecutarStop(msg, user);
                    else if (contentLower == "znp" || contentLower == "ztocando")
                        await ExecutarNowPlaying(msg, user);
                }
                catch (Exception ex) { Console.WriteLine($"[Music HandleMessage Error]: {ex.Message}\n{ex.StackTrace}"); }
            });
            return Task.CompletedTask;
        }

        private async Task ExecutarPlay(SocketMessage msg, SocketGuildUser user, string content)
        {
            var partes = content.Split(' ', 2);
            if (partes.Length < 2 || string.IsNullOrWhiteSpace(partes[1]))
            {
                await msg.Channel.SendMessageAsync("❓ **Uso:** `zplay <nome da música ou link>`\n*Exemplo: `zplay 300 no 7`*");
                return;
            }

            string query = partes[1].Trim();

            var voiceChannel = user.VoiceChannel;
            if (voiceChannel == null)
            {
                await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> Deixa de ser burro {user.Mention}, entra numa call de voz primeiro!");
                return;
            }

            var botGuildPerms = user.Guild.CurrentUser.GetPermissions(voiceChannel);
            if (!botGuildPerms.Connect || !botGuildPerms.Speak)
            {
                await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> Eu não tenho permissão de **Conectar** ou **Falar** no canal `{voiceChannel.Name}`.");
                return;
            }

            var loading = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> Procurando esta musica **{query}**...");

            var player = await ObterPlayerAsync(user.Guild.Id, voiceChannel.Id, conectar: true);
            if (player == null)
            {
                await loading.ModifyAsync(m => m.Content = $"<:erro:1493078898462949526> Falha ao conectar. Verifica se o Lavalink está online e se `LAVALINK_HOST`/`LAVALINK_PASSWORD` estão certos.");
                return;
            }

            Console.WriteLine($"[Music DEBUG] Player obtido. Volume={player.Volume}, State={player.State}, VoiceChannel={voiceChannel.Name}");

            TrackLoadResult result;
            bool isUrl = Uri.TryCreate(query, UriKind.Absolute, out var uri)
                         && (uri.Scheme == "http" || uri.Scheme == "https");

            try
            {
                if (isUrl)
                {
                    result = await _audioService.Tracks.LoadTracksAsync(query, TrackSearchMode.None);
                }
                else
                {
                    Console.WriteLine($"[Music] Buscando no SoundCloud: {query}");
                    result = await _audioService.Tracks.LoadTracksAsync(query, TrackSearchMode.SoundCloud);

                    if (!result.HasMatches)
                    {
                        Console.WriteLine("[Music] SoundCloud sem resultado, tentando YouTube...");
                        result = await _audioService.Tracks.LoadTracksAsync(query, TrackSearchMode.YouTube);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LoadTracks Error]: {ex.Message}");
                await loading.ModifyAsync(m => m.Content = $"<:erro:1493078898462949526> Erro ao buscar música: `{ex.Message}`");
                return;
            }

            if (!result.HasMatches)
            {
                await loading.ModifyAsync(m => m.Content = $"<:erro:1493078898462949526> Nenhum resultado encontrado para **{query}**.");
                return;
            }

            // Playlist
            if (result.IsPlaylist && result.Playlist != null && result.Tracks.Length > 1)
            {
                int qtd = result.Tracks.Length;
                foreach (var tr in result.Tracks)
                    await player.PlayAsync(tr);

                var ebPlaylist = new EmbedBuilder()
                    .WithColor(SpotifyDark)
                    .WithAuthor("📂  Playlist adicionada à fila")
                    .WithTitle(result.Playlist.Name)
                    .WithDescription($"```\n{qtd} músicas adicionadas à fila\n```")
                    .WithFooter($"Pedido por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .WithCurrentTimestamp();

                await loading.ModifyAsync(m => { m.Content = ""; m.Embed = ebPlaylist.Build(); });
                return;
            }

            var track = result.Track;
            if (track == null)
            {
                await loading.ModifyAsync(m => m.Content = $"<:erro:1493078898462949526> Erro ao carregar a música.");
                return;
            }

            try { await player.SetVolumeAsync(1.0f); } catch { }

            int position = await player.PlayAsync(track);

            Console.WriteLine($"[Music DEBUG] PlayAsync concluído. Position={position}, Volume após={player.Volume}, State={player.State}");

            await Task.Delay(500);
            try { await player.SetVolumeAsync(1.0f); } catch { }

            // ★★★ EMBED ESTILO SPOTIFY DARK ★★★
            Embed embed;
            if (position == 0)
            {
                // Tocando agora — card grande com capa embaixo
                embed = CriarEmbedSpotifyTocando(user, track, player);
            }
            else
            {
                // Adicionado à fila — card menor
                embed = CriarEmbedSpotifyFila(user, track, position);
            }

            await loading.ModifyAsync(m => { m.Content = ""; m.Embed = embed; });
        }

        // =========================================================
        // ★ EMBED SPOTIFY DARK — TOCANDO AGORA (capa grande)
        // =========================================================
        private Embed CriarEmbedSpotifyTocando(SocketGuildUser user, LavalinkTrack track, QueuedLavalinkPlayer player)
        {
            var dur = track.Duration;
            string durFmt = FormatarDuracao(dur);
            int volume = (int)(player.Volume * 100);
            int filaCount = player.Queue.Count;

            // Cargo mais alto do usuário (que não seja @everyone)
            var cargoAlto = user.Roles
                .Where(r => !r.IsEveryone)
                .OrderByDescending(r => r.Position)
                .FirstOrDefault();
            string cargoNome = cargoAlto?.Name ?? "Membro";

            // Barra inicial vazia (música acabou de começar)
            string barra = GerarBarraSpotify(TimeSpan.Zero, dur);

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("🎵  TOCANDO AGORA")
                .WithTitle(track.Title)
                .WithDescription(
                    $"### {track.Author}\n" +
                    $"```\n" +
                    $"⏱️  Duração: {durFmt}\n" +
                    $"🔊  Volume:  {volume}%\n" +
                    $"📋  Na fila: {filaCount} música(s)\n" +
                    $"```\n" +
                    $"{barra}\n" +
                    $"`0:00`{new string(' ', 50)}`{durFmt}`"
                )
                .WithImageUrl(track.ArtworkUri?.ToString() ?? "") // ★ IMAGEM GRANDE EMBAIXO
                .WithFooter(
                    $"Pedido por {user.Username}  •  {cargoNome}",
                    user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
                )
                .WithCurrentTimestamp();

            return eb.Build();
        }

        // =========================================================
        // ★ EMBED SPOTIFY DARK — ADICIONADO À FILA
        // =========================================================
        private Embed CriarEmbedSpotifyFila(SocketGuildUser user, LavalinkTrack track, int position)
        {
            var cargoAlto = user.Roles
                .Where(r => !r.IsEveryone)
                .OrderByDescending(r => r.Position)
                .FirstOrDefault();
            string cargoNome = cargoAlto?.Name ?? "Membro";

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("➕  ADICIONADO À FILA")
                .WithTitle(track.Title)
                .WithDescription(
                    $"### {track.Author}\n" +
                    $"```\n" +
                    $"📍  Posição:  #{position}\n" +
                    $"⏱️  Duração:  {FormatarDuracao(track.Duration)}\n" +
                    $"```"
                )
                .WithThumbnailUrl(track.ArtworkUri?.ToString() ?? "") // Thumbnail menor pra fila
                .WithFooter(
                    $"Pedido por {user.Username}  •  {cargoNome}",
                    user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
                )
                .WithCurrentTimestamp();

            return eb.Build();
        }

        // =========================================================
        // ★ BARRA DE PROGRESSO ESTILO SPOTIFY (caracteres limpos)
        // =========================================================
        private string GerarBarraSpotify(TimeSpan atual, TimeSpan total)
        {
            if (total.TotalMilliseconds <= 0)
                return "`▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬`";

            double percent = Math.Min(1.0, atual.TotalMilliseconds / total.TotalMilliseconds);
            int slots = 20;
            int pos = (int)(percent * slots);

            var sb = new System.Text.StringBuilder();
            sb.Append("`");
            for (int i = 0; i < slots; i++)
            {
                if (i < pos) sb.Append("▰");      // preenchido
                else if (i == pos) sb.Append("◉"); // bolinha posição atual
                else sb.Append("▱");               // vazio
            }
            sb.Append("`");
            return sb.ToString();
        }

        private async Task ExecutarSkip(SocketMessage msg, SocketGuildUser user)
        {
            if (user.VoiceChannel == null)
            {
                await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, entra numa call primeiro.");
                return;
            }

            var player = await ObterPlayerAsync(user.Guild.Id, user.VoiceChannel.Id, conectar: false);
            if (player == null || player.CurrentTrack == null)
            {
                await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Não tem nada tocando no momento.");
                return;
            }

            var pulada = player.CurrentTrack.Title;
            await player.SkipAsync();

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("⏭️  Pulada")
                .WithDescription($"**{pulada}**")
                .WithFooter($"Pulado por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

        // =========================================================
        // ★ COMANDO ZQUEUE com visual Spotify
        // =========================================================
        private async Task ExecutarQueue(SocketMessage msg, SocketGuildUser user)
        {
            var player = await ObterPlayerAsync(user.Guild.Id, 0, conectar: false);
            if (player == null || (player.CurrentTrack == null && player.Queue.Count == 0))
            {
                await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> A fila está vazia no momento.");
                return;
            }

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("📋  FILA DE REPRODUÇÃO");

            string descricao = "";

            if (player.CurrentTrack != null)
            {
                descricao += "**🎵  Tocando agora**\n";
                descricao += $"### {Truncar(player.CurrentTrack.Title, 50)}\n";
                descricao += $"`{player.CurrentTrack.Author}`  •  `{FormatarDuracao(player.CurrentTrack.Duration)}`\n\n";
                descricao += "━━━━━━━━━━━━━━━━━━━━━━\n\n";
            }

            if (player.Queue.Count > 0)
            {
                descricao += "**📂  Próximas músicas**\n```\n";
                int i = 1;
                foreach (var item in player.Queue.Take(10))
                {
                    var tr = item.Track;
                    if (tr == null) continue;
                    descricao += $"{i,2}. {Truncar(tr.Title, 45),-45} {FormatarDuracao(tr.Duration),6}\n";
                    i++;
                }
                descricao += "```";

                if (player.Queue.Count > 10)
                    descricao += $"\n*+ {player.Queue.Count - 10} músicas...*";
            }
            else
            {
                descricao += "*Nenhuma música na fila além da atual.*";
            }

            // Soma duração total da fila
            TimeSpan totalDuration = player.CurrentTrack?.Duration ?? TimeSpan.Zero;
            foreach (var item in player.Queue)
            {
                if (item.Track != null)
                    totalDuration += item.Track.Duration;
            }

            eb.WithDescription(descricao);
            eb.WithFooter(
                $"{player.Queue.Count} música(s) na fila  •  Tempo total: {FormatarDuracao(totalDuration)}",
                user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
            );
            eb.WithCurrentTimestamp();

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

        private async Task ExecutarPause(SocketMessage msg, SocketGuildUser user)
        {
            if (user.VoiceChannel == null) { await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, entra numa call."); return; }

            var player = await ObterPlayerAsync(user.Guild.Id, user.VoiceChannel.Id, conectar: false);
            if (player == null || player.CurrentTrack == null) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Não tem nada tocando."); return; }

            if (player.State == PlayerState.Paused) { await msg.Channel.SendMessageAsync("<:aviso:1493365148323152034> Já está pausada. Use `zresume`."); return; }

            await player.PauseAsync();

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("⏸️  Reprodução pausada")
                .WithDescription($"**{player.CurrentTrack.Title}**\n`{player.CurrentTrack.Author}`")
                .WithFooter($"Pausado por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

        private async Task ExecutarResume(SocketMessage msg, SocketGuildUser user)
        {
            if (user.VoiceChannel == null) { await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, entra numa call."); return; }

            var player = await ObterPlayerAsync(user.Guild.Id, user.VoiceChannel.Id, conectar: false);
            if (player == null || player.CurrentTrack == null) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Não tem nada tocando."); return; }

            if (player.State != PlayerState.Paused) { await msg.Channel.SendMessageAsync("<:aviso:1493365148323152034> A música não está pausada."); return; }

            await player.ResumeAsync();

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("▶️  Reprodução retomada")
                .WithDescription($"**{player.CurrentTrack.Title}**\n`{player.CurrentTrack.Author}`")
                .WithFooter($"Retomado por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

        private async Task ExecutarStop(SocketMessage msg, SocketGuildUser user)
        {
            if (user.VoiceChannel == null) { await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, entra numa call."); return; }

            var player = await ObterPlayerAsync(user.Guild.Id, user.VoiceChannel.Id, conectar: false);
            if (player == null) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Não estou tocando em nenhuma call."); return; }

            await player.StopAsync();
            await player.DisconnectAsync();
            await player.DisposeAsync();

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("⏹️  Reprodução encerrada")
                .WithDescription("Saí da call e limpei a fila.")
                .WithFooter($"Parado por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

        // =========================================================
        // ★ COMANDO ZNP — atualizado pra mostrar com a barra atual
        // =========================================================
        private async Task ExecutarNowPlaying(SocketMessage msg, SocketGuildUser user)
        {
            var player = await ObterPlayerAsync(user.Guild.Id, 0, conectar: false);
            if (player == null || player.CurrentTrack == null)
            {
                await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Não tem nada tocando.");
                return;
            }

            var track = player.CurrentTrack;
            var pos = player.Position?.Position ?? TimeSpan.Zero;
            var dur = track.Duration;

            string barra = GerarBarraSpotify(pos, dur);
            int volume = (int)(player.Volume * 100);
            int filaCount = player.Queue.Count;

            var cargoAlto = user.Roles
                .Where(r => !r.IsEveryone)
                .OrderByDescending(r => r.Position)
                .FirstOrDefault();
            string cargoNome = cargoAlto?.Name ?? "Membro";

            string statusEmoji = player.State == PlayerState.Paused ? "⏸️  PAUSADO" : "🎵  TOCANDO AGORA";

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor(statusEmoji)
                .WithTitle(track.Title)
                .WithDescription(
                    $"### {track.Author}\n" +
                    $"```\n" +
                    $"⏱️  Duração: {FormatarDuracao(dur)}\n" +
                    $"🔊  Volume:  {volume}%\n" +
                    $"📋  Na fila: {filaCount} música(s)\n" +
                    $"```\n" +
                    $"{barra}\n" +
                    $"`{FormatarDuracao(pos)}`{new string(' ', 50)}`{FormatarDuracao(dur)}`"
                )
                .WithImageUrl(track.ArtworkUri?.ToString() ?? "")
                .WithFooter(
                    $"Pedido por {user.Username}  •  {cargoNome}",
                    user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
                )
                .WithCurrentTimestamp();

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

        private async Task<QueuedLavalinkPlayer> ObterPlayerAsync(ulong guildId, ulong voiceChannelId, bool conectar)
        {
            try
            {
                if (!conectar)
                {
                    return await _audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(guildId);
                }

                var retrieveOptions = new PlayerRetrieveOptions(
                    ChannelBehavior: PlayerChannelBehavior.Join,
                    VoiceStateBehavior: MemberVoiceStateBehavior.Ignore
                );

                var playerOptions = new QueuedLavalinkPlayerOptions
                {
                    InitialVolume = 1.0f,
                    DisconnectOnStop = false,
                    SelfDeaf = false
                };

                var result = await _audioService.Players.RetrieveAsync<QueuedLavalinkPlayer, QueuedLavalinkPlayerOptions>(
                    guildId: guildId,
                    memberVoiceChannel: voiceChannelId,
                    playerFactory: PlayerFactory.Queued,
                    options: Options.Create(playerOptions),
                    retrieveOptions: retrieveOptions
                );

                if (!result.IsSuccess)
                {
                    Console.WriteLine($"[ObterPlayer] Falha: Status={result.Status}");
                    return null;
                }

                Console.WriteLine($"[ObterPlayer OK] Guild={guildId}, VoiceCh={voiceChannelId}, Volume={result.Player.Volume}");
                return result.Player;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ObterPlayer Error]: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private string FormatarDuracao(TimeSpan t)
        {
            if (t.TotalHours >= 1)
                return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
            return $"{t.Minutes}:{t.Seconds:D2}";
        }

        private string Truncar(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 2) + "..";
        }
    }
}
