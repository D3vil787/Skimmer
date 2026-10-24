#nullable enable
using System.Drawing;
using System.Runtime.InteropServices;

namespace Skimmer.Ui;

/// <summary>
/// Оформление того, что рисует не WinForms, а сама Windows: заголовок окна через
/// DWM, тёмные полосы прокрутки нативных контролов и двойная буферизация дерева.
/// Любая ошибка — тихий отказ: окно продолжает работать с системным оформлением.
/// </summary>
/// <remarks>
/// Поведение проверено спайком <c>spikes\ThemeSpike\RESULT.md</c>: атрибут 20
/// работает на Windows 10 19045, атрибуты 35 и 36 там возвращают
/// <c>E_INVALIDARG</c>, поэтому они вызываются только на Windows 11.
/// </remarks>
public static class ThemeNative
{
    /// <summary>Тёмный режим неклиентской области окна (Windows 10 1809+).</summary>
    private const int UseImmersiveDarkMode = 20;

    /// <summary>Цвет заголовка окна (Windows 11 22000+).</summary>
    private const int CaptionColor = 35;

    /// <summary>Цвет текста заголовка окна (Windows 11 22000+).</summary>
    private const int CaptionTextColor = 36;

    /// <summary>Сборка Windows 11, с которой доступны атрибуты цвета заголовка.</summary>
    private const int Windows11Build = 22000;

    /// <summary>Сообщение «использовать этот шрифт» нативному контролу.</summary>
    private const int SetFontMessage = 0x0030;

    private const int TreeViewFirstMessage = 0x1100;
    private const int SetExtendedStyleMessage = TreeViewFirstMessage + 44;
    private const int DoubleBufferStyle = 0x0004;

    /// <summary>Режимы недокументированного <c>uxtheme!SetPreferredAppMode</c>.</summary>
    private const int AppModeAllowDark = 1;
    private const int AppModeForceLight = 3;

    /// <summary>Флаги <c>SetWindowPos</c> для перерисовки рамки без движения окна.</summary>
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>Флаги <c>RedrawWindow</c> для немедленной перерисовки рамки.</summary>
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;

    /// <summary>Перерисовка неклиентской области без смены реальной активности окна.</summary>
    private const int WmNcActivate = 0x0086;

    /// <summary>Система — Windows 11 или новее.</summary>
    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= Windows11Build;

    /// <summary>
    /// Разрешает приложению тёмные нативные элементы. Вызывается один раз в начале
    /// <c>Main</c>: без этого класс темы <c>DarkMode_Explorer</c> не включает тёмные
    /// полосы прокрутки (проверено спайком).
    /// </summary>
    public static void AllowDarkMode(bool allow)
    {
        try
        {
            SetPreferredAppMode(allow ? AppModeAllowDark : AppModeForceLight);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Сборка Windows без этих ordinal'ов: остаются системные цвета.
        }
    }

    /// <summary>
    /// Приводит заголовок окна в соответствие с темой. На Windows 10 — только
    /// светлый или тёмный режим, на Windows 11 — ещё и цвет заголовка и текста.
    /// </summary>
    public static void ApplyWindowChrome(IntPtr handle, ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        TrySetAttribute(handle, UseImmersiveDarkMode, theme.IsDark ? 1 : 0);
        if (IsWindows11)
        {
            TrySetAttribute(handle, CaptionColor, ToColorRef(theme.Palette.ChromeBack));
            TrySetAttribute(handle, CaptionTextColor, ToColorRef(theme.Palette.Fore));
        }

        RedrawFrame(handle);
    }

    /// <summary>
    /// Заставляет DWM перерисовать заголовок немедленно. Без этого на Windows 10
    /// заголовок остаётся в прежней теме до следующей активации окна: атрибут уже
    /// новый, а нарисованная рамка — старая (оговорка спайка к пункту 2).
    /// </summary>
    /// <remarks>
    /// Фокус не затрагивается: <c>SetWindowPos</c> идёт с <c>SWP_NOACTIVATE</c>, а
    /// пара <c>WM_NCACTIVATE</c> только просит перерисовать неклиентскую область и
    /// не меняет настоящую активность окна. Это важно для открытых RDP-, VNC- и
    /// MMC-сессий, у которых клавиатура внутри дочернего окна.
    /// </remarks>
    private static void RedrawFrame(IntPtr handle)
    {
        try
        {
            _ = SetWindowPos(
                handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            _ = RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, RdwFrame | RdwInvalidate | RdwUpdateNow);

            // Перерисовка «как при смене активности»: DWM меняет цвет заголовка
            // только по этому сообщению, а восстановление прежнего состояния сразу
            // следом не даёт мигнуть неактивным заголовком.
            bool active = GetForegroundWindow() == handle;
            _ = SendMessage(handle, WmNcActivate, (IntPtr)(active ? 0 : 1), IntPtr.Zero);
            _ = SendMessage(handle, WmNcActivate, (IntPtr)(active ? 1 : 0), IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Заголовок перерисуется при следующей активации окна.
        }
    }

    /// <summary>
    /// Переводит полосы прокрутки и рамки контрола на тёмную или светлую тему
    /// проводника. Нужна деревьям, спискам и многострочным полям ввода.
    /// </summary>
    public static void ApplyExplorerTheme(IntPtr handle, bool dark)
    {
        if (handle != IntPtr.Zero)
        {
            TrySetWindowTheme(handle, dark ? "DarkMode_Explorer" : "Explorer", null);
        }
    }

    /// <summary>
    /// Снимает визуальные стили с контрола. Нужно полосе вкладок: иначе uxtheme
    /// рисует свой серый фон справа от последней вкладки.
    /// </summary>
    public static void RemoveVisualStyles(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            TrySetWindowTheme(handle, string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// Передаёт контролу шрифт окна (<c>WM_SETFONT</c>). Нужно контролу, который
    /// рисует себя сам (<c>ControlStyles.UserPaint</c>): WinForms такому контролу
    /// шрифт не передаёт, а нативная полоса вкладок всё равно меряет этим шрифтом
    /// ширину вкладок.
    /// </summary>
    /// <param name="handle">Дескриптор контрола.</param>
    /// <param name="font">Дескриптор GDI-шрифта; им владеет вызывающая сторона.</param>
    public static void SetWindowFont(IntPtr handle, IntPtr font)
    {
        if (handle == IntPtr.Zero || font == IntPtr.Zero)
        {
            return;
        }

        try
        {
            SendMessage(handle, SetFontMessage, font, (IntPtr)1);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Контрол останется на системном шрифте.
        }
    }

    /// <summary>Освобождает GDI-объект — например, шрифт из <see cref="SetWindowFont"/>.</summary>
    public static void DeleteGdiObject(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = DeleteObject(handle);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Объект освободится вместе с процессом.
        }
    }

    /// <summary>Включает <c>TVS_EX_DOUBLEBUFFER</c> у нативного дерева.</summary>
    public static void EnableTreeDoubleBuffer(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            SendMessage(handle, SetExtendedStyleMessage, (IntPtr)DoubleBufferStyle, (IntPtr)DoubleBufferStyle);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Без двойной буферизации дерево просто чуть заметнее мерцает.
        }
    }

    /// <summary>COLORREF (0x00BBGGRR) для DWM-атрибутов цвета.</summary>
    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    private static void TrySetAttribute(IntPtr handle, int attribute, int value)
    {
        try
        {
            // HRESULT намеренно игнорируется: на Windows 10 атрибуты 35 и 36
            // отвечают E_INVALIDARG, и это штатный отказ, а не ошибка.
            _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Окно остаётся с системным заголовком.
        }
    }

    private static void TrySetWindowTheme(IntPtr handle, string? subAppName, string? subIdList)
    {
        try
        {
            _ = SetWindowTheme(handle, subAppName, subIdList);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Контрол остаётся с системной темой.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr handle, string? subAppName, string? subIdList);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(
        IntPtr handle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr handle,
        IntPtr updateRectangle,
        IntPtr updateRegion,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
