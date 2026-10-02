// The spoken lines: which take of which line is played, in what order.
//
// Transliterated from the speech half of src/game/sound.mjs (loadTalkFile, speechArchiveFor,
// snd_playCharacterSpeech, snd_updateCharacterSpeech, snd_stopSpeech). Playing a sound is the
// host's job; what belongs to the engine is the bookkeeping around it - which .TLK archive is open,
// how a line id and a speaker turn into file names, that a line is made of several takes played
// one after another, and how long the game thinks the current one lasts, because the conversation
// waits on exactly that.
namespace LolCore;

public sealed class Speech
{
    /// <summary>Opens <c>NN.TLK</c>; the host provides it, because these are the one kind of file
    /// that is streamed off the disc rather than held in memory.</summary>
    public Func<string, PakArchive> OpenArchive;

    /// <summary>Whether the game is set to speak its lines at all.</summary>
    public bool Enabled;

    /// <summary>Set while the game is skipping ahead: no speech is started.</summary>
    public bool FastForward;

    private readonly Dictionary<string, PakArchive> _archives = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which level's archive is open, as loadTalkFile chose it.</summary>
    public int CurTlkFile = -1;

    public int LastSpeechId = -1, LastSpeaker = -1;
    public int NextSpeechId = -1, NextSpeaker = -1;

    /// <summary>The takes still to play, in order, as file names inside the open archive.</summary>
    public readonly List<string> Queue = new();

    private PakArchive _queueArchive;

    /// <summary>The take playing now, or null.</summary>
    public string Playing;

    /// <summary>How long the game believes the current line lasts, in milliseconds. It is 1 while a
    /// take is still being fetched, which is what keeps a conversation waiting.</summary>
    public int ActiveVoiceFileTotalTime;

    /// <summary>Set by the host: whether the sound it was handed is still coming out of the speakers.</summary>
    public Func<bool> HostPlaying;

    /// <summary>loadTalkFile: a level's own archive replaces the last one.</summary>
    public void LoadTalkFile(int index)
    {
        if (index == CurTlkFile) return;
        if (CurTlkFile > 0 && index > 0) _archives.Remove($"{CurTlkFile:00}.TLK");
        if (index > 0) CurTlkFile = index;
        LoadTalkArchive($"{index:00}.TLK");
    }

    public void LoadTalkArchive(string name)
    {
        if (_archives.ContainsKey(name) || OpenArchive == null) return;
        var archive = OpenArchive(name);
        if (archive != null) _archives[name] = archive;
    }

    /// <summary>speechArchiveFor: line ids with 0x4000 set live in the common archive (00.TLK).</summary>
    public PakArchive ArchiveFor(int id)
        => _archives.GetValueOrDefault($"{((id & 0x4000) != 0 ? 0 : CurTlkFile):00}.TLK");

    /// <summary>
    /// The takes a line is made of, in the order they are played. A line is either one file named
    /// after the line number and the speaker, or a run of them ending where the next one is missing.
    /// </summary>
    public List<string> FilesFor(int id, int speaker)
    {
        var files = new List<string>();
        var archive = ArchiveFor(id);
        if (archive == null) return files;
        string ext = $"{((id & 0x4000) != 0 ? 0 : CurTlkFile):00}";
        char sp = (char)speaker;
        if ((id & 0x4000) == 0 && id >= 1000)
        {
            string f = $"@{id - 1000:0000}{sp}.{ext}";
            if (archive.Has(f)) files.Add(f);
            return files;
        }
        string pattern = (id & 0x4000) != 0 ? (id & 0x3fff).ToString("X").PadLeft(3, '0') : id.ToString().PadLeft(3, '0');
        for (int i = 0; ; i += 1)
        {
            char symbol = (char)(48 + i);
            string f1 = $"{pattern}{sp}{symbol}.{ext}";
            string f2 = $"{pattern}_{symbol}.{ext}";
            if (archive.Has(f1)) files.Add(f1);
            else if (archive.Has(f2)) files.Add(f2);
            else break;
        }
        return files;
    }

    /// <summary>
    /// snd_playCharacterSpeech: a party slot is turned into the letter the files are named after,
    /// and the line's takes are queued. False when there is nothing to play - which is what makes
    /// the conversation fall back to text.
    /// </summary>
    public bool PlayCharacterSpeech(int id, int speaker, Character[] party)
    {
        if (!Enabled || FastForward) return false;
        if (speaker < 65)
        {
            var c = party != null && speaker >= 0 && speaker < party.Length ? party[speaker] : null;
            speaker = c != null && c.Active && c.Name.Length != 0 ? c.Name[0] : 0;
        }
        if (LastSpeechId == id && LastSpeaker == speaker) return true;
        LastSpeechId = id;
        LastSpeaker = speaker;
        NextSpeechId = NextSpeaker = -1;
        var files = FilesFor(id, speaker);
        if (files.Count == 0) return false;
        Stop(false);
        _queueArchive = ArchiveFor(id);
        Queue.Clear();
        Queue.AddRange(files);
        ActiveVoiceFileTotalTime = 1;
        StartNext();
        return true;
    }

    /// <summary>Hands the next take to the host and remembers how long it runs.</summary>
    public void StartNext()
    {
        if (Playing != null || Queue.Count == 0) return;
        string file = Queue[0];
        Queue.RemoveAt(0);
        Playing = file;
        var archive = _queueArchive;
        if (archive == null || !archive.Has(file)) { Playing = null; return; }
        var sound = Voc.Decode(archive.Get(file));
        ActiveVoiceFileTotalTime = sound.SampleRate > 0
            ? JsMath.RoundToInt(sound.Samples.Length * 1000.0 / sound.SampleRate)
            : 0;
        Play?.Invoke(sound);
    }

    /// <summary>Set by the host: play this decoded take.</summary>
    public Action<Voc.Sound> Play;

    /// <summary>Set by the host: stop whatever is playing now.</summary>
    public Action StopHost;

    /// <summary>Called when the host's sound has finished: the next take follows straight on.</summary>
    public void Finished()
    {
        Playing = null;
        if (Queue.Count != 0) StartNext();
    }

    /// <summary>
    /// snd_updateCharacterSpeech: 2 while a line is still being spoken, 0 once it is over - which is
    /// the answer the dialogue waits on before it moves to the next line.
    /// </summary>
    public int UpdateCharacterSpeech(Character[] party)
    {
        if (Playing != null || Queue.Count != 0 || (HostPlaying?.Invoke() ?? false)) return 2;
        if (NextSpeechId != -1)
        {
            LastSpeechId = LastSpeaker = -1;
            ActiveVoiceFileTotalTime = 0;
            if (PlayCharacterSpeech(NextSpeechId, NextSpeaker, party)) return 2;
        }
        LastSpeechId = LastSpeaker = -1;
        ActiveVoiceFileTotalTime = 0;
        return 0;
    }

    public void Stop(bool setFlag)
    {
        if (Playing == null && Queue.Count == 0) return;
        Playing = null;
        Queue.Clear();
        StopHost?.Invoke();
        if (setFlag) ActiveVoiceFileTotalTime = 0;
    }
}
