using Discord;
using Discord.Audio;
using Discord.WebSocket;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Botzinho.Music
{
    public class MusicModule
    {
        private readonly DiscordSocketClient _client;
        private readonly YoutubeClient _youtube;
        private SpotifyClient _spotify;

        // Fila de Músicas e Controle de Áudio por Servidor
        private static readonly Dictionary<ulong, Queue<MusicTrack>> _queues = new();
        private static readonly Dictionary<ulong, IAudioClient> _audioClients = new();
        private static readonly Dictionary<ulong, bool> _isPlaying = new();
        private static readonly Dictionary<ulong, bool> _skipRequested = new();

        // ⚠️ COLOQUE SUAS CHAVES DA API DO SPOTIFY AQUI
        private const string SPOTIFY_CLIENT_ID = "b59803ba196c475598c096d74c1fa12f";
        private const string SPOTIFY_CLIENT_SECRET = "f8f5b609f3344cfaae620dc3c0929f92";

        public MusicModule(DiscordSocketClient client)
        {
            _client = client;
            _youtube = new YoutubeClient();

            // Inicia a autenticação do Spotify automaticamente em background
            _ = AutenticarSpotifyAsync();

            _client.MessageReceived += HandleCommand;
        }

        // ==========================================
        // AUTENTICAÇÃO SPOTIFY
        // ==========================================
        private async Task AutenticarSpotifyAsync()
        {
            try
            {
                var config = SpotifyClientConfig.CreateDefault();
                var request = new ClientCredentialsRequest(SPOTIFY_CLIENT_ID, SPOTIFY_CLIENT_SECRET);
                var response = await new OAuthClient(config).RequestToken(request);
                _spotify = new SpotifyClient(config.WithToken(response.AccessToken));
                Console.WriteLine("[Spotify API] Autenticado com sucesso e pronto para uso!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Spotify API] Erro crítico ao autenticar: {ex.Message}");
            }
        }

        private async Task HandleCommand(SocketMessage msg)
        {
            if (msg.Author.IsBot || msg is not SocketUserMessage message) return;
            var content = msg.Content.ToLower().Trim();
            var user = msg.Author as SocketGuildUser;
            var guildId = user.Guild.Id;

            // ==========================================
            // COMANDO: !PLAY
            // ==========================================
            if (content.StartsWith("!play"))
            {
                string query = msg.Content.Substring(5).Trim();
                if (string.IsNullOrEmpty(query))
                {
                    await msg.Channel.SendMessageAsync("❓ Diga o nome da música ou mande um link do YouTube/Spotify.");
                    return;
                }

                if (user.VoiceChannel == null)
                {
                    await msg.Channel.SendMessageAsync("<:erro:1493078898462949526> Você precisa estar conectado a um canal de voz primeiro.");
                    return;
                }

                var msgAviso = await msg.Channel.SendMessageAsync($"⏳ Processando sua solicitação...");

                if (!_queues.ContainsKey(guildId)) _queues[guildId] = new Queue<MusicTrack>();

                // --- 1. FILTRO: SPOTIFY API ---
                if (query.Contains("open.spotify.com"))
                {
                    if (_spotify == null)
                    {
                        await msgAviso.ModifyAsync(m => m.Content = "<:erro:1493078898462949526> O sistema do Spotify não está autenticado no momento.");
                        return;
                    }

                    try
                    {
                        var trackList = new List<MusicTrack>();
                        string id = ExtractSpotifyId(query);

                        // Lendo Playlist
                        if (query.Contains("/playlist/"))
                        {
                            var playlist = await _spotify.Playlists.Get(id);
                            foreach (var item in playlist.Tracks.Items)
                            {
                                if (item.Track is FullTrack t)
                                    trackList.Add(new MusicTrack { Title = $"{t.Name} - {t.Artists.FirstOrDefault()?.Name}", IsSpotify = true });
                            }
                            await msgAviso.ModifyAsync(m => m.Content = $"<:spotify:1496639283224772639> **Playlist importada:** {playlist.Name} ({trackList.Count} faixas adicionadas)");
                        }
                        // Lendo Álbum
                        else if (query.Contains("/album/"))
                        {
                            var album = await _spotify.Albums.Get(id);
                            foreach (var t in album.Tracks.Items)
                            {
                                trackList.Add(new MusicTrack { Title = $"{t.Name} - {t.Artists.FirstOrDefault()?.Name}", IsSpotify = true });
                            }
                            await msgAviso.ModifyAsync(m => m.Content = $"<:spotify:1496639283224772639> **Álbum importado:** {album.Name} ({trackList.Count} faixas adicionadas)");
                        }
                        // Lendo Faixa Única (Track)
                        else if (query.Contains("/track/"))
                        {
                            var t = await _spotify.Tracks.Get(id);
                            var trackTitle = $"{t.Name} - {t.Artists.FirstOrDefault()?.Name}";
                            trackList.Add(new MusicTrack { Title = trackTitle, IsSpotify = true });
                            await msgAviso.ModifyAsync(m => m.Content = $"<:spotify:1496639283224772639> Adicionado: **{trackTitle}**");
                        }

                        // Joga tudo na fila (Lazy Loading: não carrega áudio do YT agora para não travar o bot)
                        foreach (var t in trackList) _queues[guildId].Enqueue(t);
                    }
                    catch (Exception ex)
                    {
                        await msgAviso.ModifyAsync(m => m.Content = $"<:erro:1493078898462949526> Erro ao ler dados do Spotify. Verifique se o link é válido.");
                        Console.WriteLine($"[Spotify Parse Error]: {ex.Message}");
                        return;
                    }
                }
                // --- 2. FILTRO: YOUTUBE / BUSCA DE TEXTO ---
                else
                {
                    try
                    {
                        var video = await _youtube.Search.GetVideosAsync(query).FirstOrDefaultAsync();
                        if (video == null)
                        {
                            await msgAviso.ModifyAsync(m => m.Content = "<:erro:1493078898462949526> Não encontrei nenhuma música com essa pesquisa.");
                            return;
                        }

                        _queues[guildId].Enqueue(new MusicTrack
                        {
                            Title = video.Title,
                            Url = video.Url,
                            Duration = video.Duration?.ToString(@"mm\:ss") ?? "00:00",
                            IsSpotify = false
                        });

                        await msgAviso.ModifyAsync(m => m.Content = $"✅ Adicionado à fila: **{video.Title}**");
                    }
                    catch
                    {
                        await msgAviso.ModifyAsync(m => m.Content = "<:erro:1493078898462949526> Ocorreu um erro ao buscar no YouTube.");
                        return;
                    }
                }

                // --- 3. GATILHO DO PLAYER ---
                if (!_isPlaying.ContainsKey(guildId) || !_isPlaying[guildId])
                {
                    _ = StartPlaying(user.VoiceChannel, msg.Channel as ITextChannel);
                }
            }

            // ==========================================
            // COMANDO: !SKIP
            // ==========================================
            else if (content == "!skip" || content == "!pular")
            {
                if (!_isPlaying.ContainsKey(guildId) || !_isPlaying[guildId])
                {
                    await msg.Channel.SendMessageAsync("❌ Nenhuma música está tocando agora.");
                    return;
                }

                _skipRequested[guildId] = true; // Sinaliza o FFmpeg para interromper a música atual imediatamente
                await msg.Channel.SendMessageAsync("⏭️ Pulando para a próxima...");
            }

            // ==========================================
            // COMANDO: !STOP
            // ==========================================
            else if (content == "!stop" || content == "!leave")
            {
                if (_queues.ContainsKey(guildId)) _queues[guildId].Clear();

                if (_audioClients.TryGetValue(guildId, out var ac))
                {
                    await ac.StopAsync();
                    ac.Dispose();
                    _audioClients.Remove(guildId);
                }

                _isPlaying[guildId] = false;
                _skipRequested[guildId] = true; // Interrompe o stream atual

                await msg.Channel.SendMessageAsync("⏹️ Fila limpa e bot desconectado.");
            }

            // ==========================================
            // COMANDO: !QUEUE (FILA)
            // ==========================================
            else if (content == "!queue" || content == "!fila")
            {
                if (!_queues.ContainsKey(guildId) || _queues[guildId].Count == 0)
                {
                    await msg.Channel.SendMessageAsync("A fila de músicas está vazia.");
                    return;
                }

                var list = _queues[guildId].Take(10).ToList();
                string txt = "**🎶 Fila Atual (Top 10):**\n\n";
                for (int i = 0; i < list.Count; i++)
                {
                    txt += $"`{i + 1}.` {list[i].Title}\n";
                }

                if (_queues[guildId].Count > 10)
                    txt += $"\n*...e mais {_queues[guildId].Count - 10} músicas.*";

                await msg.Channel.SendMessageAsync(txt);
            }
        }

        // ==========================================
        // MOTOR DO PLAYER E LOOP DA FILA
        // ==========================================
        private async Task StartPlaying(IVoiceChannel voiceChannel, ITextChannel textChannel)
        {
            var guildId = voiceChannel.GuildId;

            try
            {
                IAudioClient audioClient;
                if (!_audioClients.ContainsKey(guildId))
                {
                    Console.WriteLine($"[INFO] Tentando conectar ao canal de voz: {voiceChannel.Name}");
                    // Adicionamos o Wait() ou configuramos para garantir a conexão
                    audioClient = await voiceChannel.ConnectAsync();
                    _audioClients[guildId] = audioClient;
                    Console.WriteLine("[INFO] Conectado com sucesso!");
                }
                else
                {
                    audioClient = _audioClients[guildId];
                }

                _isPlaying[guildId] = true;

                while (_queues.ContainsKey(guildId) && _queues[guildId].Count > 0)
                {
                    var track = _queues[guildId].Dequeue();
                    _skipRequested[guildId] = false;

                    string finalUrl = track.Url;

                    if (track.IsSpotify)
                    {
                        var video = await _youtube.Search.GetVideosAsync(track.Title).FirstOrDefaultAsync();
                        if (video != null) finalUrl = video.Url;
                    }

                    if (!string.IsNullOrEmpty(finalUrl))
                    {
                        await textChannel.SendMessageAsync($"🎶 Tocando agora: **{track.Title}**");
                        await StreamAudio(guildId, finalUrl);
                    }

                    if (!_audioClients.ContainsKey(guildId)) break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO CRÍTICO NO PLAYER]: {ex.Message}");
                await textChannel.SendMessageAsync("❌ Erro ao tentar conectar ou tocar áudio. Verifique as permissões do bot.");
            }
            finally
            {
                _isPlaying[guildId] = false;
            }
        }

        private async Task StreamAudio(ulong guildId, string url)
        {
            try
            {
                var audioClient = _audioClients[guildId];
                var manifest = await _youtube.Videos.Streams.GetManifestAsync(url);
                var streamInfo = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                using var ffmpeg = CreateProcess(streamInfo.Url);
                using var output = ffmpeg.StandardOutput.BaseStream;
                using var discord = audioClient.CreatePCMStream(AudioApplication.Music);

                byte[] buffer = new byte[81920]; // Buffer maior para estabilidade
                int bytesRead;

                // O loop verifica '_skipRequested'. Se o cara der !skip, o loop quebra na hora e a música para.
                while (_isPlaying[guildId] && !_skipRequested[guildId] && (bytesRead = await output.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await discord.WriteAsync(buffer, 0, bytesRead);
                }

                await discord.FlushAsync();
                if (!ffmpeg.HasExited) ffmpeg.Kill(); // Garante que o processo não fique zumbi no PC
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FFmpeg Erro] {ex.Message}");
            }
        }

        // Configuração vitalícia do FFmpeg
        private Process CreateProcess(string url)
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel panic -reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5 -i \"{url}\" -ac 2 -f s16le -ar 48000 pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
        }

        // Extrai o ID puro de qualquer link do Spotify (ignorando parâmetros extras tipo ?si=)
        private string ExtractSpotifyId(string url)
        {
            try
            {
                var uri = new Uri(url);
                var lastSegment = uri.Segments.Last();
                return lastSegment.TrimEnd('/');
            }
            catch
            {
                return "";
            }
        }
    }

    public class MusicTrack
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Duration { get; set; }
        public bool IsSpotify { get; set; }
    }
}
