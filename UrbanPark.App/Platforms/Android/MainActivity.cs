using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace UrbanPark.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Window is null) return;

        var controlador = new WindowInsetsControllerCompat(Window, Window.DecorView);
        controlador.AppearanceLightStatusBars = false;
        controlador.AppearanceLightNavigationBars = false;

        if (Build.VERSION.SdkInt < BuildVersionCodes.VanillaIceCream)
        {
            var asfalto = Android.Graphics.Color.ParseColor("#0A0E13");
            Window.SetStatusBarColor(asfalto);
            Window.SetNavigationBarColor(asfalto);
        }
    }
}
