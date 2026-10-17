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

## v0.5 — vistas compactas y AWL que entiende variables

- Todas las vistas flotantes compactas: ruleta de 400 px sin línea de ayuda; buscador de funciones sin ayuda
  (va en el tooltip de la vista previa), 5 resultados y 8 líneas de vista previa.
- `AwlParser`: reconoce instrucción / variable / constante / etiqueta / comentario línea por línea
  (la primera palabra es la instrucción; resuelve L, T y A ambiguos). Mnemónicos alemanes e ingleses.
- Cambiar una variable la reemplaza en todo el bloque (DB12 también en sus accesos DB12.DBX…).
- Vista previa coloreada; editor convierte variables en parámetros.

## v0.4 — funciones (código AWL)

- Biblioteca de funciones (functions.json): nombre, categoría, descripción, código. Se incluye en el respaldo.
- Buscador compacto con atajo propio (Ctrl+Shift+F), vista previa y parámetros `{{nombre}}`
  (llaves dobles porque AWL usa `$` en sus textos).
- Parámetros rellenados desde la última fila del taglist (mismo nombre de columna) o el último valor usado.
- Ctrl+F en el taglist abre las funciones con esa fila; Ctrl+N guarda lo copiado como función.
- Estadísticas: pegados de funciones y su tiempo ahorrado (60 s por defecto).

## v0.3 — para usarla siempre

- **Secuencias** (Shift+Enter): pegar varias columnas con Tab entre ellas; configurable por hoja en el perfil.
- **Avance por proyecto**: pegar marca la fila ✓; Ctrl+D marca/desmarca; contador ✓ hechas/total en el buscador.
- **Recientes primero** (↺) cuando no has escrito nada.
- **Hoja automática**: si no hay coincidencias en la hoja actual, se muestran las de todas («TODAS (auto)»).
- **Proyectos** (archivo + perfil): se eligen solos por el título de la ventana, por el Excel abierto o el último usado; F4 cambia.
- **Estadísticas** (usage.json, agregadas por elemento y por día): pegados, tiempo ahorrado estimado, top, gráfico 30 días, avance.
- **Historial**: fijar elementos (P / Ctrl+P / Configuración), no guarda textos marcados como privados por gestores de
  contraseñas ni textos muy largos.
- **Instalador** Inno Setup por usuario + **actualización automática** desde GitHub Releases + **respaldo** completo.

## Modo taglist (v0.2)

Decidido con el usuario:
- **Dos atajos**: ruleta (Ctrl+Shift+Espacio) y taglist (Ctrl+Shift+T, abre directo el buscador).
- **Rueda = columna** de la fila; ↑↓ = fila. Cada fila parte en la «columna a pegar» de su hoja.
- **Hoja inicial = la última usada**. Tab recorre las hojas (incluida «Todas»), Ctrl+1…9 salta directo.
- La búsqueda mira **todas las columnas visibles** de la fila. Orden: celda idéntica → empieza igual → contiene.
- **Perfiles de taglist** (las "plantillas de taglist"): hojas, fila de encabezados, columna a pegar y columnas visibles.
  «Detectar hojas» propone todo (encabezado = primera fila con 2+ textos; columna a pegar = la que empieza con N°/Nº/NUM/ID).
- **Opacidad** ajustable (40–100 %) para ruleta y taglist; en el taglist también con Ctrl+rueda.
- Probado con un taglist real (hojas Act, DIn, AIn, PID, Msg, Cnt, Switch, Unit… con columnas No | IO | Code | Text_0…):
  se reconoce `Code` como tag y `Text_0` como descripción, se ocultan las filas reservadas (sólo No/IO),
  la rueda salta columnas vacías, y se desmarcan hojas vacías o de configuración.
- Mejoras a la ruleta: teclas 1–8 eligen y pegan un sector directo (con su número dibujado).

## Estado (v0.1)

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
6. Configurar el taglist real: Examinar → «Detectar hojas» → revisar la columna a pegar de cada hoja → *Vista previa*.
6b. Ctrl+Shift+T en el SCADA: escribir parte de un tag, ↑↓, rueda, Tab entre hojas, Enter → pega en el campo.
7. Probar con el Excel abierto al mismo tiempo.
8. Probar en el SCADA real (algunos programas antiguos leen el portapapeles lento: si pega lo anterior,
   desactivar "devolver al portapapeles lo que había antes").
9. Monitor externo con escalado distinto (125 % / 150 %).
10. Si la app corre como usuario normal y el SCADA como administrador, Windows bloquea los hooks:
    en ese caso hay que abrir TextPaste también como administrador.

## Próximos pasos (ideas, en orden sugerido)

1. **Ajustes tras la primera prueba real** (velocidad del toque, tamaño de la ruleta, textos).
2. Historial: mostrar de qué programa se copió y vista previa del texto completo al apuntar.
3. Secuencias con otras teclas entre valores (Enter, flechas) según el formulario del SCADA.
3. **Filtro por columna/área** del taglist (p. ej. sólo área 310) para listas gigantes.
4. **Varias plantillas por fuente** (una por defecto para taglist y otra para historial).
5. **Secuencias**: pegar varios campos seguidos con Tab entre ellos (tag → Tab → valor → Tab → descripción).
6. **Colores por favorito** (lo tenía el diseño original) y favoritos fijados desde el historial con un clic.
7. Instalador / auto-actualización.
