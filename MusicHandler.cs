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
    // =====================================================================
    // MÓDULO DE MÚSICA - LAVALINK4NET v4 (CORRIGIDO)
    // Comandos: zplay, zskip, zqueue, zpause, zresume, zstop, znp
    // =====================================================================

    public class MusicHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly IAudioService _audioService;
        private static readonly Dictionary<ulong, DateTime> _cooldowns = new();

        private static readonly Color PurpleTheme = new Color(160, 80, 220);

        public MusicHandler(DiscordSocketClient client, IAudioService audioService)
        {
            _client = client;
            _audioService = audioService;
            _client.MessageReceived += HandleMessage;

            _ = Task.Run(() => VigilanteCallVazia());
        }

        // ==============================================================
        // Auto-desconecta quando o bot fica sozinho numa call
        // ==============================================================
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

        // =========================================================
        //                    COMANDO ZPLAY
        // =========================================================
        private async Task ExecutarPlay(SocketMessage msg, SocketGuildUser user, string content)
        {
            var partes = content.Split(' ', 2);
            if (partes.Length < 2 || string.IsNullOrWhiteSpace(partes[1]))
            {
                await msg.Channel.SendMessageAsync("❓ **Uso:** `zplay <nome da música ou link>`\n*Exemplo: `zplay lofi hip hop radio`*");
                return;
            }

            string query = partes[1].Trim();

            var voiceChannel = user.VoiceChannel;
            if (voiceChannel == null)
            {
                await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, entra numa call de voz primeiro!");
                return;
            }

            // Verifica permissões
            var botGuildPerms = user.Guild.CurrentUser.GetPermissions(voiceChannel);
            if (!botGuildPerms.Connect || !botGuildPerms.Speak)
            {
                await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> Eu não tenho permissão de **Conectar** ou **Falar** no canal `{voiceChannel.Name}`.");
                return;
            }

            var loading = await msg.Channel.SendMessageAsync($"<a:carregandoportal:1492944498605686844> Procurando **{query}**...");

            var player = await ObterPlayerAsync(user.Guild.Id, voiceChannel.Id, conectar: true);
            if (player == null)
            {
                await loading.ModifyAsync(m => m.Content = $"<:erro:1493078898462949526> Falha ao conectar. Verifica se o Lavalink está online e se `LAVALINK_HOST`/`LAVALINK_PASSWORD` estão certos.");
                return;
            }

            TrackLoadResult result;
            bool isUrl = Uri.TryCreate(query, UriKind.Absolute, out var uri)
                         && (uri.Scheme == "http" || uri.Scheme == "https");

            try
            {
                if (isUrl)
                    result = await _audioService.Tracks.LoadTracksAsync(query, TrackSearchMode.None);
                else
                    result = await _audioService.Tracks.LoadTracksAsync(query, TrackSearchMode.YouTube);
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

                var eb = new EmbedBuilder()
                    .WithColor(PurpleTheme)
                    .WithTitle("📂 Playlist adicionada à fila")
                    .WithDescription($"**{result.Playlist.Name}**\n`{qtd}` músicas foram adicionadas à fila.")
                    .WithFooter($"Pedido por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

                await loading.ModifyAsync(m => { m.Content = ""; m.Embed = eb.Build(); });
                return;
            }

            // Música única
            var track = result.Track;
            if (track == null)
            {
                await loading.ModifyAsync(m => m.Content = $"<:erro:1493078898462949526> Erro ao carregar a música.");
                return;
            }

            int position = await player.PlayAsync(track);

            var embed = new EmbedBuilder()
                .WithColor(PurpleTheme)
                .WithAuthor(position == 0 ? "🎵 Tocando agora" : $"➕ Adicionado à fila (posição #{position})")
                .WithTitle(track.Title)
                .WithUrl(track.Uri?.ToString() ?? "")
                .WithDescription($"**Canal:** {track.Author}\n**Duração:** `{FormatarDuracao(track.Duration)}`")
                .WithThumbnailUrl(track.ArtworkUri?.ToString() ?? "")
                .WithFooter($"Pedido por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

            await loading.ModifyAsync(m => { m.Content = ""; m.Embed = embed.Build(); });
        }

        // =========================================================
        //                    COMANDO ZSKIP
        // =========================================================
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

            await msg.Channel.SendMessageAsync($"<a:sucess:1494692628372132013> {user.Mention} pulou **{pulada}**.");
        }

        // =========================================================
        //                    COMANDO ZQUEUE
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
                .WithColor(PurpleTheme)
                .WithTitle("🎵 Fila de Músicas");

            string descricao = "";

            if (player.CurrentTrack != null)
                descricao += $"**🎶 Tocando agora:**\n[{player.CurrentTrack.Title}]({player.CurrentTrack.Uri}) — `{FormatarDuracao(player.CurrentTrack.Duration)}`\n\n";

            if (player.Queue.Count > 0)
            {
                descricao += "**📋 Próximas:**\n";
                int i = 1;
                foreach (var item in player.Queue.Take(10))
                {
                    var tr = item.Track;
                    if (tr == null) continue;
                    descricao += $"`{i}.` [{Truncar(tr.Title, 60)}]({tr.Uri}) — `{FormatarDuracao(tr.Duration)}`\n";
                    i++;
                }

                if (player.Queue.Count > 10)
                    descricao += $"\n*...e mais `{player.Queue.Count - 10}` músicas.*";
            }
            else
            {
                descricao += "*Nenhuma música na fila além da atual.*";
            }

            eb.WithDescription(descricao);
            eb.WithFooter($"Total na fila: {player.Queue.Count} música(s)");

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

        // =========================================================
        //                    COMANDO ZPAUSE
        // =========================================================
        private async Task ExecutarPause(SocketMessage msg, SocketGuildUser user)
        {
            if (user.VoiceChannel == null) { await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, entra numa call."); return; }

            var player = await ObterPlayerAsync(user.Guild.Id, user.VoiceChannel.Id, conectar: false);
            if (player == null || player.CurrentTrack == null) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Não tem nada tocando."); return; }

            if (player.State == PlayerState.Paused) { await msg.Channel.SendMessageAsync("<:aviso:1493365148323152034> Já está pausada. Use `zresume`."); return; }

            await player.PauseAsync();
            await msg.Channel.SendMessageAsync($"⏸️ {user.Mention} pausou a música.");
        }

        // =========================================================
        //                    COMANDO ZRESUME
        // =========================================================
        private async Task ExecutarResume(SocketMessage msg, SocketGuildUser user)
        {
            if (user.VoiceChannel == null) { await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, entra numa call."); return; }

            var player = await ObterPlayerAsync(user.Guild.Id, user.VoiceChannel.Id, conectar: false);
            if (player == null || player.CurrentTrack == null) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Não tem nada tocando."); return; }

            if (player.State != PlayerState.Paused) { await msg.Channel.SendMessageAsync("<:aviso:1493365148323152034> A música não está pausada."); return; }

            await player.ResumeAsync();
            await msg.Channel.SendMessageAsync($"▶️ {user.Mention} retomou a música.");
        }

        // =========================================================
        //                    COMANDO ZSTOP
        // =========================================================
        private async Task ExecutarStop(SocketMessage msg, SocketGuildUser user)
        {
            if (user.VoiceChannel == null) { await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, entra numa call."); return; }

            var player = await ObterPlayerAsync(user.Guild.Id, user.VoiceChannel.Id, conectar: false);
            if (player == null) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Não estou tocando em nenhuma call."); return; }

            await player.StopAsync();
            await player.DisconnectAsync();
            await player.DisposeAsync();

            await msg.Channel.SendMessageAsync($"⏹️ {user.Mention} parou a música e saí da call.");
        }

        // =========================================================
        //                    COMANDO ZNP
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

            string barra = GerarBarraProgresso(pos, dur);

            var eb = new EmbedBuilder()
                .WithColor(PurpleTheme)
                .WithAuthor("🎵 Tocando agora")
                .WithTitle(track.Title)
                .WithUrl(track.Uri?.ToString() ?? "")
                .WithDescription($"**Canal:** {track.Author}\n\n{barra}\n`{FormatarDuracao(pos)} / {FormatarDuracao(dur)}`")
                .WithThumbnailUrl(track.ArtworkUri?.ToString() ?? "");

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

        // =========================================================
        // ★★★ MÉTODO CORRIGIDO - API CORRETA DO LAVALINK4NET v4 ★★★
        // =========================================================
        private async Task<QueuedLavalinkPlayer> ObterPlayerAsync(ulong guildId, ulong voiceChannelId, bool conectar)
        {
            try
            {
                // Só pega player existente, sem conectar
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
                    InitialVolume = 0.5f,
                    DisconnectOnStop = false,
                    SelfDeaf = true
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

                return result.Player;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ObterPlayer Error]: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        // =========================================================
        //                    AUXILIARES
        // =========================================================
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

        private string GerarBarraProgresso(TimeSpan atual, TimeSpan total)
        {
            if (total.TotalMilliseconds <= 0) return "`🔘▬▬▬▬▬▬▬▬▬▬▬▬▬▬`";

            double percent = Math.Min(1.0, atual.TotalMilliseconds / total.TotalMilliseconds);
            int slots = 15;
            int pos = (int)(percent * slots);

            var sb = new System.Text.StringBuilder();
            sb.Append("`");
            for (int i = 0; i < slots; i++)
            {
                if (i == pos) sb.Append("🔘");
                else sb.Append("▬");
            }
            sb.Append("`");
            return sb.ToString();
        }
    }
}
