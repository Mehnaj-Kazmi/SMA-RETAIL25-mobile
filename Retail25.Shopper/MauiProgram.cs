using Microsoft.Extensions.Logging;

namespace Retail25.Shopper;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				// The app's own face: Encode Sans Expanded, an extended grotesque in the vein of
				// Helvetica Neue 63 Extended. That one is Linotype's and cannot be shipped inside an
				// APK without a licence; this is SIL Open Font Licence, so it can be, and the wide
				// letterforms are what the design is actually asking for.
				fonts.AddFont("EncodeSansExpanded-Regular.ttf", "SmaText");
				fonts.AddFont("EncodeSansExpanded-Medium.ttf", "SmaMedium");
				fonts.AddFont("EncodeSansExpanded-SemiBold.ttf", "SmaBold");

				// Kept because the template's own Styles.xaml names them on implicit styles. Removing
				// the files would leave those pointing at nothing.
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		StripEntryUnderline();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	/// <summary>
	/// Removes the Material underline Android draws under every text field.
	/// <para>
	/// The design puts each field in its own rounded card, and the platform's underline cuts a second
	/// line across the bottom of that card — two competing ideas of where the field's edge is. There
	/// is no cross-platform property for it, so it is tinted away on the native control.
	/// </para>
	/// </summary>
	private static void StripEntryUnderline()
	{
#if ANDROID
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(
			"Retail25.NoUnderline",
			(handler, _) => handler.PlatformView.BackgroundTintList =
				Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
#endif
	}
}
