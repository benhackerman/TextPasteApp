# Plan de TextPaste

## Por qué se dejó MVVM

El proyecto original tenía MVVM + inyección de dependencias + CommunityToolkit. Para una app de bandeja
con una ventana especial (la ruleta) eso agregaba capas sin beneficio. La estructura nueva es:

- **Core** (`net8.0`): toda la lógica que se puede razonar y testear sin Windows. Clases chicas, una responsabilidad cada una.
- **App** (`net8.0-windows`, WPF): code-behind directo. Un solo `AppController` que conecta las piezas.
  Sin contenedores de DI: los objetos se crean con `new` en `AppController.Start()`.

## Cómo fluye una pegada

```
Tecla (hook global) ──► ChordTracker ──► señal "Opened"
                                          │
                     AppController.Open() ├─ guarda la ventana con foco
                                          ├─ arma OverlaySession (historial, favoritos, taglist)
                                          └─ RadialMenuWindow.Open() bajo el puntero (sin robar foco)
Mouse (hook global) ──► sector / rueda / clics ──► OverlaySession ──► Render()
Soltar modificadores ─► señal "Released" ──► texto = plantilla(seleccionado)
                                          └─ Paster: portapapeles + Ctrl+V en la ventana original
                                             (y opcionalmente restaura el portapapeles anterior)
```

Si se suelta muy rápido (< 350 ms) sin haber hecho nada → **modo buscador**: la ventana toma el foco,
aparece un cuadro de búsqueda, Enter pega y la app de destino recupera el foco antes del Ctrl+V.

## Decisiones técnicas

| Tema | Decisión | Motivo |
|---|---|---|
| Atajo | Hook de teclado de bajo nivel (`WH_KEYBOARD_LL`) | `RegisterHotKey` no avisa cuando se *suelta* la combinación |
| Rueda/clics | Hook de mouse sólo mientras la ruleta está abierta | Se "traga" la rueda para que no haga scroll en la app de atrás |
| Foco | Ventana `WS_EX_NOACTIVATE` | La app de destino nunca pierde el foco → el Ctrl+V cae donde corresponde |
| Alt/Win en el atajo | Se envía una tecla sin uso (0xE8) | Evita que soltar Alt abra el menú o Win abra Inicio |
| Excel | `ExcelDataReader` en modo compartido | Lee .xlsx/.xls/.csv aunque el archivo esté abierto en Excel; liviano |
| Taglist grande | Caché + recarga sólo si cambia la fecha del archivo; búsqueda normalizada | 10.000+ filas sin demora; "xv1023" encuentra "XV-1023" |
| DPI | Manifest PerMonitorV2 + posición en píxeles físicos | Ruleta nítida y centrada en monitores con distinto escalado |
| Distribución | `.exe` único autocontenido desde GitHub Actions | Se puede probar en el PC del trabajo sin instalar nada |

## Estado (v0.1 — esta rama)

- [x] Reestructura completa (Core + App + tests + CI)
- [x] Historial del portapapeles (sin duplicados, límite, se guarda)
- [x] Favoritos editables
- [x] Taglist desde Excel/CSV: hoja, fila de encabezados, columnas por nombre o letra, vista previa
- [x] Plantillas con `{valor}`, `{tag}`, `{desc}`, `{Columna}`, `{valor:0000}`
- [x] Ruleta: modo sectores + modo lista (tambor), rueda, Tab, clic derecho, Esc
- [x] Modo buscador con toque rápido
- [x] Bandeja: configuración, recargar taglist, pausar, salir
- [x] 44 tests de la lógica

## Para probar en Windows (checklist)

Cosas que no se pueden verificar sin Windows real; anota lo que falle y lo vemos:

1. Abrir con Ctrl+Shift+Espacio sobre Bloc de notas → la ruleta aparece centrada en el puntero.
2. Copiar 3–4 textos, abrir, apuntar a uno, soltar → se pega en el Bloc de notas.
3. Rueda con la ruleta abierta → cambia de página y **no** hace scroll en la app de atrás.
4. Tab → Favoritos → Taglist. Clic derecho → cambia la plantilla (chip abajo).
5. Toque rápido → queda abierta con buscador; escribir parte de un tag; Enter → pega.
6. Configurar el taglist real: elegir archivo, hoja, fila de encabezados, columnas → *Vista previa*.
7. Probar con el Excel abierto al mismo tiempo.
8. Probar en el SCADA real (algunos programas antiguos leen el portapapeles lento: si pega lo anterior,
   desactivar "devolver al portapapeles lo que había antes").
9. Monitor externo con escalado distinto (125 % / 150 %).
10. Si la app corre como usuario normal y el SCADA como administrador, Windows bloquea los hooks:
    en ese caso hay que abrir TextPaste también como administrador.

## Próximos pasos (ideas, en orden sugerido)

1. **Ajustes tras la primera prueba real** (velocidad del toque, tamaño de la ruleta, textos).
2. **Recientes del taglist**: los últimos tags usados primero en la lista.
3. **Filtro por columna/área** del taglist (p. ej. sólo área 310) para listas gigantes.
4. **Varias plantillas por fuente** (una por defecto para taglist y otra para historial).
5. **Secuencias**: pegar varios campos seguidos con Tab entre ellos (tag → Tab → valor → Tab → descripción).
6. **Colores por favorito** (lo tenía el diseño original) y favoritos fijados desde el historial con un clic.
7. Instalador / auto-actualización.
