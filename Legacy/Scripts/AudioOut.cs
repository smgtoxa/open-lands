// The game's sound, as src/game/sound.mjs routes it: AdLib music and effects from the Nuked OPL3
// port, VOC samples for the effects that have one, and the spoken lines. Everything is mixed here in
// OnAudioFilterRead, on Unity's audio thread; the engine talks to it from the main thread under a lock.
using System;
using System.Collections.Generic;
using LolCore;
using UnityEngine;
using Resources = LolCore.Resources;

public sealed class AudioOut : MonoBehaviour, ISound
{
    readonly object _lock = new object();
    Resources _res;
    AdlibPlayer _adlib;
    int _rate;
    float[] _left = new float[4096], _right = new float[4096];

    public bool MusicEnabled = true;
    public bool SfxEnabled = true;
    public float MusicVolume = 1f, SfxVolume = 1f, VoiceVolume = 1f;
    /// <summary>tim.introMode: cinematics address tracks of the loaded file directly.</summary>
    public Func<bool> IntroMode = () => false;

    int[] _sfxIndex, _musicMap;
    string[] _sfxFiles;
    int _lastSfxTrack = -1, _lastMusicTrack = -1;
    int _curFileIndex = -1, _curFileExt = -1;
    bool _adlibPlaying;
    readonly Dictionary<string, Voc.Sound> _vocCache = new Dictionary<string, Voc.Sound>();

    sealed class Voice { public float[] Samples; public double Pos, Step; public float Gain; }
    readonly List<Voice> _sfx = new List<Voice>();
    Voice _speech;

    public void Init(Resources res)
    {
        _res = res;
        _rate = AudioSettings.outputSampleRate;
        _sfxIndex = StaticData.Table("IngameSfxIndex");
        _sfxFiles = (string[])StaticData.Names("IngameSfxFiles").Clone();
        _musicMap = StaticData.Table("MusicTrackMap");
        lock (_lock)
        {
            _adlib = new AdlibPlayer(_rate);
            _adlib.Init();
        }
        // OnAudioFilterRead runs only behind a playing source: a looping silent clip is that source.
        var src = gameObject.GetComponent<AudioSource>();
        if (src == null) src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0;
        src.loop = true;
        src.clip = AudioClip.Create("silence", _rate, 2, _rate, false);
        src.Play();
    }

    /// <summary>A new game's own archives, and its own copy of the effect names (scripts rename them).</summary>
    public void Rebind(Resources res)
    {
        _res = res;
        _sfxFiles = (string[])StaticData.Names("IngameSfxFiles").Clone();
        _vocCache.Clear();
        _curFileIndex = _curFileExt = -1;
        _lastMusicTrack = _lastSfxTrack = -1;
        StopMusic();
        StopVoice();
    }

    /// <summary>assignCustomSfx: a script renames the sample behind an effect.</summary>
    public void AssignCustomSfx(int index, string name)
    {
        if (_sfxIndex == null || index > 250) return;
        int t = _sfxIndex[index << 1];
        if (t == 0xffff || t < 0 || t >= _sfxFiles.Length) return;
        _sfxFiles[t] = name;
    }

    // ---------------------------------------------------------------- music

    /// <summary>snd_loadSoundFile: the music file a track lives in, loaded only when it changes.</summary>
    public void LoadSoundFile(int track)
    {
        if (!MusicEnabled || _musicMap == null) return;
        int t = (track - 250) * 3;
        if (t < 0 || t + 2 >= _musicMap.Length || (_curFileIndex == _musicMap[t] && _curFileExt == _musicMap[t + 1])) return;
        StopMusic();
        LoadSoundFileByName($"LORE{_musicMap[t]:00}{(char)_musicMap[t + 1]}");
        _curFileIndex = _musicMap[t];
        _curFileExt = _musicMap[t + 1];
    }

    public void LoadSoundFileByName(string name)
    {
        if (_res == null) return;
        string file = name + ".ADL";
        if (!_res.Exists(file)) _res.LoadPak("MUSIC.PAK");
        if (!_res.Exists(file)) { Debug.LogWarning($"music: {file} not found"); return; }
        var bytes = _res.Get(file);
        _adlibPlaying = false;
        lock (_lock) _adlib.LoadFile(file, bytes);
    }

    /// <summary>snd_playTrack.</summary>
    public int PlayTrack(int track)
    {
        if (IntroMode())
        {
            if (MusicEnabled) { _adlibPlaying = true; lock (_lock) _adlib.Play(track, 0xff); }
            return _lastMusicTrack;
        }
        if (track == -1) return _lastMusicTrack;
        int previous = _lastMusicTrack;
        _lastMusicTrack = track;
        if (MusicEnabled)
        {
            LoadSoundFile(track);
            int t = (track - 250) * 3;
            if (t >= 0 && t + 2 < _musicMap.Length)
            {
                _adlibPlaying = true;
                lock (_lock) _adlib.Play(_musicMap[t + 2], 0xff);
            }
        }
        return previous;
    }

    /// <summary>snd_stopMusic.</summary>
    public void StopMusic()
    {
        if (!MusicEnabled) return;
        lock (_lock)
        {
            if (_adlibPlaying) _adlib.BeginFadeOut();
            _adlib.HaltTrack();
        }
        _adlibPlaying = false;
    }

    /// <summary>Settings turned the music off or on: silence it, or put the level's theme back.</summary>
    public void SetMusicEnabled(bool on, int theme)
    {
        if (on == MusicEnabled) return;
        if (!on) { StopMusic(); MusicEnabled = false; return; }
        MusicEnabled = true;
        _curFileIndex = _curFileExt = -1;
        _lastMusicTrack = -1;
        if (theme > 0) PlayTrack(theme);
    }

    // ---------------------------------------------------------------- effects

    static readonly int[] VolTable1 = { 223, 159, 95, 47, 15, 0 };
    static readonly int[] VolTable2 = { 255, 191, 127, 63, 30, 0 };

    /// <summary>snd_playSoundEffect: a VOC sample when the effect has one, else an AdLib track.</summary>
    public void PlaySoundEffect(int track, int volume)
    {
        if (_sfxIndex == null) return;
        if (track == 1 && (_lastSfxTrack == -1 || _lastSfxTrack == 1)) return;
        _lastSfxTrack = track;
        if (track == -1 || track * 2 >= _sfxIndex.Length || !SfxEnabled) return;
        volume &= 0xff;
        for (int i = 0; i < 6; i += 1)
            if (VolTable1[i] < volume) { volume = VolTable2[i]; break; }
        int vocIndex = (short)_sfxIndex[track * 2];
        string name = vocIndex < 0 || vocIndex >= _sfxFiles.Length ? null : _sfxFiles[vocIndex];
        if (!string.IsNullOrEmpty(name) && !name.Equals("EMPTY", StringComparison.OrdinalIgnoreCase))
        {
            var sound = Sample(name + ".VOC");
            if (sound != null) StartSample(sound, volume / 255f * SfxVolume, speech: false);
            return;
        }
        if (track == 168) track = 167;
        lock (_lock) _adlib.PlaySoundEffect(track, volume);
    }

    /// <summary>snd_voicePlay: a named VOC (a cutscene's own take).</summary>
    public void VoicePlay(string name, int volume)
    {
        var sound = Sample(name + ".VOC");
        if (sound != null) StartSample(sound, volume / 255f * SfxVolume, speech: false);
    }

    Voc.Sound Sample(string file)
    {
        if (_vocCache.TryGetValue(file, out var s)) return s;
        if (_res == null || !_res.Exists(file)) return null;
        try { s = Voc.Decode(_res.Get(file)); }
        catch (Exception e) { Debug.LogWarning($"sfx {file}: {e.Message}"); s = null; }
        _vocCache[file] = s;
        return s;
    }

    void StartSample(Voc.Sound sound, float gain, bool speech)
    {
        if (sound == null || sound.Samples.Length == 0 || _rate == 0) return;
        var v = new Voice { Samples = sound.Samples, Step = (double)sound.SampleRate / _rate, Gain = gain };
        lock (_lock)
        {
            if (speech) _speech = v;
            else { if (_sfx.Count > 24) _sfx.RemoveAt(0); _sfx.Add(v); }
        }
    }

    // ---------------------------------------------------------------- speech

    public void PlayVoice(Voc.Sound sound) => StartSample(sound, VoiceVolume, speech: true);
    public void StopVoice() { lock (_lock) _speech = null; }
    public bool VoicePlaying { get { lock (_lock) return _speech != null; } }

    // ---------------------------------------------------------------- mixing

    void OnAudioFilterRead(float[] data, int channels)
    {
        int frames = data.Length / channels;
        lock (_lock)
        {
            if (_adlib == null) return;
            if (_left.Length < frames) { _left = new float[frames]; _right = new float[frames]; }
            _adlib.Render(_left, _right, frames);
            float mv = MusicVolume * 2.5f;   // the worklet's gain
            for (int i = 0; i < frames; i += 1)
            {
                float l = _left[i] * mv, r = _right[i] * mv;
                for (int k = _sfx.Count - 1; k >= 0; k -= 1)
                {
                    var v = _sfx[k];
                    int at = (int)v.Pos;
                    if (at >= v.Samples.Length) { _sfx.RemoveAt(k); continue; }
                    float s = v.Samples[at] * v.Gain;
                    l += s; r += s;
                    v.Pos += v.Step;
                }
                if (_speech != null)
                {
                    int at = (int)_speech.Pos;
                    if (at >= _speech.Samples.Length) _speech = null;
                    else
                    {
                        float s = _speech.Samples[at] * _speech.Gain;
                        l += s; r += s;
                        _speech.Pos += _speech.Step;
                    }
                }
                l = l > 1 ? 1 : l < -1 ? -1 : l;
                r = r > 1 ? 1 : r < -1 ? -1 : r;
                if (channels == 1) data[i] = (l + r) * 0.5f;
                else { data[i * channels] = l; data[i * channels + 1] = r; }
            }
        }
    }
}
