// src/game/sound.mjs: sound effects and speech (sound_lol.cpp) on WebAudio. Music and AdLib sound effects go through
// the OPL3 emulator in an AudioWorklet (src/platform/adlib-worklet.mjs), fed like SoundPC_v1 v4.
// Host boundary: every decision (files, tracks, volumes, queues, timings) is kept literally; the WebAudio
// context, buffers, gain nodes and the worklet port are replaced by IAudioHost.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>What the page's AudioContext + AudioWorkletNode did. Every call is made on the engine's scheduler.</summary>
    public interface IAudioHost
    {
        /// <summary>An audio output exists (JS: an AudioContext could be made, with audioWorklet support).</summary>
        bool Available { get; }

        /// <summary>createBuffer + createBufferSource + gain(volume01) + start(). onEnded is the source's onended:
        /// the host must invoke it on the engine's scheduler (Scheduler.Run) once playback ends by itself.</summary>
        object PlaySamples(float[] samples, int sampleRate, double volume01, Action onEnded);

        /// <summary>source.stop()</summary>
        void Stop(object handle);

        /// <summary>adlibNode.port.postMessage(message): the worklet's load/play/halt/fade/stopAll/volume messages.</summary>
        void AdlibPost(AdlibMessage m);
    }

    /// <summary>The messages sound.mjs posts to the AdLib worklet ({ type, name, bytes, track, volume, music, sfx }).</summary>
    public sealed class AdlibMessage
    {
        public string type;
        public string name;
        public byte[] bytes;
        public int track, volume, music, sfx;
    }

    /// <summary>A TLK archive as main.mjs openTalkArchive returns it (RemotePak: has(name), async get(name)).</summary>
    public interface ITalkArchive
    {
        bool has(string name);
        Task<byte[]> get(string name);
    }

    /// <summary>decodeVoc's { sampleRate, samples }; also what vocBuffer caches (the JS AudioBuffer).</summary>
    public sealed class VocData
    {
        public int sampleRate;
        public float[] samples;
        /// <summary>AudioBuffer.duration (seconds)</summary>
        public double duration => (double)samples.Length / sampleRate;
    }

    /// <summary>playBuffer's AudioBufferSourceNode: the host handle plus its reassignable onended.</summary>
    public sealed class AudioSource
    {
        public object handle;
        public Action onended;
    }

    /// <summary>speechQueue entries: { archive, file }</summary>
    public sealed class SpeechQueueItem
    {
        public ITalkArchive archive;
        public string file;
    }

    /// <summary>startNextSpeech's token: { done, source }</summary>
    public sealed class SpeechToken
    {
        public bool done;
        public AudioSource source;
    }

    public sealed partial class LandsOfLore
    {
        public static VocData decodeVoc(byte[] bytes)
        {
            // JS reads past the end as undefined, which bitwise ops turn into 0.
            int B(int i) => i >= 0 && i < bytes.Length ? bytes[i] : 0;
            // Creative Voice File: 26-byte header, then typed blocks with 3-byte lengths.
            var sig = new StringBuilder();
            for (int i = 0; i < 19 && i < bytes.Length; i += 1) sig.Append((char)bytes[i]);
            if (sig.ToString() != "Creative Voice File") throw new Exception("Not a VOC file");
            int dataOffset = B(20) | (B(21) << 8);
            int p = dataOffset;
            int sampleRate = 11025;
            var chunks = new List<byte[]>();
            int total = 0;
            while (p < bytes.Length)
            {
                int type = B(p++);
                if (type == 0) break;
                int len = B(p) | (B(p + 1) << 8) | (B(p + 2) << 16);
                p += 3;
                if (type == 1)
                {
                    sampleRate = Js.Round(1000000.0 / (256 - B(p)));
                    var data = Js.Slice(bytes, p + 2, p + len);
                    chunks.Add(data);
                    total += data.Length;
                }
                else if (type == 9)
                {
                    sampleRate = B(p) | (B(p + 1) << 8) | (B(p + 2) << 16) | (B(p + 3) << 24);
                    var data = Js.Slice(bytes, p + 12, p + len);
                    chunks.Add(data);
                    total += data.Length;
                }
                else if (type == 3)
                {
                    int pause = B(p) | (B(p + 1) << 8);
                    var fill = new byte[pause];
                    Js.Fill(fill, (byte)0x80);
                    chunks.Add(fill);
                    total += pause;
                }
                p += len;
            }
            var samples = new float[total];
            int at = 0;
            foreach (var chunk in chunks)
            {
                for (int i = 0; i < chunk.Length; i += 1) samples[at++] = (chunk[i] - 128) / 128f;
            }
            return new VocData { sampleRate = sampleRate, samples = samples };
        }

        // ---- host wiring ----
        /// <summary>The page's audio output (AudioContext + worklet), set by the host.</summary>
        public IAudioHost audioHost;
        /// <summary>main.mjs engine.openTalkArchive: opens a TLK archive by name, null when missing.</summary>
        public Func<string, Task<ITalkArchive>> openTalkArchive;

        public IAudioHost audio;
        public bool sfxEnabled;
        public bool speechEnabledFlag;
        public bool textEnabledFlag;
        public int lastSfxTrack;
        public int environmentSfx;
        public int environmentSfxVol;
        public int envSfxDistThreshold;
        public bool envSfxUseQueue;
        public List<(int soundId, int block)> envSfxQueue;
        public string[] ingameSoundList;
        public int lastSpeechId, lastSpeaker, nextSpeechId, nextSpeaker;
        public List<SpeechQueueItem> speechQueue;
        public SpeechToken speechPlaying;
        public int speechLoading;
        public int curMusicTheme;
        public int lastMusicTrack;
        public bool musicEnabled;
        public int musicVolume;
        /// <summary>the worklet node: here the host it was first posted to (null until then)</summary>
        public object adlibNode;
        public Task adlibQueue;
        public bool adlibPlaying;
        public bool audioDisposed;
        public HashSet<AudioSource> liveSources;
        public int curMusicFileIndex;
        public int curMusicFileExt;
        public Dictionary<string, VocData> vocCache;
        public Dictionary<string, ITalkArchive> speechArchives;

        public void initSound()
        {
            audio = null;
            sfxEnabled = true;
            speechEnabledFlag = true;
            textEnabledFlag = true;
            lastSfxTrack = -1;
            environmentSfx = 0;
            environmentSfxVol = 0;
            envSfxDistThreshold = 2;
            envSfxUseQueue = false;
            envSfxQueue = new List<(int, int)>();
            ingameSoundList = (string[])@static.IngameSfxFiles.Clone();
            lastSpeechId = lastSpeaker = nextSpeechId = nextSpeaker = -1;
            speechQueue = new List<SpeechQueueItem>();
            speechPlaying = null;
            speechLoading = 0;
            curMusicTheme = -1;
            lastMusicTrack = -1;
            musicEnabled = true;
            musicVolume = 255;
            adlibNode = null;
            adlibQueue = Task.CompletedTask;
            adlibPlaying = false;
            audioDisposed = false;
            liveSources = new HashSet<AudioSource>();
            curMusicFileIndex = -1;
            curMusicFileExt = 0;
            vocCache = new Dictionary<string, VocData>();
            speechArchives = new Dictionary<string, ITalkArchive>();
        }

        // One AudioContext for the whole page. The host builds a fresh engine for every load, and a browser
        // allows only a handful of contexts before it refuses to make any more - at which point every sound
        // in the game stops. Sharing one means loading a save as often as you like costs nothing.
        // (C#: sharing is the host's business; audioHost is that shared output.)
        public IAudioHost audioContext()
        {
            if (audioDisposed) return null;
            if (audio != null) return audio;
            if (audioHost == null || !audioHost.Available) return null;
            audio = audioHost;
            return audio;
        }

        // Hand the shared context back before this engine is thrown away. The worklet node belongs to the
        // context, not to the engine, so an engine that does not let go of it leaves its music playing
        // under the next one's - which is what turned repeated loading into a wall of noise.
        public void snd_dispose()
        {
            audioDisposed = true;
            musicEnabled = false;
            try { if (adlibNode != null) ((IAudioHost)adlibNode).AdlibPost(new AdlibMessage { type = "halt" }); } catch (Exception) { /* the node is already gone */ }
            foreach (var source in liveSources ?? new HashSet<AudioSource>()) { try { audioHost?.Stop(source.handle); } catch (Exception) { /* already finished */ } }
            liveSources = new HashSet<AudioSource>();
            adlibNode = null;
            adlibQueue = Task.CompletedTask;
            audio = null;
        }

        public bool textEnabled()
        {
            return textEnabledFlag;
        }

        public bool speechEnabled()
        {
            return speechEnabledFlag && speechArchives.Count > 0 && audioContext() != null;
        }

        public VocData vocBuffer(string name, byte[] bytes)
        {
            var ctx = audioContext();
            if (ctx == null) return null;
            if (vocCache.TryGetValue(name, out var buffer) && buffer != null) return buffer;
            var voc = decodeVoc(bytes);
            buffer = voc;
            vocCache[name] = buffer;
            return buffer;
        }

        public AudioSource playBuffer(VocData buffer, int volume)
        {
            var ctx = audioContext();
            if (ctx == null || buffer == null) return null;
            // (ctx.resume() of a suspended context is the host's business)
            var source = new AudioSource();
            if (liveSources == null) liveSources = new HashSet<AudioSource>();
            liveSources.Add(source);
            source.onended = () => liveSources.Remove(source);
            source.handle = ctx.PlaySamples(buffer.samples, buffer.sampleRate, Math.Max(0, Math.Min(1, volume / 255.0)), () => source.onended?.Invoke());
            return source;
        }

        public void snd_playSoundEffect(int track, int volume)
        {
            if (track == 1 && (lastSfxTrack == -1 || lastSfxTrack == 1)) return;
            lastSfxTrack = track;
            if (track == -1 || track * 2 >= @static.IngameSfxIndex.Length || !sfxEnabled) return;
            volume &= 0xff;
            int[] volTable1 = { 223, 159, 95, 47, 15, 0 };
            int[] volTable2 = { 255, 191, 127, 63, 30, 0 };
            for (int i = 0; i < 6; i += 1)
            {
                if (volTable1[i] < volume)
                {
                    volume = volTable2[i];
                    break;
                }
            }
            int vocIndex = (short)@static.IngameSfxIndex[track * 2];
            string name = vocIndex == -1 ? null : ingameSoundList[vocIndex];
            if (!string.IsNullOrEmpty(name) && name.ToUpperInvariant() != "EMPTY")
            {
                string file = $"{name}.VOC";
                if (!res.exists(file)) return;
                try
                {
                    playBuffer(vocBuffer(file, res.get(file)), volume);
                }
                catch (Exception error)
                {
                    log($"sfx {file}: {error.Message}");
                }
                return;
            }
            // No sample: the effect is an AdLib track in the current music file.
            if (track == 168) track = 167;
            adlibPost(new AdlibMessage { type = "play", track = track, volume = volume });
        }

        public bool snd_processEnvironmentalSoundEffectBase(int soundId, int block)
        {
            if (!sfxEnabled) return false;
            if (environmentSfx != 0) snd_playSoundEffect(environmentSfx, environmentSfxVol);
            int dist = 0;
            if (block != 0)
            {
                dist = getBlockDistance(currentBlock, block);
                if (dist > envSfxDistThreshold)
                {
                    environmentSfx = 0;
                    return false;
                }
            }
            environmentSfx = soundId;
            environmentSfxVol = (15 - (block != 0 || dist < 2 ? dist : 0)) << 4;
            return true;
        }

        public bool snd_processEnvironmentalSoundEffect(int soundId, int block)
        {
            if (!snd_processEnvironmentalSoundEffectBase(soundId, block)) return false;
            if (block != currentBlock)
            {
                int[] blockShiftTable = { -32, -31, 1, 33, 32, 31, -1, -33 };
                int cbl = currentBlock;
                for (int i = 3; i > 0; i -= 1)
                {
                    int dir = calcMonsterDirection(cbl & 0x1f, cbl >> 5, block & 0x1f, block >> 5);
                    cbl = (cbl + blockShiftTable[dir]) & 0x3ff;
                    if (cbl == block) break;
                    if (testWallFlag(cbl, 0, 1)) environmentSfxVol >>= 1;
                }
            }
            if (soundId == 0 || sceneUpdateRequired) return false;
            return snd_processEnvironmentalSoundEffect(0, 0);
        }

        public void snd_updateEnvironmentalSfx(int soundId)
        {
            snd_processEnvironmentalSoundEffect(soundId, currentBlock);
        }

        public void snd_queueEnvironmentalSoundEffect(int soundId, int block)
        {
            if (envSfxUseQueue && envSfxQueue.Count < 10) envSfxQueue.Add((soundId, block));
            else snd_processEnvironmentalSoundEffect(soundId, block);
        }

        public void snd_playQueuedEffects()
        {
            foreach (var (soundId, block) in envSfxQueue) snd_processEnvironmentalSoundEffect(soundId, block);
            envSfxQueue = new List<(int, int)>();
        }

        // ---- music (AdLib worklet) ----
        // Every worklet command goes through one promise chain so load/play/halt keep their order even
        // though the module load and MUSIC.PAK fetch are asynchronous.
        public void adlibPost(AdlibMessage message)
        {
            var ctx = audioContext();
            if (ctx == null || audioDisposed) return;
            adlibQueue = adlibPostStep(adlibQueue, ctx, message);
        }

        // promise.then(async () => {...}).catch(log): runs after the previous step, in a microtask.
        async Task adlibPostStep(Task previous, IAudioHost ctx, AdlibMessage message)
        {
            if (previous.IsCompleted) await Task.Yield();
            else await previous;
            try
            {
                if (adlibNode == null)
                {
                    // (JS: audioWorklet.addModule + new AudioWorkletNode + gain; the worklet is host side)
                    adlibNode = ctx;
                    ctx.AdlibPost(new AdlibMessage { type = "volume", music = musicVolume, sfx = 255 });
                }
                if (message.type == "load" && !res.exists(message.name)) await res.loadPak("MUSIC.PAK");
                if (message.type == "load") message.bytes = (byte[])res.get(message.name).Clone();
                ctx.AdlibPost(message);
            }
            catch (QuitException) { throw; }
            catch (Exception error) { log($"adlib: {error.Message}"); }
        }

        // promise.then(() => new Promise((resolve) => setTimeout(resolve, ms)))
        async Task adlibWaitStep(Task previous, double ms)
        {
            if (previous.IsCompleted) await Task.Yield();
            else await previous;
            await sched.Sleep(ms);
        }

        public void setMusicVolume(int volume)
        {
            musicVolume = volume;
            adlibPost(new AdlibMessage { type = "volume", music = volume, sfx = 255 });
        }

        public void snd_loadSoundFile(int track)
        {
            if (!musicEnabled) return;
            int t = (track - 250) * 3;
            var map = @static.MusicTrackMap;
            // Asking for the file that is already loaded must change nothing. Stopping the music first and
            // then returning here left the game silent until the next level load - which is what happened
            // every time a monster script asked for its own level's music in the middle of a fight.
            if (t < 0 || (curMusicFileIndex == map[t] && curMusicFileExt == map[t + 1])) return;
            snd_stopMusic();
            snd_loadSoundFileByName($"LORE{map[t].ToString().PadLeft(2, '0')}{(char)map[t + 1]}");
            curMusicFileIndex = map[t];
            curMusicFileExt = map[t + 1];
        }

        public void snd_loadSoundFileByName(string name)
        {
            adlibPlaying = false;
            adlibPost(new AdlibMessage { type = "load", name = $"{name}.ADL" });
        }

        public int snd_playTrack(int track)
        {
            if (tim != null && tim.introMode)
            { // cinematics address tracks of the loaded file directly
                if (musicEnabled) { adlibPlaying = true; adlibPost(new AdlibMessage { type = "play", track = track, volume = 0xff }); }
                return lastMusicTrack;
            }
            if (track == -1) return lastMusicTrack;
            int res = lastMusicTrack;
            lastMusicTrack = track;
            if (musicEnabled)
            {
                snd_loadSoundFile(track);
                int t = (track - 250) * 3;
                if (t >= 0)
                {
                    adlibPlaying = true;
                    adlibPost(new AdlibMessage { type = "play", track = @static.MusicTrackMap[t + 2], volume = 0xff });
                }
            }
            return res;
        }

        public int snd_stopMusic()
        {
            if (musicEnabled)
            {
                if (adlibPlaying)
                {
                    adlibPlaying = false;
                    adlibPost(new AdlibMessage { type = "fade" });
                    // ScummVM waits 3 ticks for the fade-out track before halting; do it inside the queue.
                    adlibQueue = adlibWaitStep(adlibQueue, 3 * tickLength);
                }
                adlibPost(new AdlibMessage { type = "halt" });
            }
            return snd_playTrack(-1);
        }

        public void snd_voicePlay(string name, int volume)
        {
            string file = $"{name}.VOC";
            if (!res.exists(file)) return;
            try
            {
                playBuffer(vocBuffer(file, res.get(file)), volume);
            }
            catch (Exception error)
            {
                log($"voice {file}: {error.Message}");
            }
        }

        // ---- speech ----
        public async Task loadTalkArchive(string name)
        {
            if (speechArchives.ContainsKey(name) || openTalkArchive == null) return;
            var archive = await openTalkArchive(name);
            if (archive != null) speechArchives[name] = archive;
        }

        public ITalkArchive speechArchiveFor(int id)
        {
            string name = $"{((id & 0x4000) != 0 ? 0 : curTlkFile).ToString().PadLeft(2, '0')}.TLK";
            return speechArchives.TryGetValue(name, out var a) ? a : null;
        }

        public bool snd_playCharacterSpeech(int id, int speaker, int unused = 0)
        {
            if (!speechEnabled() || fastForward) return false;
            if (speaker < 65)
            {
                var c = speaker >= 0 && speaker < characters.Length ? characters[speaker] : null;
                if (c == null) log($"speech {id}: no party slot {speaker}");
                speaker = c != null && (c.flags & 1) != 0 ? (c.name.Length > 0 ? c.name[0] : 0) : 0;
            }
            if (lastSpeechId == id && speaker == lastSpeaker) return true;
            lastSpeechId = id;
            lastSpeaker = speaker;
            nextSpeechId = nextSpeaker = -1;
            var archive = speechArchiveFor(id);
            if (archive == null) return false;
            string ext = ((id & 0x4000) != 0 ? 0 : curTlkFile).ToString().PadLeft(2, '0');
            string sp = ((char)speaker).ToString();
            var files = new List<string>();
            if ((id & 0x4000) == 0 && id >= 1000)
            {
                string f = $"@{(id - 1000).ToString().PadLeft(4, '0')}{sp}.{ext}";
                if (archive.has(f)) files.Add(f);
            }
            else
            {
                string pattern = (id & 0x4000) != 0 ? (id & 0x3fff).ToString("X").PadLeft(3, '0') : id.ToString().PadLeft(3, '0');
                for (int i = 0; ; i += 1)
                {
                    string symbol = ((char)(48 + i)).ToString();
                    string f1 = $"{pattern}{sp}{symbol}.{ext}";
                    string f2 = $"{pattern}_{symbol}.{ext}";
                    if (archive.has(f1)) files.Add(f1);
                    else if (archive.has(f2)) files.Add(f2);
                    else break;
                }
            }
            if (files.Count == 0) return false;
            snd_stopSpeech(false);
            speechQueue = files.Select(f => new SpeechQueueItem { archive = archive, file = f }).ToList();
            activeVoiceFileTotalTime = 1;
            speechLoading += 1;
            tim.abortFlag = 0;
            _ = startNextSpeech();
            return true;
        }

        public async Task startNextSpeech()
        {
            if (speechPlaying != null || speechQueue.Count == 0)
            {
                speechLoading = Math.Max(0, speechLoading - 1);
                return;
            }
            var entry = speechQueue[0];
            speechQueue.RemoveAt(0);
            var archive = entry.archive;
            string file = entry.file;
            var token = new SpeechToken { done = false };
            speechPlaying = token;
            try
            {
                var bytes = await archive.get(file);
                if (speechPlaying != token) return;
                var buffer = vocBuffer(file, bytes);
                activeVoiceFileTotalTime = Js.Round(buffer.duration * 1000);
                var source = playBuffer(buffer, 255);
                token.source = source;
                // Wall-clock fallback in case the audio backend never reports the end of playback.
                var ended = new TaskCompletionSource<bool>();
                if (source == null) ended.TrySetResult(true);
                else
                {
                    source.onended = () => ended.TrySetResult(true);
                    _ = speechTimeout(ended, buffer.duration * 1000 + 250);
                }
                await ended.Task;
            }
            catch (QuitException) { throw; }
            catch (Exception error)
            {
                log($"speech {file}: {error.Message}");
            }
            finally
            {
                if (speechPlaying == token)
                {
                    speechPlaying = null;
                    speechLoading = Math.Max(0, speechLoading - 1);
                    if (speechQueue.Count > 0)
                    {
                        speechLoading += 1;
                        _ = startNextSpeech();
                    }
                }
            }
        }

        // setTimeout(resolve, ms)
        async Task speechTimeout(TaskCompletionSource<bool> ended, double ms)
        {
            await sched.Sleep(ms);
            ended.TrySetResult(true);
        }

        public int snd_updateCharacterSpeech()
        {
            if (speechPlaying != null || speechQueue.Count > 0 || speechLoading != 0) return 2;
            if (nextSpeechId != -1)
            {
                lastSpeechId = lastSpeaker = -1;
                activeVoiceFileTotalTime = 0;
                if (snd_playCharacterSpeech(nextSpeechId, nextSpeaker, 0)) return 2;
            }
            lastSpeechId = lastSpeaker = -1;
            activeVoiceFileTotalTime = 0;
            return 0;
        }

        public void snd_stopSpeech(bool setFlag)
        {
            if (speechPlaying == null && speechQueue.Count == 0) return;
            var token = speechPlaying;
            speechPlaying = null;
            speechQueue = new List<SpeechQueueItem>();
            speechLoading = 0;
            if (token != null && token.source != null)
            {
                try { token.source.onended = null; audioHost?.Stop(token.source.handle); } catch (Exception) { /* already stopped */ }
            }
            activeVoiceFileTotalTime = 0;
            nextSpeechId = nextSpeaker = -1;
            if (setFlag) tim.abortFlag = 1;
        }
    }
}
