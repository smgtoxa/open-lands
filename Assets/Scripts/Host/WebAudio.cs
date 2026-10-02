// The page's AudioContext for the engine (Lol.IAudioHost): buffer sources for the VOC samples, and
// the AdLib worklet (src/platform/adlib-worklet.mjs: OPL3 + Westwood driver, gain 2.5) - all mixed in
// OnAudioFilterRead on Unity's audio thread. The engine talks to it from the main thread (locked);
// a source's onended is handed back to the engine's event loop.
using System;
using System.Collections.Generic;
using Lol;
using Lol.Audio;
using UnityEngine;

namespace LolHost
{
    public sealed class WebAudio : MonoBehaviour, IAudioHost
    {
        const float Gain = 2.5f;   // the chip's raw output is quiet next to the VOC samples
        readonly object _lock = new object();
        AdlibPlayer _adlib;
        int _rate;
        float[] _left = new float[4096], _right = new float[4096];
        public Scheduler sched;
        /// <summary>Settings: the page's own volume knobs (1 = as the browser plays it).</summary>
        public float musicVolume = 1f, effectsVolume = 1f;

        sealed class Source { public float[] samples; public double pos, step; public float gain; public Action onEnded; public bool stopped; }
        readonly List<Source> _playing = new List<Source>();
        readonly List<Action> _ended = new List<Action>();

        public bool Available => _adlib != null;

        // test recording (Autopilot record:): the mixed output, interleaved, until the length is reached
        List<float> _rec;
        int _recFrames, _recChannels;
        string _recPath;

        public void Record(string path, float seconds)
        {
            lock (_lock) { _rec = new List<float>(); _recFrames = (int)(seconds * _rate); _recPath = path; }
        }

        void SaveRecording()
        {
            List<float> data; string path; int ch;
            lock (_lock) { data = _rec; path = _recPath; ch = _recChannels; _rec = null; }
            if (data == null) return;
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                int bytes = data.Count * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(_rate); w.Write(_rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var f in data) w.Write((short)Mathf.Clamp(f * 32767f, -32768, 32767));
            }
            Debug.Log($"audio recorded: {path}");
        }

        void Update()
        {
            bool full;
            lock (_lock) full = _rec != null && _rec.Count >= _recFrames * Math.Max(1, _recChannels);
            if (full) SaveRecording();
        }

        void Awake()
        {
            _rate = AudioSettings.outputSampleRate;
            _adlib = new AdlibPlayer(_rate);
            _adlib.Init();
            // OnAudioFilterRead runs only behind a playing source: a looping silent clip is that source.
            var src = gameObject.AddComponent<UnityEngine.AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0;
            src.loop = true;
            src.clip = AudioClip.Create("silence", _rate, 2, _rate, false);
            src.Play();
        }

        public object PlaySamples(float[] samples, int sampleRate, double volume01, Action onEnded)
        {
            var s = new Source { samples = samples ?? Array.Empty<float>(), step = (double)sampleRate / _rate, gain = (float)volume01, onEnded = onEnded };
            lock (_lock) _playing.Add(s);
            return s;
        }

        public void Stop(object handle)
        {
            if (!(handle is Source s)) return;
            lock (_lock)
            {
                if (s.stopped) return;
                s.stopped = true;
                _playing.Remove(s);
                // AudioBufferSourceNode.stop() still fires onended.
                if (s.onEnded != null) _ended.Add(s.onEnded);
            }
        }

        public void AdlibPost(AdlibMessage m)
        {
            lock (_lock)
            {
                switch (m.type)
                {
                    case "load": _adlib.LoadFile(m.name, m.bytes); break;
                    case "play": _adlib.Play(m.track, m.volume); break;
                    case "halt": _adlib.HaltTrack(); break;
                    case "fade": _adlib.BeginFadeOut(); break;
                    case "stopAll": _adlib.StopAll(); break;
                    case "volume": _adlib.SetVolume(m.music, m.sfx); break;
                }
            }
        }

        /// <summary>Main thread, once a frame: the sources that ended, back to the engine.</summary>
        public void DeliverEnded()
        {
            List<Action> ended;
            lock (_lock)
            {
                if (_ended.Count == 0) return;
                ended = new List<Action>(_ended);
                _ended.Clear();
            }
            foreach (var a in ended) sched.Run(a);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            int frames = data.Length / channels;
            lock (_lock)
            {
                if (_adlib == null) return;
                if (_left.Length < frames) { _left = new float[frames]; _right = new float[frames]; }
                Array.Clear(_left, 0, frames);
                Array.Clear(_right, 0, frames);
                _adlib.Render(_left, _right, frames);
                for (int i = 0; i < frames; i += 1)
                {
                    float l = Mathf.Clamp(_left[i] * Gain, -1, 1) * musicVolume, r = Mathf.Clamp(_right[i] * Gain, -1, 1) * musicVolume;
                    for (int k = _playing.Count - 1; k >= 0; k -= 1)
                    {
                        var s = _playing[k];
                        int at = (int)s.pos;
                        if (at >= s.samples.Length)
                        {
                            _playing.RemoveAt(k);
                            s.stopped = true;
                            if (s.onEnded != null) _ended.Add(s.onEnded);
                            continue;
                        }
                        float v = s.samples[at] * s.gain * effectsVolume;
                        l += v;
                        r += v;
                        s.pos += s.step;
                    }
                    l = Mathf.Clamp(l, -1, 1);
                    r = Mathf.Clamp(r, -1, 1);
                    if (channels == 1) data[i] = (l + r) * 0.5f;
                    else { data[i * channels] = l; data[i * channels + 1] = r; }
                    if (_rec != null && _rec.Count < _recFrames * 2) { _recChannels = 2; _rec.Add(l); _rec.Add(r); }
                }
            }
        }
    }
}
