using MudBlazor;

namespace Flow.Client.Services;

/// <summary>
/// Тема MudBlazor, собранная из токенов Flow (wwwroot/css/tokens.css). Источник истины — токены:
/// цвет правится там, сюда переносится то же значение с пометкой, из какой переменной оно взято.
/// Форма контролов (pill-кнопки, круглые icon-кнопки, поля-подчёркивания) темой не задаётся —
/// она живёт в wwwroot/css/mud-overrides.css.
/// Приложение всегда тёмное: светлой палитры у Flow нет, поэтому PaletteLight не заполняется,
/// а MudThemeProvider поднимается с IsDarkMode="true" (App.razor).
/// </summary>
public static class FlowTheme
{
    private static readonly string[] Inter =
        ["Inter", "-apple-system", "BlinkMacSystemFont", "Segoe UI", "Roboto", "sans-serif"];

    public static readonly MudTheme Instance = new()
    {
        PaletteDark = new PaletteDark
        {
            // --accent / --on-accent: янтарь на тёмно-зелёном — единственный акцент интерфейса.
            Primary = "#FFBA00",
            PrimaryContrastText = "#0C3B2E",
            // --sage / --tan: вторые по частоте цвета Flow; семантику Mud (success/warning) закрываем ими,
            // чтобы стандартные компоненты не приносили чужую палитру.
            Secondary = "#6D9773",
            Tertiary = "#BB8A52",
            Success = "#6D9773",
            Warning = "#BB8A52",
            Info = "#6D9773",
            // --danger / --error
            Error = "#E2604F",

            // --bg-content / --surface / --surface-raised
            Background = "#153E32",
            Surface = "#1B4A3B",
            BackgroundGray = "#245A48",
            // --bg-deep: сайдбар темнее контента
            DrawerBackground = "#082019",
            DrawerText = "#FFFFFF",
            DrawerIcon = "rgba(255,255,255,0.68)",
            AppbarBackground = "#153E32",
            AppbarText = "#FFFFFF",

            // --text-primary / --text-secondary / --text-disabled
            TextPrimary = "#FFFFFF",
            TextSecondary = "rgba(255,255,255,0.68)",
            TextDisabled = "rgba(255,255,255,0.3)",
            ActionDefault = "rgba(255,255,255,0.68)",
            ActionDisabled = "rgba(255,255,255,0.3)",
            ActionDisabledBackground = "rgba(255,255,255,0.06)",

            // --border / --border-strong / --border-subtle
            Divider = "rgba(255,255,255,0.10)",
            DividerLight = "rgba(255,255,255,0.06)",
            LinesDefault = "rgba(255,255,255,0.10)",
            LinesInputs = "rgba(255,255,255,0.22)",
            TableLines = "rgba(255,255,255,0.10)",
            TableHover = "rgba(255,255,255,0.04)",
            TableStriped = "rgba(255,255,255,0.02)",

            OverlayDark = "rgba(8,32,25,0.6)"
        },

        // Inter 500/600/900 и шкала из tokens.css (--text-*). Roboto, который MudBlazor ждёт по умолчанию,
        // в проекте не подключён — шрифт задаётся здесь один раз для всех компонентов библиотеки.
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = Inter,
                FontSize = "14px",      // --text-sm
                FontWeight = "500",     // --fw-medium
                LineHeight = "1.33",    // --lh-normal
                LetterSpacing = "normal"
            },
            Body1 = new Body1Typography { FontFamily = Inter, FontSize = "15px", FontWeight = "500", LineHeight = "1.33" },
            Body2 = new Body2Typography { FontFamily = Inter, FontSize = "14px", FontWeight = "500", LineHeight = "1.33" },
            Caption = new CaptionTypography { FontFamily = Inter, FontSize = "12px", FontWeight = "500", LineHeight = "1.33" },
            Button = new ButtonTypography { FontFamily = Inter, FontSize = "14px", FontWeight = "600", LineHeight = "1.15", LetterSpacing = "normal", TextTransform = "none" },
            H1 = new H1Typography { FontFamily = Inter, FontSize = "40px", FontWeight = "900", LineHeight = "0.925", LetterSpacing = "-0.02em" },
            H2 = new H2Typography { FontFamily = Inter, FontSize = "25px", FontWeight = "900", LineHeight = "1.15", LetterSpacing = "-0.4px" },
            H3 = new H3Typography { FontFamily = Inter, FontSize = "22px", FontWeight = "800", LineHeight = "1.15", LetterSpacing = "-0.4px" },
            H4 = new H4Typography { FontFamily = Inter, FontSize = "20px", FontWeight = "800", LineHeight = "1.15" },
            H5 = new H5Typography { FontFamily = Inter, FontSize = "18px", FontWeight = "700", LineHeight = "1.15" },
            H6 = new H6Typography { FontFamily = Inter, FontSize = "16px", FontWeight = "700", LineHeight = "1.15" },
            Subtitle1 = new Subtitle1Typography { FontFamily = Inter, FontSize = "15px", FontWeight = "600", LineHeight = "1.33" },
            Subtitle2 = new Subtitle2Typography { FontFamily = Inter, FontSize = "14px", FontWeight = "600", LineHeight = "1.33" },
            Overline = new OverlineTypography { FontFamily = Inter, FontSize = "12px", FontWeight = "600", LineHeight = "1.33", LetterSpacing = "0.08em" }
        },

        LayoutProperties = new LayoutProperties
        {
            // --radius-md; pill-радиус кнопок и круглые icon-кнопки — в mud-overrides.css
            DefaultBorderRadius = "10px",
            DrawerWidthLeft = "260px"
        },

        Shadows = new Shadow()
    };
}
