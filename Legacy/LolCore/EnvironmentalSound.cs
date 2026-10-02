// Sounds that belong to a place rather than to a moment: water running somewhere ahead, a monster
// growling two blocks away, the thing behind the wall.
//
// Transliterated from src/game/sound.mjs (snd_processEnvironmentalSoundEffectBase,
// snd_processEnvironmentalSoundEffect, snd_updateEnvironmentalSfx, snd_queueEnvironmentalSoundEffect,
// snd_playQueuedEffects).
//
// The whole point is the attenuation: how loud a sound is depends on how many blocks away it is and
// how many walls stand between, worked out by walking three steps towards it and halving the volume
// at each wall. Without this every sound in the game plays at full volume wherever it comes from.
namespace LolCore;

public sealed partial class MonsterBoard
{
    /// <summary>updateFlags bit 0: a panel is over the view, and the place makes no sound.</summary>
    public Func<int> UpdateFlagsNow;

    /// <summary>A sound to play, and how loud: (sound id, volume 0..240).</summary>
    public Action<int, int> OnEnvironmentalSound;

    /// <summary>Whether sound effects are wanted at all. The options screen turns them off.</summary>
    public bool SfxEnabled = true;

    /// <summary>The sound the place is making now, and at what volume.</summary>
    public int EnvironmentSfx;
    public int EnvironmentSfxVol;

    /// <summary>Beyond this many blocks a sound is not heard at all.</summary>
    public int EnvSfxDistThreshold = 3;

    /// <summary>
    /// Whether a spell is collecting its sounds instead of playing them as it goes, and the ones it
    /// has collected. A fireball passing through three blocks would otherwise play three growls at
    /// once; the engine queues them and plays them when the spell is done.
    /// </summary>
    public bool EnvSfxUseQueue;
    private readonly List<(int Sound, int Block)> _envSfxQueue = new();

    private static readonly int[] BlockShiftTable = { -32, -31, 1, 33, 32, 31, -1, -33 };

    /// <summary>
    /// sceneUpdateRequired: something in view has changed and the picture is owed a redraw. The
    /// engine sets it from its 28 checkSceneUpdateNeed calls and clears it when it draws; here a
    /// monster moving sets it, which is what those calls are about twelve times out of the
    /// twenty-eight, and DrawScene clears it.
    /// </summary>
    public bool SceneUpdateRequired;

    /// <summary>
    /// snd_processEnvironmentalSoundEffectBase: the sound already going plays, and this one becomes
    /// the place's sound - unless it is too far away to be heard.
    /// </summary>
    private bool ProcessEnvironmentalSoundEffectBase(int soundId, int block)
    {
        if (!SfxEnabled) return false;
        if (EnvironmentSfx != 0) OnEnvironmentalSound?.Invoke(EnvironmentSfx, EnvironmentSfxVol);
        int dist = 0;
        if (block != 0)
        {
            dist = GetBlockDistance(Party.Block, block);
            if (dist > EnvSfxDistThreshold)
            {
                EnvironmentSfx = 0;
                return false;
            }
        }
        EnvironmentSfx = soundId;
        EnvironmentSfxVol = (15 - (block != 0 || dist < 2 ? dist : 0)) << 4;
        return true;
    }

    /// <summary>
    /// snd_processEnvironmentalSoundEffect: and then quieter for every wall in the way. Walking three
    /// steps towards the block is the engine's own approximation of "is there anything between us".
    /// </summary>
    public bool ProcessEnvironmentalSoundEffect(int soundId, int block)
    {
        if (!ProcessEnvironmentalSoundEffectBase(soundId, block)) return false;
        if (block != Party.Block)
        {
            int cbl = Party.Block;
            for (int i = 3; i > 0; i -= 1)
            {
                int dir = CalcMonsterDirection(cbl & 0x1f, cbl >> 5, block & 0x1f, block >> 5);
                cbl = (cbl + BlockShiftTable[dir & 7]) & 0x3ff;
                if (cbl == block) break;
                if (TestWallFlag(cbl, 0, 1)) EnvironmentSfxVol >>= 1;
            }
        }
        // Passing 0 here is the engine playing what it just worked out and clearing the slot; the
        // recursion is one deep, because the second call comes in with soundId 0. It is skipped when
        // the view is about to be drawn again, because drawing it calls this anyway - playing now
        // would sound the same thing twice.
        if (soundId == 0 || SceneUpdateRequired) return false;
        return ProcessEnvironmentalSoundEffect(0, 0);
    }

    /// <summary>snd_updateEnvironmentalSfx: the sound of where the party is standing.</summary>
    public void UpdateEnvironmentalSfx(int soundId) => ProcessEnvironmentalSoundEffect(soundId, Party.Block);

    /// <summary>snd_queueEnvironmentalSoundEffect: play it now, or collect it if a spell is running.</summary>
    public void QueueEnvironmentalSoundEffect(int soundId, int block)
    {
        if (EnvSfxUseQueue && _envSfxQueue.Count < 10) _envSfxQueue.Add((soundId, block));
        else ProcessEnvironmentalSoundEffect(soundId, block);
    }

    /// <summary>snd_playQueuedEffects: everything the spell collected, in the order it happened.</summary>
    public void PlayQueuedEffects()
    {
        foreach (var (sound, block) in _envSfxQueue) ProcessEnvironmentalSoundEffect(sound, block);
        _envSfxQueue.Clear();
    }
}
