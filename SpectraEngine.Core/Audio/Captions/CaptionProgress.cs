namespace SpectraEngine.Core.Audio.Captions;

// How far one playing sound has come through its captions.
internal struct CaptionProgress
{
    // The sound's captions, or null when it has none that show.
    public SoundCaptions? Captions;

    // Which lookup of the feed's the captions are from. Zero before the first.
    public int Lookup;

    // Whether the sound was heard at the last look.
    public bool WasHeard;

    // Seconds into the sound at the last look, and which pass of its loop
    // that was. They mean nothing unless WasHeard.
    public double Seconds;
    public long Pass;
}
