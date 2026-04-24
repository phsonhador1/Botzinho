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
using System.Collections.Concurrent;
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

        // Tema dark estilo Spotify
        private static readonly Color SpotifyDark = new Color(24, 24, 24);

        // Estado de loop por servidor: 0=off, 1=música, 2=fila
        private static readonly ConcurrentDictionary<ulong, int> _loopMode = new();

        // Mensagens com player ativo: guildId → (msgId, channelId)
        private static readonly ConcurrentDictionary<ulong, (ulong MsgId, ulong ChId)> _playerMessages = new();

        public MusicHandler(DiscordSocketClient client, IAudioService audioService)
        {
            _client = client;
            _audioService = audioService;
            _client.MessageReceived += HandleMessage;
            _client.ButtonExecuted += HandleButton;
            _client.SelectMenuExecuted += HandleSelectMenu;

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
                                _playerMessages.TryRemove(guild.Id, out _);
                                _loopMode.TryRemove(guild.Id, out _);
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

        // =====================================================================
        // ★★★ HANDLER DOS BOTÕES INTERATIVOS ★★★
        // =====================================================================
        private Task HandleButton(SocketMessageComponent component)
        {
            _ = Task.Run(async () => {
                try
                {
                    if (!component.Data.CustomId.StartsWith("zoe_music_")) return;
                    if (component.GuildId == null) return;

                    var user = component.User as SocketGuildUser;
                    if (user == null) return;

                    ulong guildId = component.GuildId.Value;

                    // Segurança: só quem está na call pode controlar
                    var botCall = user.Guild.CurrentUser.VoiceChannel;
                    if (botCall == null || user.VoiceChannel?.Id != botCall.Id)
                    {
                        await component.RespondAsync(
                            "<:erro:1493078898462949526> Você precisa estar **na mesma call que o bot** pra controlar a reprodução.",
                            ephemeral: true);
                        return;
                    }

                    var player = await _audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(guildId);
                    if (player == null)
                    {
                        await component.RespondAsync(
                            "<:erro:1493078898462949526> Nenhuma música tocando.",
                            ephemeral: true);
                        return;
                    }

                    string acao = component.Data.CustomId;

                    switch (acao)
                    {
                        case "zoe_music_pause":
                            if (player.State == PlayerState.Paused)
                            {
                                await player.ResumeAsync();
                                await component.DeferAsync();
                            }
                            else
                            {
                                await player.PauseAsync();
                                await component.DeferAsync();
                            }
                            await AtualizarPlayerEmbed(guildId, user);
                            break;

                        case "zoe_music_skip":
                            if (player.CurrentTrack == null)
                            {
                                await component.RespondAsync("<:erro:1493078898462949526> Nada tocando pra pular.", ephemeral: true);
                                return;
                            }
                            await player.SkipAsync();
                            await component.DeferAsync();
                            await Task.Delay(500); // pequena espera pra trocar a track
                            await AtualizarPlayerEmbed(guildId, user);
                            break;

                        case "zoe_music_stop":
                            _playerMessages.TryRemove(guildId, out _);
                            _loopMode.TryRemove(guildId, out _);
                            await player.StopAsync();
                            await player.DisconnectAsync();
                            await player.DisposeAsync();

                            // Edita o embed pra mostrar "encerrado"
                            try
                            {
                                var ebFim = new EmbedBuilder()
                                    .WithColor(SpotifyDark)
                                    .WithAuthor("⏹️  Reprodução encerrada")
                                    .WithDescription("Saí da call e limpei a fila.")
                                    .WithFooter($"Encerrado por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                                    .Build();

                                await component.UpdateAsync(m => {
                                    m.Embed = ebFim;
                                    m.Components = new ComponentBuilder().Build();
                                });
                            }
                            catch { try { await component.DeferAsync(); } catch { } }
                            break;

                        case "zoe_music_loop":
                            int modoAtual = _loopMode.GetValueOrDefault(guildId, 0);
                            int novoModo = (modoAtual + 1) % 3;
                            _loopMode[guildId] = novoModo;

                            player.RepeatMode = novoModo switch
                            {
                                1 => TrackRepeatMode.Track,
                                2 => TrackRepeatMode.Queue,
                                _ => TrackRepeatMode.None
                            };

                            await component.DeferAsync();
                            await AtualizarPlayerEmbed(guildId, user);
                            break;

                        case "zoe_music_queue":
                            await MostrarFilaEphemeral(component, player);
                            break;

                        case "zoe_music_remove":
                            await MostrarMenuRemover(component, player);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HandleButton Error]: {ex.Message}\n{ex.StackTrace}");
                    try { await component.RespondAsync($"<:erro:1493078898462949526> Erro: {ex.Message}", ephemeral: true); } catch { }
                }
            });
            return Task.CompletedTask;
        }

        // =====================================================================
        // ★ HANDLER DO SELECT MENU (remover música)
        // =====================================================================
        private Task HandleSelectMenu(SocketMessageComponent component)
        {
            _ = Task.Run(async () => {
                try
                {
                    if (component.Data.CustomId != "zoe_music_remove_select") return;
                    if (component.GuildId == null) return;

                    var user = component.User as SocketGuildUser;
                    if (user == null) return;

                    ulong guildId = component.GuildId.Value;

                    var botCall = user.Guild.CurrentUser.VoiceChannel;
                    if (botCall == null || user.VoiceChannel?.Id != botCall.Id)
                    {
                        await component.RespondAsync(
                            "<:erro:1493078898462949526> Você precisa estar na mesma call que o bot.",
                            ephemeral: true);
                        return;
                    }

                    var player = await _audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(guildId);
                    if (player == null)
                    {
                        await component.RespondAsync("<:erro:1493078898462949526> Nenhuma música tocando.", ephemeral: true);
                        return;
                    }

                    if (!int.TryParse(component.Data.Values.FirstOrDefault(), out int indice))
                    {
                        await component.RespondAsync("<:erro:1493078898462949526> Seleção inválida.", ephemeral: true);
                        return;
                    }

                    if (indice < 0 || indice >= player.Queue.Count)
                    {
                        await component.RespondAsync("<:erro:1493078898462949526> Música não está mais na fila.", ephemeral: true);
                        return;
                    }

                    var removida = player.Queue.ElementAt(indice).Track;
                    await player.Queue.RemoveAtAsync(indice);

                    var eb = new EmbedBuilder()
                        .WithColor(SpotifyDark)
                        .WithAuthor("🗑️  Música removida")
                        .WithDescription($"**{removida?.Title ?? "Música"}** foi removida da fila.")
                        .WithFooter($"Removido por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

                    await component.UpdateAsync(m => {
                        m.Embed = eb.Build();
                        m.Components = new ComponentBuilder().Build();
                    });

                    // Atualiza o player principal pra refletir a nova fila
                    await AtualizarPlayerEmbed(guildId, user);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SelectMenu Error]: {ex.Message}");
                    try { await component.RespondAsync($"<:erro:1493078898462949526> Erro: {ex.Message}", ephemeral: true); } catch { }
                }
            });
            return Task.CompletedTask;
        }

        // =====================================================================
        // ★ Atualiza o embed do player principal
        // =====================================================================
        private async Task AtualizarPlayerEmbed(ulong guildId, SocketGuildUser user)
        {
            try
            {
                if (!_playerMessages.TryGetValue(guildId, out var info)) return;

                var player = await _audioService.Players.GetPlayerAsync<QueuedLavalinkPlayer>(guildId);
                if (player == null || player.CurrentTrack == null) return;

                var channel = _client.GetChannel(info.ChId) as ISocketMessageChannel;
                if (channel == null) return;

                var msg = await channel.GetMessageAsync(info.MsgId) as IUserMessage;
                if (msg == null) return;

                var embed = CriarEmbedPlayer(user, player, player.CurrentTrack, guildId);
                var components = CriarBotoesPlayer(player, guildId);

                await msg.ModifyAsync(m => {
                    m.Embed = embed;
                    m.Components = components;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AtualizarPlayer Error]: {ex.Message}");
            }
        }

        // =====================================================================
        // ★ Mostra a fila em mensagem ephemeral
        // =====================================================================
        private async Task MostrarFilaEphemeral(SocketMessageComponent component, QueuedLavalinkPlayer player)
        {
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
                foreach (var item in player.Queue.Take(15))
                {
                    var tr = item.Track;
                    if (tr == null) continue;
                    descricao += $"{i,2}. {Truncar(tr.Title, 45),-45} {FormatarDuracao(tr.Duration),6}\n";
                    i++;
                }
                descricao += "```";

                if (player.Queue.Count > 15)
                    descricao += $"\n*+ {player.Queue.Count - 15} músicas...*";
            }
            else
            {
                descricao += "*Nenhuma música na fila além da atual.*";
            }

            TimeSpan totalDuration = player.CurrentTrack?.Duration ?? TimeSpan.Zero;
            foreach (var item in player.Queue)
                if (item.Track != null) totalDuration += item.Track.Duration;

            eb.WithDescription(descricao);
            eb.WithFooter(
                $"{player.Queue.Count} música(s) na fila  •  Tempo total: {FormatarDuracao(totalDuration)}"
            );
            eb.WithCurrentTimestamp();

            await component.RespondAsync(embed: eb.Build(), ephemeral: true);
        }

        // =====================================================================
        // ★ Mostra dropdown pra remover música da fila
        // =====================================================================
        private async Task MostrarMenuRemover(SocketMessageComponent component, QueuedLavalinkPlayer player)
        {
            if (player.Queue.Count == 0)
            {
                await component.RespondAsync(
                    "<:erro:1493078898462949526> A fila está vazia, nada pra remover.",
                    ephemeral: true);
                return;
            }

            var menu = new SelectMenuBuilder()
                .WithCustomId("zoe_music_remove_select")
                .WithPlaceholder("Selecione a música pra remover da fila...")
                .WithMinValues(1)
                .WithMaxValues(1);

            int i = 0;
            foreach (var item in player.Queue.Take(25))
            {
                var tr = item.Track;
                if (tr == null) { i++; continue; }

                menu.AddOption(
                    label: Truncar(tr.Title, 95),
                    value: i.ToString(),
                    description: $"{Truncar(tr.Author, 40)} • {FormatarDuracao(tr.Duration)}",
                    emote: new Emoji("🎵")
                );
                i++;
            }

            var components = new ComponentBuilder()
                .WithSelectMenu(menu)
                .Build();

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("🗑️  REMOVER MÚSICA DA FILA")
                .WithDescription($"Selecione qual música quer remover. Você tem **{Math.Min(player.Queue.Count, 25)}** músicas pra escolher.");

            await component.RespondAsync(embed: eb.Build(), components: components, ephemeral: true);
        }

        // =====================================================================
        // ★★★ COMANDO ZPLAY ★★★
        // =====================================================================
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
                await loading.ModifyAsync(m => m.Content = $"<:erro:1493078898462949526> Falha ao conectar. Verifica se o Lavalink está online.");
                return;
            }

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

                var ebPL = new EmbedBuilder()
                    .WithColor(SpotifyDark)
                    .WithAuthor("📂  Playlist adicionada à fila")
                    .WithTitle(result.Playlist.Name)
                    .WithDescription($"```\n{qtd} músicas adicionadas à fila\n```")
                    .WithFooter($"Pedido por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .WithCurrentTimestamp();

                await loading.ModifyAsync(m => { m.Content = ""; m.Embed = ebPL.Build(); });
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
            await Task.Delay(500);
            try { await player.SetVolumeAsync(1.0f); } catch { }

            if (position == 0)
            {
                // Tocando agora → manda player completo com botões
                var embed = CriarEmbedPlayer(user, player, track, user.Guild.Id);
                var components = CriarBotoesPlayer(player, user.Guild.Id);

                await loading.DeleteAsync();
                var msgPlayer = await msg.Channel.SendMessageAsync(embed: embed, components: components);

                _playerMessages[user.Guild.Id] = (msgPlayer.Id, msg.Channel.Id);
            }
            else
            {
                // Adicionado à fila → embed simples sem botões
                var ebFila = CriarEmbedFila(user, track, position);
                await loading.ModifyAsync(m => { m.Content = ""; m.Embed = ebFila; });

                // Atualiza o player principal pra mostrar a fila aumentada
                await AtualizarPlayerEmbed(user.Guild.Id, user);
            }
        }

        // =====================================================================
        // ★ EMBED PLAYER PRINCIPAL (Spotify dark, capa grande)
        // =====================================================================
        private Embed CriarEmbedPlayer(SocketGuildUser user, QueuedLavalinkPlayer player, LavalinkTrack track, ulong guildId)
        {
            var dur = track.Duration;
            int volume = (int)(player.Volume * 100);
            int filaCount = player.Queue.Count;

            var cargoAlto = user.Roles
                .Where(r => !r.IsEveryone)
                .OrderByDescending(r => r.Position)
                .FirstOrDefault();
            string cargoNome = cargoAlto?.Name ?? "Membro";

            int loopMode = _loopMode.GetValueOrDefault(guildId, 0);
            string loopStatus = loopMode switch
            {
                1 => "🔂  Loop: música atual",
                2 => "🔁  Loop: fila inteira",
                _ => "➡️  Sem loop"
            };

            string statusHeader = player.State == PlayerState.Paused
                ? "⏸️  PAUSADO"
                : "🎵  TOCANDO AGORA";

            var pos = player.Position?.Position ?? TimeSpan.Zero;
            string barra = GerarBarraSpotify(pos, dur);

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor(statusHeader)
                .WithTitle(track.Title)
                .WithDescription(
                    $"### {track.Author}\n" +
                    $"```\n" +
                    $"⏱️  Duração: {FormatarDuracao(dur)}\n" +
                    $"🔊  Volume:  {volume}%\n" +
                    $"📋  Na fila: {filaCount} música(s)\n" +
                    $"```\n" +
                    $"{barra}\n" +
                    $"`{FormatarDuracao(pos)}`{new string(' ', 50)}`{FormatarDuracao(dur)}`\n\n" +
                    $"{loopStatus}"
                )
                .WithImageUrl(track.ArtworkUri?.ToString() ?? "")
                .WithFooter(
                    $"Pedido por {user.Username}  •  {cargoNome}",
                    user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
                )
                .WithCurrentTimestamp();

            return eb.Build();
        }

        // =====================================================================
        // ★ BOTÕES DO PLAYER (estilo Spotify)
        // =====================================================================
        private MessageComponent CriarBotoesPlayer(QueuedLavalinkPlayer player, ulong guildId)
        {
            bool pausado = player.State == PlayerState.Paused;
            int loopMode = _loopMode.GetValueOrDefault(guildId, 0);

            // Estilo do botão de loop muda conforme estado
            ButtonStyle estiloLoop = loopMode == 0 ? ButtonStyle.Secondary : ButtonStyle.Success;

            var cb = new ComponentBuilder()
                // Linha 1: controles principais
                .WithButton(
                    customId: "zoe_music_pause",
                    style: ButtonStyle.Primary,
                    emote: new Emoji(pausado ? "▶️" : "⏸️"),
                    row: 0)
                .WithButton(
                    customId: "zoe_music_skip",
                    style: ButtonStyle.Secondary,
                    emote: new Emoji("⏭️"),
                    row: 0)
                .WithButton(
                    customId: "zoe_music_stop",
                    style: ButtonStyle.Danger,
                    emote: new Emoji("⏹️"),
                    row: 0)
                .WithButton(
                    customId: "zoe_music_loop",
                    style: estiloLoop,
                    emote: new Emoji(loopMode == 1 ? "🔂" : "🔁"),
                    row: 0)
                // Linha 2: fila
                .WithButton(
                    label: "Ver fila",
                    customId: "zoe_music_queue",
                    style: ButtonStyle.Secondary,
                    emote: new Emoji("📋"),
                    row: 1)
                .WithButton(
                    label: "Remover música",
                    customId: "zoe_music_remove",
                    style: ButtonStyle.Secondary,
                    emote: new Emoji("🗑️"),
                    row: 1);

            return cb.Build();
        }

        // =====================================================================
        // ★ EMBED "ADICIONADO À FILA" (sem botões, é só notificação)
        // =====================================================================
        private Embed CriarEmbedFila(SocketGuildUser user, LavalinkTrack track, int position)
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
                .WithThumbnailUrl(track.ArtworkUri?.ToString() ?? "")
                .WithFooter(
                    $"Pedido por {user.Username}  •  {cargoNome}",
                    user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
                )
                .WithCurrentTimestamp();

            return eb.Build();
        }

        // =====================================================================
        // ★ BARRA DE PROGRESSO ESTILO SPOTIFY
        // =====================================================================
        private string GerarBarraSpotify(TimeSpan atual, TimeSpan total)
        {
            if (total.TotalMilliseconds <= 0)
                return "`▱▱▱▱▱▱▱▱▱▱▱▱▱▱▱▱▱▱▱▱`";

            double percent = Math.Min(1.0, atual.TotalMilliseconds / total.TotalMilliseconds);
            int slots = 20;
            int pos = (int)(percent * slots);

            var sb = new System.Text.StringBuilder();
            sb.Append("`");
            for (int i = 0; i < slots; i++)
            {
                if (i < pos) sb.Append("▰");
                else if (i == pos) sb.Append("◉");
                else sb.Append("▱");
            }
            sb.Append("`");
            return sb.ToString();
        }

        // =====================================================================
        // COMANDOS DE TEXTO (zskip, zqueue, zpause, etc)
        // =====================================================================
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

            await Task.Delay(500);
            await AtualizarPlayerEmbed(user.Guild.Id, user);

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("⏭️  Pulada")
                .WithDescription($"**{pulada}**")
                .WithFooter($"Pulado por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

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

            TimeSpan totalDuration = player.CurrentTrack?.Duration ?? TimeSpan.Zero;
            foreach (var item in player.Queue)
                if (item.Track != null) totalDuration += item.Track.Duration;

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
            await AtualizarPlayerEmbed(user.Guild.Id, user);

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("⏸️  Pausado")
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
            await AtualizarPlayerEmbed(user.Guild.Id, user);

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("▶️  Retomado")
                .WithDescription($"**{player.CurrentTrack.Title}**\n`{player.CurrentTrack.Author}`")
                .WithFooter($"Retomado por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

        private async Task ExecutarStop(SocketMessage msg, SocketGuildUser user)
        {
            if (user.VoiceChannel == null) { await msg.Channel.SendMessageAsync($"<:erro:1493078898462949526> {user.Mention}, entra numa call."); return; }

            var player = await ObterPlayerAsync(user.Guild.Id, user.VoiceChannel.Id, conectar: false);
            if (player == null) { await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Não estou tocando em nenhuma call."); return; }

            _playerMessages.TryRemove(user.Guild.Id, out _);
            _loopMode.TryRemove(user.Guild.Id, out _);

            await player.StopAsync();
            await player.DisconnectAsync();
            await player.DisposeAsync();

            var eb = new EmbedBuilder()
                .WithColor(SpotifyDark)
                .WithAuthor("⏹️  Reprodução encerrada")
                .WithDescription("Saí da call e limpei a fila.")
                .WithFooter($"Encerrado por {user.Username}", user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

            await msg.Channel.SendMessageAsync(embed: eb.Build());
        }

        private async Task ExecutarNowPlaying(SocketMessage msg, SocketGuildUser user)
        {
            var player = await ObterPlayerAsync(user.Guild.Id, 0, conectar: false);
            if (player == null || player.CurrentTrack == null)
            {
                await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Não tem nada tocando.");
                return;
            }

            var embed = CriarEmbedPlayer(user, player, player.CurrentTrack, user.Guild.Id);
            var components = CriarBotoesPlayer(player, user.Guild.Id);

            var msgEnviada = await msg.Channel.SendMessageAsync(embed: embed, components: components);

            // Atualiza o "player ativo" pra essa nova mensagem
            _playerMessages[user.Guild.Id] = (msgEnviada.Id, msg.Channel.Id);
        }

        // =====================================================================
        // OBTER PLAYER
        // =====================================================================
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

                return result.Player;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ObterPlayer Error]: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        // =====================================================================
        // AUXILIARES
        // =====================================================================
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
