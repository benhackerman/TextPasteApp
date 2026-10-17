# TextPaste — clipboard con esteroides

Mantén un atajo de teclado y aparece una **ruleta bajo el puntero** con textos para pegar.
Al soltar el atajo se pega lo elegido en la app donde estabas (SCADA, Excel, editor de PLC…).

Pensado para parametrizar módulos de control desde un **taglist en Excel** sin andar buscando
a mano el número de actuador que corresponde a cada tag del P&ID.

Hay **tres atajos**: ruleta, taglist y funciones.

### Ruleta — `Ctrl + Shift + Espacio` (historial y favoritos)

| Acción | Gesto |
|---|---|
| Abrir | Mantener **Ctrl + Shift** y presionar **Espacio** |
| Elegir | Mover el mouse hacia un sector · teclas **1–8** · flechas |
| Pegar | **Soltar** Ctrl + Shift (o clic izquierdo, o Enter) |
| Página siguiente | **Rueda** |
| Historial ↔ Favoritos | **Tab** o clic medio |
| Cambiar de plantilla | **Clic derecho** o **T** |
| Fijar en el historial | **P** (en modo buscador: Ctrl+P) |
| Buscar | **Toque rápido** del atajo: queda abierta con un cuadro de búsqueda |
| Cancelar | **Esc** o soltar con el mouse en el centro |

### Taglist — `Ctrl + Shift + T` (buscador con autocompletar)

1. Clic en el campo del SCADA donde va el módulo.
2. **Ctrl + Shift + T** → aparece justo bajo el puntero una ventana compacta: una línea para escribir y una
   tabla con los resultados (en la última hoja usada). Filtra con cada letra.
3. Escribe lo que sepas: tag, número, parte de la descripción. Busca en **todas las columnas** de la fila.
4. La **celda verde** es lo que se pega. **↑ ↓** cambian de fila · **Rueda** cambia de columna (parte en la columna por defecto de la hoja) · **→** acepta el autocompletado.
5. **Enter** pega y se cierra.

| Otras teclas | |
|---|---|
| **Shift + Enter** | Pega la **secuencia** de la hoja: varios valores con Tab entre ellos (llena el formulario de una pasada) |
| **Ctrl + D** | Marca / desmarca la fila como hecha ✓ (pegar ya la marca sola) |
| **Tab / Shift+Tab** | Hoja siguiente / anterior («Todas» busca en el libro completo) |
| **Ctrl + 1…9** | Hoja directa (Ctrl + 0 = Todas) |
| **F4** | Cambiar de proyecto (otro taglist) |
| **Ctrl + F** | Usar la fila en una **función** de código (rellena sus parámetros) |
| **F2 / Ctrl+T** | Cambiar plantilla |
| **Ctrl + Rueda** | Opacidad de la ventana |
| **Ctrl + Tab** | Pasar a la ruleta (historial/favoritos) |
| **Esc** o clic fuera | Cancelar |

En la tabla: **✓** = fila ya parametrizada en este proyecto, **↺** = usada hace poco (sin escribir nada, las
recientes salen primero). Si lo que escribes no está en la hoja actual, se busca solo en todas («TODAS (auto)»).

### ¿Cómo sabe qué taglist usar?

Cada **proyecto** es un archivo de taglist + un perfil. Al presionar el atajo se elige solo:
1. si el título de la ventana donde vas a pegar (tu SCADA) contiene una palabra de «Reconocer en la ventana»;
2. si no, el proyecto cuyo Excel tengas abierto;
3. si no, el último que usaste. **F4** cambia a mano; también desde el ícono de la bandeja → Proyecto.

**Perfiles de taglist:** describen el formato del Excel (hojas, fila de encabezados, columna a pegar,
columnas visibles, secuencia, columnas ocultas como `Text_1, Text_2`). Se configuran una vez con «Detectar hojas»
y los comparten todos los proyectos con ese formato. Se pueden exportar/importar.

### Funciones — `Ctrl + Shift + F` (código AWL guardado)

Guarda bloques de código para pegarlos de nuevo y búscalos por nombre, categoría, descripción o contenido.

- **Guardar:** copia el código y en el buscador presiona **Ctrl+N** (o bandeja → «Guardar lo copiado como función…»).
  También desde Configuración → Funciones (crear, editar, importar archivos `.awl`, exportar).
- **Parámetros `{{nombre}}`:** se piden al pegar y se recuerda el último valor. Si se llaman como una columna del taglist
  (`{{No}}`, `{{Code}}`, `{{Text_0}}`, `{{valor}}`…) se rellenan solos con la última fila elegida.
- **Desde el taglist:** busca el actuador, **Ctrl+F** → elige la función → Enter: pega el bloque AWL de ese actuador.

```
// {{Code}} - {{Text_0}}
      CALL  FB   100 , DB{{No}}
       Cmd :=M{{No}}.0
```

| En el buscador de funciones | |
|---|---|
| Escribir | Filtra al instante |
| ↑↓ / rueda | Elegir función |
| Tab | Ir a los parámetros (verde = tomado del taglist) |
| Enter | Pegar |
| Ctrl+N / Ctrl+E | Nueva desde lo copiado / editar |

### Estadísticas

Bandeja → **Estadísticas**: pegados de hoy, de la semana y totales, **tiempo ahorrado** (estimado, ajustable),
gráfico de los últimos 30 días, lo que más pegas y el **avance de cada proyecto** (filas ✓ por hoja).

### Para usarla siempre

- **Instalador** sin permisos de administrador (`TextPaste-x.y.z-Setup.exe`), con inicio automático con Windows.
- **Se actualiza sola**: busca versiones nuevas en GitHub al iniciar; bandeja → «Actualizar».
- **Respaldo**: Configuración → General → Exportar/Importar respaldo (configuración, historial y estadísticas).
- Historial: **fijar** elementos (tecla P en la ruleta) y no se guardan contraseñas copiadas desde gestores como KeePass.

Las **plantillas** envuelven el valor antes de pegarlo: `PLC1.MC_{valor}.CMD` + valor `23` → `PLC1.MC_23.CMD`.

La configuración se abre con doble clic en el ícono de la bandeja (junto al reloj).
Todo se guarda en `%AppData%\TextPasteApp\` (`settings.json`, `history.json`, `errors.log`).

## Probarlo

**Instalar:** en GitHub → *Releases* → última versión → `TextPaste-x.y.z-Setup.exe`.
(O sin instalar: *Actions* → última ejecución verde de *Build* → `TextPasteApp-win-x64`, trae el `.exe` suelto y el instalador.)

**Publicar una versión nueva** (para que la app se actualice sola): subir `<Version>` en
`src/TextPasteApp/TextPasteApp.csproj`, y en GitHub → *Actions* → *Build* → *Run workflow* → marcar «Publicar».

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
