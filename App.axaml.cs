using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Alexandreia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var db = new Db(Db.DefaultPath());

            // Alla prima apertura si segue il sistema; dopo, quello che ha scelto l'utente.
            if (db.Setting(Db.TemaKey) is { } tema)
                RequestedThemeVariant = tema == "scuro"
                    ? Avalonia.Styling.ThemeVariant.Dark
                    : Avalonia.Styling.ThemeVariant.Light;

            desktop.MainWindow = new MainWindow(db);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
