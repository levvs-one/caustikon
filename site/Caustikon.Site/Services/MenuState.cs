using Microsoft.JSInterop;

namespace Caustikon.Site.Services;

/// <summary>Which header menu is open. Opening one closes the other; a click outside any menu closes both.</summary>
public static class MenuState
{
    private static string? open;

    public static event Action? Changed;

    public static bool IsOpen(string id) => open == id;

    public static void Toggle(string id)
    {
        open = open == id ? null : id;
        Changed?.Invoke();
    }

    public static void Close()
    {
        if (open is null)
        {
            return;
        }

        open = null;
        Changed?.Invoke();
    }

    [JSInvokable("CloseMenus")]
    public static void CloseFromPage() => Close();
}
