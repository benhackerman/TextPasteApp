# TextPaste — clipboard con esteroides

Mantén un atajo de teclado y aparece una **ruleta bajo el puntero** con textos para pegar.
Al soltar el atajo se pega lo elegido en la app donde estabas (SCADA, Excel, editor de PLC…).

Pensado para parametrizar módulos de control desde un **taglist en Excel** sin andar buscando
a mano el número de actuador que corresponde a cada tag del P&ID.

Hay **dos atajos**:

### Ruleta — `Ctrl + Shift + Espacio` (historial y favoritos)

| Acción | Gesto |
|---|---|
| Abrir | Mantener **Ctrl + Shift** y presionar **Espacio** |
| Elegir | Mover el mouse hacia un sector · teclas **1–8** · flechas |
| Pegar | **Soltar** Ctrl + Shift (o clic izquierdo, o Enter) |
| Página siguiente | **Rueda** |
| Historial ↔ Favoritos | **Tab** o clic medio |
| Cambiar de plantilla | **Clic derecho** o **T** |
| Buscar | **Toque rápido** del atajo: queda abierta con un cuadro de búsqueda |
| Cancelar | **Esc** o soltar con el mouse en el centro |

### Taglist — `Ctrl + Shift + T` (buscador con autocompletar)

1. Clic en el campo del SCADA donde va el módulo.
2. **Ctrl + Shift + T** → se abre el buscador en la última hoja usada, listo para escribir.
3. Escribe lo que sepas: tag, número, parte de la descripción. Busca en **todas las columnas** de la fila.
4. **↑ ↓** cambian de fila · **Rueda** elige qué columna de esa fila pegar (parte en la columna por defecto de la hoja) · **→** acepta el autocompletado.
5. **Enter** pega y se cierra.

| Otras teclas | |
|---|---|
| **Tab / Shift+Tab** | Hoja siguiente / anterior («Todas» busca en el libro completo) |
| **Ctrl + 1…9** | Hoja directa (Ctrl + 0 = Todas) |
| **F2 / Ctrl+T / botón** | Cambiar plantilla |
| **Ctrl + Rueda** | Opacidad de la ventana |
| **Ctrl + Tab** | Pasar a la ruleta (historial/favoritos) |
| **Esc** o clic fuera | Cancelar |

**Perfiles de taglist:** describen cómo es tu Excel (qué hojas usar, fila de encabezados, columna a pegar,
columnas visibles). Se configuran una vez con «Detectar hojas»; en cada proyecto nuevo sólo cambias el archivo.
Se pueden exportar/importar para usarlos en otro computador.

Las **plantillas** envuelven el valor antes de pegarlo: `PLC1.MC_{valor}.CMD` + valor `23` → `PLC1.MC_23.CMD`.

La configuración se abre con doble clic en el ícono de la bandeja (junto al reloj).
Todo se guarda en `%AppData%\TextPasteApp\` (`settings.json`, `history.json`, `errors.log`).

## Probarlo

**Sin compilar:** en GitHub → pestaña *Actions* → última ejecución verde de *Build* → descarga
`TextPasteApp-win-x64` (un solo `.exe`, no necesita instalar .NET).

**Desde el código** (Windows, .NET 8 SDK o Visual Studio 2022):

```powershell
dotnet run --project src/TextPasteApp
dotnet test tests/TextPasteApp.Core.Tests
```

## Estructura

```
src/
  TextPasteApp.Core/        Lógica pura, sin Windows → se puede testear en cualquier lado
    Models/                 Datos: AppSettings, TaglistProfile, TaglistBook/Sheet/Row, Snippet, TextTemplate
    Logic/                  ChordTracker (atajos), OverlaySession (ruleta), TaglistSession (buscador),
                            TemplateEngine, TextSearch, ClipboardHistory, RadialGeometry
    Taglist/                Lectura de Excel/CSV con varias hojas, detección de encabezados, caché
    Storage/                JsonStore: guarda/lee JSON en %AppData%
  TextPasteApp/             La app WPF (code-behind, sin MVVM)
    AppController.cs        El "cerebro": conecta hooks → sesión → ventana → pegar
    Native/                 Todo lo de Win32: hooks de teclado/mouse, portapapeles, Ctrl+V
    Overlay/                La ruleta (se dibuja en código desde OverlaySession)
    Taglist/                Ventana del buscador del taglist
    Settings/               Ventana de configuración
    Tray/                   Ícono de la bandeja
tests/TextPasteApp.Core.Tests/   Tests xUnit de la lógica
docs/PLAN.md                Plan, decisiones y próximos pasos
```

Regla simple: **si no necesita Windows, va en Core y lleva test**. La app WPF sólo dibuja y habla con Windows.
