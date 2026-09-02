using MudBlazor;

namespace MesDataManager.Web.Theming;

/// <summary>
/// Tema dell'applicazione. Le scelte rispondono al contesto d'uso: postazioni di reparto e
/// scrivanie di stabilimento, luce variabile, tabelle dense da leggere di fretca.
/// <list type="bullet">
///   <item>Neutri freddi, dal grigio alluminio all'antracite: non affaticano su turni lunghi
///   e lasciano che siano i dati a dare colore.</item>
///   <item>Un solo accento interattivo (blu acciaio) e un solo accento di segnalazione (ambra),
///   riservato agli stati bloccati dall'ERP: se l'ambra compare, significa sempre la stessa cosa.</item>
///   <item>IBM Plex Sans per il testo e IBM Plex Mono per codici e identificativi, dove
///   distinguere zero da O e uno da I conta davvero.</item>
/// </list>
/// </summary>
public static class MesTheme
{
    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#3E7CA6",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#2E3A45",
            Tertiary = "#4E6472",
            Background = "#F2F4F5",
            Surface = "#FFFFFF",
            AppbarBackground = "#232C34",
            AppbarText = "#EDF0F2",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#1B2126",
            DrawerIcon = "#4E6472",
            TextPrimary = "#14181C",
            TextSecondary = "#5A6B77",
            Divider = "#DDE1E4",
            TableLines = "#E4E8EA",
            TableHover = "#EAF1F6",
            TableStriped = "#F7F9FA",
            Warning = "#C4761A",
            Success = "#2F7D5B",
            Error = "#B3372C",
            Info = "#3E7CA6",
            ActionDefault = "#4E6472",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#6FA8CC",
            Secondary = "#9FB2BF",
            Background = "#14181C",
            Surface = "#1C232A",
            AppbarBackground = "#111519",
            DrawerBackground = "#1C232A",
            TextPrimary = "#E6EAED",
            TextSecondary = "#9FB2BF",
            Divider = "#2C353D",
            TableLines = "#2C353D",
            TableHover = "#232D35",
            Warning = "#E0A052",
            Success = "#5CB08B",
            Error = "#E27469",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "system-ui", "sans-serif"],
                FontSize = "0.875rem",
                LineHeight = "1.5",
            },
            H5 = new H5Typography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "system-ui", "sans-serif"],
                FontSize = "1.25rem",
                FontWeight = "600",
                LetterSpacing = "-0.01em",
            },
            H6 = new H6Typography
            {
                FontFamily = ["IBM Plex Sans", "Segoe UI", "system-ui", "sans-serif"],
                FontSize = "1rem",
                FontWeight = "600",
            },
            Subtitle2 = new Subtitle2Typography
            {
                FontSize = "0.75rem",
                FontWeight = "600",
                LineHeight = "1.4",
            },
            Body2 = new Body2Typography
            {
                FontSize = "0.8125rem",
                LineHeight = "1.45",
            },
            Button = new ButtonTypography
            {
                FontWeight = "600",
                TextTransform = "none",
            },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "3px",
            DrawerWidthLeft = "300px",
        },
    };
}
