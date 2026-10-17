# TextPaste — clipboard con esteroides

Mantén un atajo de teclado y aparece una **ruleta bajo el puntero** con textos para pegar.
Al soltar el atajo se pega lo elegido en la app donde estabas (SCADA, Excel, editor de PLC…).

Pensado para parametrizar módulos de control desde un **taglist en Excel** sin andar buscando
a mano el número de actuador que corresponde a cada tag del P&ID.

| Fuente | Qué muestra | Cómo se navega |
|---|---|---|
| **Historial** | Lo último que copiaste (Ctrl+C) | Ruleta de 8 sectores; la rueda cambia de página |
| **Favoritos** | Lista fija que tú ajustas | Igual que el historial |
| **Taglist** | Filas del Excel: busca por tag, pega el valor | Lista tipo tambor; la rueda avanza fila por fila; buscador |

Las **plantillas** envuelven el valor antes de pegarlo: `PLC1.MC_{valor}.CMD` + valor `23` → `PLC1.MC_23.CMD`.

## Uso

| Acción | Gesto |
|---|---|
| Abrir | Mantener **Ctrl + Shift** y presionar **Espacio** (configurable) |
| Elegir | Mover el mouse hacia un sector · rueda · flechas |
| Pegar | **Soltar** Ctrl + Shift (o clic izquierdo, o Enter) |
| Cambiar de fuente | **Tab** o clic medio |
| Cambiar de plantilla | **Clic derecho** o **T** |
| Buscar (modo buscador) | **Toque rápido** del atajo: la ruleta queda abierta con un cuadro de búsqueda |
| Cancelar | **Esc** o soltar con el mouse en el centro |

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
    Models/                 Datos: AppSettings, Snippet, TagRow, TextTemplate, PickItem
    Logic/                  ChordTracker (atajo), OverlaySession (estado de la ruleta),
                            TemplateEngine, TextSearch, ClipboardHistory, RadialGeometry
    Taglist/                Lectura de Excel/CSV (ExcelDataReader) + caché que recarga si cambia
    Storage/                JsonStore: guarda/lee JSON en %AppData%
  TextPasteApp/             La app WPF (code-behind, sin MVVM)
    AppController.cs        El "cerebro": conecta hooks → sesión → ventana → pegar
    Native/                 Todo lo de Win32: hooks de teclado/mouse, portapapeles, Ctrl+V
    Overlay/                La ruleta (se dibuja en código desde OverlaySession)
    Settings/               Ventana de configuración
    Tray/                   Ícono de la bandeja
tests/TextPasteApp.Core.Tests/   Tests xUnit de la lógica
docs/PLAN.md                Plan, decisiones y próximos pasos
```

Regla simple: **si no necesita Windows, va en Core y lleva test**. La app WPF sólo dibuja y habla con Windows.
