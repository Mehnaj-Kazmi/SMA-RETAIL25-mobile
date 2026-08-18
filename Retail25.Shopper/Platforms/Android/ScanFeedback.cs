using Android.Media;
using Retail25.Shopper.Services;

namespace Retail25.Shopper.Platforms.Android;

/// <summary>
/// The noise a scanner makes, because a shop is loud and nobody watches the screen while scanning.
/// <para>
/// Two distinct tones rather than one: a short high beep when items went on the bill, and a lower
/// double buzz when the shop refused the tag. A single sound would confirm only that the trigger was
/// pressed, which is the one thing the shopper already knows — they pressed it. The point is to tell
/// them, without looking, whether the item is paid for or still their problem at the exit gate.
/// </para>
/// <para>
/// ToneGenerator rather than a bundled audio file: it is a system tone, it respects the device's
/// notification volume, and it needs no asset, no MediaPlayer lifecycle, and no permission.
/// </para>
/// </summary>
public sealed class ScanFeedback : IScanFeedback
{
    /// <summary>
    /// Loud, because this competes with a shop floor. Not maximum: a handheld held near the ear
    /// while reading small print should not be painful.
    /// </summary>
    private const int ScanVolume = 80;

    public void Accepted() => Play(Tone.PropBeep, 120);

    public void Refused()
    {
        // Lower and longer than the accept tone, so the two are told apart by ear in a noisy aisle
        // rather than by paying attention.
        Play(Tone.SupError, 250);
    }

    private static void Play(Tone tone, int milliseconds)
    {
        try
        {
            // Constructed per beep and disposed with it. A long-lived ToneGenerator holds an
            // AudioTrack open for the life of the app, which on some devices blocks other audio and
            // on others is silently reclaimed — leaving a scanner that has quietly stopped beeping.
            using var generator = new ToneGenerator(global::Android.Media.Stream.Notification, (Volume)ScanVolume);
            generator.StartTone(tone, milliseconds);
        }
        catch (Exception)
        {
            // A device with no tone generator, or audio in a state that refuses one. Sound is
            // feedback, not function: losing it must never cost the shopper their scan.
        }
    }
}
