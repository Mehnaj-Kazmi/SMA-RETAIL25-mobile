using Android.Media;
using Android.OS;
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

    public void Accepted() => Play(Tone.PropBeep, 150, 40);

    public void Refused()
    {
        // Lower and longer than the accept tone, so the two are told apart by ear in a noisy aisle
        // rather than by paying attention. The longer buzz distinguishes it by feel as well.
        Play(Tone.SupError, 300, 150);
    }

    private static void Play(Tone tone, int milliseconds, int vibrateMs)
    {
        // Vibration first, because it is the half that cannot be silenced by a volume slider. A
        // handheld lives in a pocket or a holster between scans and spends its day in a shop where
        // somebody has invariably turned the sound down; a scan that gives no feedback at all is one
        // the shopper repeats, and a tag scanned twice is a support call about double-charging.
        Vibrate(vibrateMs);

        try
        {
            // Music, not Notification. Notification is the stream a handheld's owner mutes first —
            // and on this C72 it was audible in dumpsys and inaudible in the aisle. Scanner beeps
            // belong with media volume, which is the one people leave up.
            using var generator = new ToneGenerator(global::Android.Media.Stream.Music, (Volume)ScanVolume);
            generator.StartTone(tone, milliseconds);

            // StartTone is asynchronous and the generator is disposed at the end of this block. Torn
            // down immediately, the tone is cut off before it is audible — which is exactly what
            // "the beep is not happening" looked like on the handheld.
            Thread.Sleep(milliseconds + 60);
        }
        catch (Exception)
        {
            // A device with no tone generator, or audio in a state that refuses one. Sound is
            // feedback, not function: losing it must never cost the shopper their scan.
        }
    }

    private static void Vibrate(int milliseconds)
    {
        try
        {
            var context = global::Android.App.Application.Context;

            if (context.GetSystemService(global::Android.Content.Context.VibratorService) is not Vibrator vibrator
                || !vibrator.HasVibrator)
            {
                return;
            }

            vibrator.Vibrate(VibrationEffect.CreateOneShot(milliseconds, VibrationEffect.DefaultAmplitude));
        }
        catch (Exception)
        {
            // Same rule as the tone: feedback, not function.
        }
    }
}
