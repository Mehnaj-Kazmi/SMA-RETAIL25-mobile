using Android.App;
using Android.Runtime;

namespace Retail25.Shopper;

// NetworkSecurityConfig points at Resources/xml/network_security_config.xml, which re-permits plain
// HTTP for private network ranges only. Without it the app cannot reach a development API over
// http:// at all, and the failure surfaces as a bare "connection cleartext not permitted".
[Application(NetworkSecurityConfig = "@xml/network_security_config")]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
