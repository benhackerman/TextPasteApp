using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TextPasteApp.Native;

/// <summary>Pega un texto en la ventana que tenía el foco: lo pone en el portapapeles y simula Ctrl+V.</summary>
internal static class Paster
{
    /// <summary>
    /// Pega el texto. Si restoreClipboard es true, después devuelve al portapapeles el texto que había antes.
    /// </summary>
    public static async Task PasteAsync(string text, IntPtr targetWindow, bool restoreClipboard)
    {
        var previous = restoreClipboard ? ClipboardHelper.TryGetText() : null;

        if (!ClipboardHelper.TrySetText(text)) return;

        if (targetWindow != IntPtr.Zero && Win32.IsWindow(targetWindow) && Win32.GetForegroundWindow() != targetWindow)
        {
            Win32.SetForegroundWindow(targetWindow);
            await Task.Delay(60);
        }

        await WaitForModifiersReleasedAsync();
        SendCtrlV();

        if (previous != null && previous != text)
        {
            // La app de destino lee el portapapeles de forma asíncrona; le damos tiempo antes de restaurar.
            await Task.Delay(400);
            ClipboardHelper.TrySetText(previous);
        }
    }

    /// <summary>
    /// Pega varios valores seguidos con Tab entre ellos, para llenar un formulario de una pasada.
    /// Un valor vacío no pega nada pero igual avanza con Tab (así cada valor cae en su campo).
    /// </summary>
    public static async Task PasteSequenceAsync(System.Collections.Generic.IReadOnlyList<string> values, IntPtr targetWindow,
        bool restoreClipboard, int separatorVk = Win32.VK_TAB, int delayMs = 120)
    {
        delayMs = Math.Clamp(delayMs, 30, 2000);
        if (values.Count == 0) return;
        var previous = restoreClipboard ? ClipboardHelper.TryGetText() : null;

        if (targetWindow != IntPtr.Zero && Win32.IsWindow(targetWindow) && Win32.GetForegroundWindow() != targetWindow)
        {
            Win32.SetForegroundWindow(targetWindow);
            await Task.Delay(60);
        }
        await WaitForModifiersReleasedAsync();

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i].Length > 0 && ClipboardHelper.TrySetText(values[i]))
            {
                SendCtrlV();
                await Task.Delay(delayMs); // que la app alcance a leer el portapapeles antes del siguiente
            }
            if (i < values.Count - 1)
            {
                Send(Key(separatorVk, down: true), Key(separatorVk, down: false));
                await Task.Delay(Math.Max(40, delayMs * 2 / 3));
            }
        }

        if (previous != null)
        {
            await Task.Delay(400);
            ClipboardHelper.TrySetText(previous);
        }
    }

    /// <summary>Si el usuario todavía tiene Shift o Alt presionado, Ctrl+V se convertiría en otra cosa.</summary>
    private static async Task WaitForModifiersReleasedAsync()
    {
        for (var i = 0; i < 60; i++)
        {
            if (!Win32.IsKeyDown(Win32.VK_CONTROL) && !Win32.IsKeyDown(Win32.VK_SHIFT) &&
                !Win32.IsKeyDown(Win32.VK_MENU) && !Win32.IsKeyDown(Win32.VK_LWIN) && !Win32.IsKeyDown(Win32.VK_RWIN))
                return;
            await Task.Delay(15);
        }
    }

    private static void SendCtrlV() => Send(
        Key(Win32.VK_CONTROL, down: true),
        Key(Win32.VK_V, down: true),
        Key(Win32.VK_V, down: false),
        Key(Win32.VK_CONTROL, down: false));

    /// <summary>Presiona y suelta una tecla sin uso, para que soltar Alt/Win no active menús ni el menú Inicio.</summary>
    public static void SendDummyKey() => Send(
        Key(Win32.VK_UNASSIGNED, down: true),
        Key(Win32.VK_UNASSIGNED, down: false));

    private static void Send(params Win32.INPUT[] inputs) =>
        Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());

    private static Win32.INPUT Key(int vk, bool down) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        U = new Win32.InputUnion
        {
            ki = new Win32.KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = down ? 0 : Win32.KEYEVENTF_KEYUP,
                dwExtraInfo = Win32.InjectedMarker,
            },
        },
    };
}
