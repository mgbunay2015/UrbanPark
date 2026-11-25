#if ANDROID
using Android.App;
using Android.Content;
using Microsoft.Maui.Authentication;

namespace UrbanPark.App.Platforms.Android;

/// <summary>
/// Recibe la redirección de Google Sign-In en Android.
/// El esquema debe coincidir con GoogleAuthServicio.Esquema y con el URI registrado en Google Cloud.
/// </summary>
[Activity(NoHistory = true, LaunchMode = global::Android.Content.PM.LaunchMode.SingleTop, Exported = true)]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "com.urbanpark.app")]
public class CallbackActivity : WebAuthenticatorCallbackActivity
{
}
#endif
